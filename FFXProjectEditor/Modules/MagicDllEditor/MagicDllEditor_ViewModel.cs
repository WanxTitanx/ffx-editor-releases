using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.FfxLib.MagicDll;
using FfxMagicFieldType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor
{
    /// <summary>
    /// ViewModel do Magic DLL Editor (reescrito 2026-08-01 — Jarvis-PPP-C2C3).
    /// Padrão: CommunityToolkit ObservableObject + [ObservableProperty] + [RelayCommand].
    /// Fluxo: Abrir → parse via MagicDllDocument_Wrapper (Root → Descriptors → Programs →
    /// Slots → Fields) → editar campos (janela provada) → Salvar cópia (backup + SHA) |
    /// RT0 check | Adicionar campo (grow de record — WRITEBACK_SPEC §2.2) | Reverter.
    /// A árvore usa os nós de MagicDllEditor_TreeNodes (MagicNode), que o XAML consome
    /// via Label/MetaText/Children; campos são MagicFieldNode com SourceField wired.
    /// Segurança: salvar SEMPRE em cópia (nunca Steam Library); grow = RISCO ALTO.
    /// </summary>
    internal partial class MagicDllEditor_ViewModel : ObservableObject
    {
        private readonly MagicDllDocument_Wrapper _document = new();
#if FFX_INCLUDE_DEVTOOLS
        private readonly HashSet<MagicSlot> _pendingGrowSlots = new();
#endif

        /// <summary>
        /// Gate de serialização dos comandos mutáveis (Open/SaveCopy/AddField/Revert):
        /// impede corrida entre eles (ex.: abrir durante salvar corromperia os working
        /// bytes). Thread-safety 2026-08-01.
        /// </summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>Caminho do arquivo aberto (vazio sem documento).</summary>
        [ObservableProperty]
#if FFX_INCLUDE_DEVTOOLS
        [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand), nameof(Rt0CheckCommand), nameof(RevertCommand), nameof(AddFieldCommand))]
#else
        [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand), nameof(Rt0CheckCommand), nameof(RevertCommand))]
#endif
        private string filePath = string.Empty;

        /// <summary>Nome do efeito (catálogo noclip). Ex.: "Power Break".</summary>
        [ObservableProperty] private string? effectName;

        /// <summary>Nó selecionado na TreeView (alvo dos comandos contextuais).</summary>
        [ObservableProperty]
#if FFX_INCLUDE_DEVTOOLS
        [NotifyCanExecuteChangedFor(nameof(AddFieldCommand))]
#endif
        private MagicNode? selectedNode;

        /// <summary>SHA-256 (hex) do arquivo original lido.</summary>
        [ObservableProperty] private string shaBefore = string.Empty;

        /// <summary>SHA-256 (hex) dos working bytes atuais.</summary>
        [ObservableProperty] private string shaAfter = string.Empty;

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>Resumo dos slots pendentes de persistência de grow.</summary>
        [ObservableProperty] private string pendingGrowSummary = string.Empty;

        /// <summary>Largura do grow (W ∈ {4,8,12,16}) — default 4 (1 float).</summary>
        [ObservableProperty] private int growWidth = 4;

        /// <summary>Índice do ComboBox de largura do grow: 0→4, 1→8, 2→12, 3→16.</summary>
        public int GrowWidthComboIndex
        {
            get => GrowWidth switch { 8 => 1, 12 => 2, 16 => 3, _ => 0 };
            set => GrowWidth = value switch { 1 => 8, 2 => 12, 3 => 16, _ => 4 };
        }


        /// <summary>Fator de mutação da simulação U1 (×delta aplicado ao slot alvo).</summary>
        [ObservableProperty] private double simFactor = 2.0;
#endif

        /// <summary>Filtro de slots por família/opcode (ex.: "pppSclMove", "Rand", "Draw"). Vazio = todos.</summary>
        [ObservableProperty] private string slotFilter = string.Empty;

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>Número de frames simulados (60 = ~1s de runtime).</summary>
        [ObservableProperty] private int simFrames = 60;

        /// <summary>Resumo da última simulação U1 (slots, razão final, aviso simulação ≠ jogo).</summary>
        [ObservableProperty] private string simSummary = string.Empty;

        /// <summary>Tabela frame → escala X antes × depois (overlay) da última simulação.</summary>
        [ObservableProperty] private string simTable = string.Empty;
#endif

        /// <summary>Resumo das mutações ativas vs vanilla (painel "Editor de clone" — read-only).</summary>
        [ObservableProperty] private string cloneDiffSummary = string.Empty;

        /// <summary>Detalhe das mutações ativas (records mutados com SHA antes/depois).</summary>
        [ObservableProperty] private string cloneDiffDetail = string.Empty;

        /// <summary>URL do visualizador 3D embutido (mini navegador) — o WebView2 navega para cá quando o preview é aberto.</summary>
        [ObservableProperty] private string previewUrl = string.Empty;
        [ObservableProperty] private int previewToolTab;

        /// <summary>true quando há URL de preview para exibir (mostra o mini navegador embutido).</summary>
        public bool HasPreviewUrl => !string.IsNullOrEmpty(PreviewUrl);

        /// <summary>true fora do Windows: sem WebView2 — o painel mostra o card de fallback e a URL
        /// do preview abre no navegador do sistema.</summary>
        public bool IsExternalViewer =>
            !Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported;

        partial void OnPreviewUrlChanged(string value)
        {
            OnPropertyChanged(nameof(HasPreviewUrl));
            ReopenPreviewCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(HasPreviewUrl))]
        private void ReopenPreview() => Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(PreviewUrl);

        /// <summary>true quando há resumo de diff do clone para exibir.</summary>
        public bool HasCloneDiff => !string.IsNullOrEmpty(CloneDiffSummary);

        partial void OnCloneDiffSummaryChanged(string value) => OnPropertyChanged(nameof(HasCloneDiff));

        /// <summary>Raízes da TreeView (uma por root PPP do .data).</summary>
        public ObservableCollection<MagicNode> RootNodes { get; } = new();

        /// <summary>Pilha "voltar" do histórico de navegação (nós visitados antes do atual).</summary>
        private readonly List<MagicNode> _backStack = new();

        /// <summary>Pilha "ir para frente" do histórico (reaberta após voltar).</summary>
        private readonly List<MagicNode> _forwardStack = new();

        /// <summary>Último nó registrado no histórico (dedupe de seleção repetida).</summary>
        private MagicNode? _lastHistoryNode;

        /// <summary>true quando a mudança de SelectedNode é programática (back/forward) — não registra histórico.</summary>
        private bool _suppressHistory;

        /// <summary>true quando há nó anterior no histórico (habilita o botão "voltar").</summary>
        public bool CanNavigateBack => _backStack.Count > 0;

        /// <summary>true quando há nó à frente no histórico (habilita o botão "ir para frente").</summary>
        public bool CanNavigateForward => _forwardStack.Count > 0;

        /// <summary>Texto do breadcrumb (caminho real do nó na árvore: "Root / Programs / P3").</summary>
        public string BreadcrumbText
        {
            get
            {
                if (SelectedNode is null)
                    return "";
                var labels = new List<string>();
                MagicNode? cur = SelectedNode;
                while (cur is not null)
                {
                    labels.Add(cur.Label);
                    cur = FindParent(cur);
                }
                labels.Reverse();
                return string.Join(" / ", labels);
            }
        }

        /// <summary>Localiza o pai de <paramref name="node"/> na árvore (DFS) — usado pelo breadcrumb.</summary>
        private MagicNode? FindParent(MagicNode node)
        {
            foreach (MagicNode root in RootNodes)
            {
                MagicNode? parent = FindParentIn(root, node);
                if (parent is not null)
                    return parent;
            }
            return null;
        }

        private static MagicNode? FindParentIn(MagicNode current, MagicNode target)
        {
            foreach (MagicNode child in current.Children)
            {
                if (ReferenceEquals(child, target))
                    return current;
                MagicNode? deeper = FindParentIn(child, target);
                if (deeper is not null)
                    return deeper;
            }
            return null;
        }


        /// <summary>Log de operações (bind do painel — entradas com nível p/ cor).</summary>
        public ObservableCollection<MagicLogEntry> Log { get; } = new();

        /// <summary>Wrapper técnico (parse/bytes/RT0/grow/backup) — exposto p/ code-behind e testes.</summary>
        public MagicDllDocument_Wrapper Document => _document;

        /// <summary>File picker de abertura; retorna o caminho escolhido ou null se cancelado.</summary>
        public Func<Task<string?>>? OpenFilePicker { get; set; }

        /// <summary>Save picker da cópia; retorna o caminho de destino ou null se cancelado.</summary>
        public Func<Task<string?>>? SaveFilePicker { get; set; }

        /// <summary>Save picker do clone; retorna o caminho do novo magic_&lt;id&gt;.dll ou null se cancelado.</summary>
        public Func<Task<string?>>? CloneFilePicker { get; set; }

        /// <summary>
        /// UI confirmation required before restoring backups created by legacy preview builds.
        /// A missing host is fail-closed: the recovery command does not touch user data.
        /// </summary>
#if FFX_INCLUDE_DEVTOOLS
        public Func<Task<bool>>? ConfirmLegacyPreviewRestore { get; set; }

        /// <summary>Injectable boundary keeps the destructive legacy-recovery path testable.</summary>
        internal Func<(int Restored, string Detail)> RestoreLegacyPreviewAction { get; set; }
            = RestoreLegacyPreview;
#endif

        /// <summary>Título do documento: "magic_0021.dll — Power Break".</summary>
        public string DocumentTitle
        {
            get
            {
                if (FilePath.Length == 0)
                    return "No file open.";
                string fileName = Path.GetFileName(FilePath);
                return !string.IsNullOrEmpty(EffectName) ? $"{fileName} — {EffectName}" : fileName;
            }
        }

        /// <summary>Status principal (dirty + SHA).</summary>
        public string StatusText
        {
            get
            {
                if (!HasDocument)
                    return "Open a magic_XXXX.dll to start. Saving is always to a copy you choose.";
                string dirty = IsDirty ? "● modified" : "clean";
                return $"SHA before {ShortSha(ShaBefore)} · after {ShortSha(ShaAfter)} · {dirty}";
            }
        }

        /// <summary>true quando há documento carregado (CanExecute dos comandos).</summary>
        public bool HasDocument => _document.HasDocument;

        /// <summary>true only for a verified save pair currently owned by this document.</summary>
        public bool HasBackup => _document.HasRestorableBackup;

        /// <summary>true quando há edições/grow em memória (indicador de dirty).</summary>
        public bool IsDirty => _document.IsDirty;

        /// <summary>true quando há nó selecionado (painel de edição visível).</summary>
        public bool HasSelection => SelectedNode is not null;

        /// <summary>true quando NÃO há nó selecionado (placeholder do painel).</summary>
        public bool NoSelection => SelectedNode is null;

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>Adicionar campo exige um slot selecionado na árvore + documento.</summary>
        public bool CanAddField => SelectedNode is MagicSlotNode { Slot: not null } && HasDocument;
#endif

        partial void OnSelectedNodeChanged(MagicNode? value)
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(NoSelection));
#if FFX_INCLUDE_DEVTOOLS
            OnPropertyChanged(nameof(CanAddField));
#endif
            OnPropertyChanged(nameof(BreadcrumbText));
            if (_suppressHistory)
                return;

            // Registra no histórico estilo navegador: o nó anterior vai para a pilha "voltar"
            // e a pilha "ir para frente" zera (novo caminho explorado).
            if (_lastHistoryNode is not null && !ReferenceEquals(_lastHistoryNode, value))
            {
                _backStack.Add(_lastHistoryNode);
                if (_backStack.Count > 200)
                    _backStack.RemoveAt(0); // trava de memória — histórico curto
                _forwardStack.Clear();
                OnPropertyChanged(nameof(CanNavigateBack));
                OnPropertyChanged(nameof(CanNavigateForward));
            }
            _lastHistoryNode = value;
        }

        /// <summary>
        /// Navega para um filho (clicou numa "pasta" da visão de pasta): seleciona o filho
        /// (o histórico é registrado no OnSelectedNodeChanged) e auto-expande a árvore até ele.
        /// </summary>
        [RelayCommand]
        private void NavigateInto(MagicNode child)
        {
            if (child is null)
                return;
            SelectedNode = child;
            ExpandPathTo(child);
        }

        /// <summary>Volta para o nó visitado antes (botão ⬅) — como o "voltar" do navegador.</summary>
        [RelayCommand]
        private void NavigateBack()
        {
            if (_backStack.Count == 0)
                return;
            _suppressHistory = true;
            try
            {
                MagicNode? current = SelectedNode;
                MagicNode target = _backStack[_backStack.Count - 1];
                _backStack.RemoveAt(_backStack.Count - 1);
                if (current is not null)
                    _forwardStack.Add(current);
                _lastHistoryNode = target;
                SelectedNode = target;
                ExpandPathTo(target);
            }
            finally
            {
                _suppressHistory = false;
            }
            OnPropertyChanged(nameof(CanNavigateBack));
            OnPropertyChanged(nameof(CanNavigateForward));
            OnPropertyChanged(nameof(BreadcrumbText));
        }

        /// <summary>Vai para o nó que estava antes de voltar (botão ➡) — como o "avançar" do navegador.</summary>
        [RelayCommand]
        private void NavigateForward()
        {
            if (_forwardStack.Count == 0)
                return;
            _suppressHistory = true;
            try
            {
                MagicNode? current = SelectedNode;
                MagicNode target = _forwardStack[_forwardStack.Count - 1];
                _forwardStack.RemoveAt(_forwardStack.Count - 1);
                if (current is not null)
                    _backStack.Add(current);
                _lastHistoryNode = target;
                SelectedNode = target;
                ExpandPathTo(target);
            }
            finally
            {
                _suppressHistory = false;
            }
            OnPropertyChanged(nameof(CanNavigateBack));
            OnPropertyChanged(nameof(CanNavigateForward));
            OnPropertyChanged(nameof(BreadcrumbText));
        }

        /// <summary>
        /// Revela um nó na árvore (fly-to): expande a cadeia de ancestrais + seleciona.
        /// Usado pelo breadcrumb clicável e pelo double-click na árvore.
        /// </summary>
        [RelayCommand]
        private void FlyTo(MagicNode node)
        {
            if (node is null)
                return;
            SelectedNode = node;
            ExpandPathTo(node);
        }

        /// <summary>Expande os ancestrais de <paramref name="target"/> na árvore (IsExpanded=true).</summary>
        private void ExpandPathTo(MagicNode target)
        {
            foreach (MagicNode root in RootNodes)
            {
                if (TryExpandPath(root, target))
                    return;
            }
        }

        /// <summary>DFS: se a subárvore contém <paramref name="target"/>, expande os ancestrais e retorna true.</summary>
        private static bool TryExpandPath(MagicNode node, MagicNode target)
        {
            if (ReferenceEquals(node, target))
                return true;
            foreach (MagicNode child in node.Children)
            {
                if (TryExpandPath(child, target))
                {
                    node.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }

        partial void OnFilePathChanged(string value)
        {
            OnPropertyChanged(nameof(DocumentTitle));
            OnPropertyChanged(nameof(StatusText));
        }

        partial void OnEffectNameChanged(string? value)
        {
            OnPropertyChanged(nameof(DocumentTitle));
        }

        partial void OnShaBeforeChanged(string value) => OnPropertyChanged(nameof(StatusText));

        partial void OnShaAfterChanged(string value) => OnPropertyChanged(nameof(StatusText));

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>true quando há resumo de simulação para exibir.</summary>
        public bool HasSimSummary => !string.IsNullOrEmpty(SimSummary);

        /// <summary>true quando há tabela de simulação para exibir.</summary>
        public bool HasSimTable => !string.IsNullOrEmpty(SimTable);

        partial void OnSimSummaryChanged(string value)
        {
            OnPropertyChanged(nameof(HasSimSummary));
            ExportSimulationCommand.NotifyCanExecuteChanged();
        }

        partial void OnSimTableChanged(string value) => OnPropertyChanged(nameof(HasSimTable));
#endif

        /// <summary>Filtro de slots alterado → reconstrói a árvore (preservando seleção quando possível).</summary>
        partial void OnSlotFilterChanged(string value) => RebuildTreePreservingSelection();


        // --- Comandos (spec: OpenCommand, SaveCopyCommand, AddFieldCommand, Rt0CheckCommand, RevertCommand) ---

        /// <summary>Abrir DLL... → file picker → OpenFileAsync.</summary>
        [RelayCommand]
        private async Task OpenAsync()
        {
            if (OpenFilePicker is null)
            {
                AppendLog("No file picker available (host missing).", MagicLogLevel.Warn);
                return;
            }

            string? path = await OpenFilePicker();
            if (string.IsNullOrEmpty(path))
                return;

            await OpenFileAsync(path);
        }

        /// <summary>Abre e parseia um arquivo (público para o code-behind e testes headless).</summary>
        public async Task<bool> OpenFileAsync(string path)
        {
            await _gate.WaitAsync();
            try
            {
                await Task.CompletedTask; // parse é síncrono e rápido (arquivos ~450KB)
                if (!_document.TryLoad(path, out string error))
                {
                    AppendLog($"Failed to open {path}: {error}", MagicLogLevel.Error);
                    return false;
                }

                OnDocumentLoaded();
                MagicDllFile? file = _document.ParsedFile;
                int roots = file?.Roots.Count ?? 0;
                int programs = file?.Roots.Sum(r => r.Programs.Count) ?? 0;
                int slots = file?.Roots.Sum(r => r.Programs.Sum(p => p.Slots.Count)) ?? 0;
                AppendLog($"Opened: {path} — {roots} root(s), {programs} program(s), {slots} slot(s), SHA {ShortSha(ShaBefore)}…");
                if (EffectName is null)
                    AppendLog("Effect name is not catalogued; preview uses the loaded DLL resource.", MagicLogLevel.Warn);
                else
                    AppendLog($"Effect identified: {EffectName} (noclip catalog).");
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Salvar cópia... → aplica edições pendentes → save picker → grava a cópia (backup .bak + SHA).</summary>
        [RelayCommand(CanExecute = nameof(HasDocument))]
        private async Task SaveCopyAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (!HasDocument)
                {
                    AppendLog("No file open to save.", MagicLogLevel.Warn);
                    return;
                }

                int applied = ApplyPendingFieldEdits();
                if (applied < 0)
                    return; // erro já logado — aborta o save
                if (applied > 0)
                    AppendLog($"{applied} field(s) applied to the in-memory document (write-back confined to the window).");

                if (SaveFilePicker is null)
                {
                    AppendLog("No save picker available (host missing).", MagicLogLevel.Warn);
                    return;
                }

                string? path = await SaveFilePicker();
                if (string.IsNullOrEmpty(path))
                {
                    AppendLog("Save cancelled.");
                    return;
                }

                if (!_document.TrySaveCopy(path, out string saveError))
                {
                    AppendLog($"Save FAILED: {saveError}", MagicLogLevel.Error);
                    return;
                }

                UpdateShaDisplay();
                AppendLog($"Copy saved: {path} — SHA {ShortSha(ShaAfter)}... (backup .bak alongside)");

                foreach (MagicFieldNode field in CollectFieldNodes())
                    field.MarkSaved();
#if FFX_INCLUDE_DEVTOOLS
                _pendingGrowSlots.Clear();
                PendingGrowSummary = string.Empty;
#endif
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Clona a magic DLL atual para um novo magic_&lt;id&gt;.dll (FASE 1). Copia os working bytes
        /// (com as edições aplicadas) para um arquivo novo com um id escolhido pelo usuário.
        /// Não altera o documento aberto — o clone é um arquivo novo.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasDocument))]
        private async Task CloneMagicAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (!HasDocument)
                {
                    AppendLog("No file open to clone.", MagicLogLevel.Warn);
                    return;
                }

                int applied = ApplyPendingFieldEdits();
                if (applied < 0)
                    return; // erro já logado — aborta o clone
                if (applied > 0)
                    AppendLog($"{applied} field(s) applied to the in-memory document (write-back confined to the window).");

                if (CloneFilePicker is null)
                {
                    AppendLog("No clone picker available (host missing).", MagicLogLevel.Warn);
                    return;
                }

                string? path = await CloneFilePicker();
                if (string.IsNullOrEmpty(path))
                {
                    AppendLog("Clone cancelled.");
                    return;
                }

                if (!_document.TryCloneMagic(path, out string cloneError))
                {
                    AppendLog($"Clone FAILED: {cloneError}", MagicLogLevel.Error);
                    return;
                }

                AppendLog($"Clone created: {path} (new file; source unchanged).");
            }
            finally
            {
                _gate.Release();
            }
        }

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>
        /// Restores only the strictly validated .magic3d.bak files left by legacy builds. Modern
        /// previews are LocalAppData overlays and never need this command. Human confirmation is
        /// mandatory because a successful recovery replaces the matching selected-data file.
        /// </summary>
        [RelayCommand]
        private async Task RestoreLegacyPreviewAsync()
        {
            if (ConfirmLegacyPreviewRestore is null)
            {
                AppendLog(Strings.U_Md_RestoreLegacyPreviewUnavailable, MagicLogLevel.Warn);
                return;
            }

            if (!await ConfirmLegacyPreviewRestore())
            {
                AppendLog(Strings.U_Md_RestoreLegacyPreviewCancelled);
                return;
            }

            await _gate.WaitAsync();
            try
            {
                ClosePreview();

                (int restored, string detail) = RestoreLegacyPreviewAction();
                if (restored == 0)
                {
                    AppendLog(Strings.U_Md_RestoreLegacyPreviewNone);
                    return;
                }

                AppendLog(string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.U_Md_RestoreLegacyPreviewDone,
                    restored,
                    detail));
            }
            catch (Exception ex)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "Magic.Preview",
                    $"Legacy preview recovery failed: {ex.Message}",
                    ex);
                AppendLog(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.U_Md_RestoreLegacyPreviewFailed,
                        ex.Message),
                    MagicLogLevel.Error);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static (int Restored, string Detail) RestoreLegacyPreview()
        {
            int restored = MagicOverrideService.RestoreAllOverrides(out string detail);
            return (restored, detail);
        }
#endif

#if FFX_INCLUDE_DEVTOOLS
        /// <summary>Adicionar campo no slot selecionado → grow de record (WRITEBACK_SPEC §2.2). RISCO ALTO.</summary>
        [RelayCommand(CanExecute = nameof(CanAddField))]
        private async Task AddFieldAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (SelectedNode is not MagicSlotNode { Slot: not null } slotNode)
                {
                    AppendLog("Add field requires a slot selected in the tree (Programs → slot).", MagicLogLevel.Warn);
                    return;
                }
                MagicSlot slot = slotNode.Slot!;

                int width = GrowWidth;
                if (width is not (4 or 8 or 12 or 16))
                {
                    AppendLog($"Invalid GrowWidth: {width} — use 4/8/12/16 (WRITEBACK_SPEC §2.2).", MagicLogLevel.Warn);
                    return;
                }

                await Task.CompletedTask; // grow é síncrono; mantém assinatura async

                AppendLog(
                    $"Add field on slot #{slot.SlotIndex} (handler #{slot.HandlerTableIndex}" +
                    (slot.OpcodeName != null ? $" {slot.OpcodeName}" : "") +
                    $") — HIGH RISK (pointer-trust): alters the .data layout and pointer offsets; copy only; RT2 pending.",
                    MagicLogLevel.Warn);

                if (!_document.TryGrowRecord(slot, width, out string report, out string error))
                {
                    AppendLog($"Grow FAILED: {error}", MagicLogLevel.Error);
                    return;
                }

                AppendLog("Grow applied (in memory): " + report);
                _pendingGrowSlots.Add(slot);
                PendingGrowSummary = $"{_pendingGrowSlots.Count} slot(s) with grow pending persistence (W={width})";
                AppendLog("Persist via 'Save copy...' — never save over the installed game.");

                UpdateShaDisplay();
                RebuildTreePreservingSelection();
            }
            finally
            {
                _gate.Release();
            }
        }
#endif


        /// <summary>RT0 check: re-parse → serialize → SHA idêntico (abrir→salvar sem editar = byte-idêntico).</summary>
        [RelayCommand(CanExecute = nameof(HasDocument))]
        private void Rt0Check()
        {
            if (!HasDocument)
            {
                AppendLog("No file open for the RT0 check.", MagicLogLevel.Warn);
                return;
            }

            int dirty = CollectFieldNodes().Count(f => f.IsDirty);
            if (dirty > 0)
                AppendLog($"Warning: {dirty} field(s) with pending edits — RT0 validates the clean pipeline (parse→serialize).", MagicLogLevel.Warn);

            if (_document.Rt0Check(out string message))
                AppendLog(message);
            else
                AppendLog(message, MagicLogLevel.Error);
        }

        /// <summary>Reverter: restaura backup .bak (hash-gated) OU descarta edições/grow em memória.</summary>
        [RelayCommand(CanExecute = nameof(HasBackup))]
        private async Task RevertAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (!HasDocument)
                    return;

                string? lastSaved = _document.LastSavedPath;
                if (lastSaved != null && File.Exists(lastSaved + ".bak"))
                {
                    if (!_document.TryRestoreBackup(lastSaved, out string restoreError))
                    {
                        AppendLog($"Backup restore aborted: {restoreError}", MagicLogLevel.Error);
                        return;
                    }
                    AppendLog($"Backup restored (hash-gated): {lastSaved}");

                    if (!_document.TryLoad(lastSaved, out string loadError))
                    {
                        AppendLog($"Failed to reload the restored file: {loadError}", MagicLogLevel.Error);
                        return;
                    }
                    OnDocumentLoaded();
                    AppendLog("Document reloaded from the restored backup.");
                }
                else
                {
                    _document.DiscardWorkingChanges();
                    foreach (MagicFieldNode field in CollectFieldNodes())
                        field.ResetToOriginal();
#if FFX_INCLUDE_DEVTOOLS
                    _pendingGrowSlots.Clear();
                    PendingGrowSummary = string.Empty;
#endif
                    UpdateShaDisplay();
                    RebuildTreePreservingSelection();
                    AppendLog("Edits/grow discarded — document back to the original open state.");
                }
            }
            finally
            {
                _gate.Release();
            }
        }


#if FFX_INCLUDE_DEVTOOLS
        /// <summary>
        /// Simula a família U1 do documento aberto (T4 simulado — valida o que a mutação faz
        /// matematicamente, NÃO o visual completo). A mutação é aplicada ao slot selecionado
        /// na árvore quando ele é U1; caso contrário ao primeiro slot SclMove do documento.
        /// Simulação é READ-ONLY: nunca escreve bytes.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasDocument))]
        private void SimulateU1()
        {
            MagicDllFile? file = _document.ParsedFile;
            if (file is null)
            {
                AppendLog("No document open to simulate.", MagicLogLevel.Warn);
                return;
            }

            int frames = Math.Clamp(SimFrames, 1, 600);
            double factor = SimFactor;
            IReadOnlyList<MagicU1SlotInput> slots = MagicU1Simulator.CollectU1Slots(file);
            if (slots.Count == 0)
            {
                SimSummary = "No U1 slot found in this effect (SclMove/Scale/Move/Angle/AngMove families...).";
                SimTable = string.Empty;
                AppendLog("U1 simulation: no U1 slot in the document.", MagicLogLevel.Warn);
                return;
            }

            // Alvo da mutação: slot U1 selecionado, senão o primeiro SclMove, senão o primeiro U1.
            int? target = null;
            if (SelectedNode is MagicSlotNode { Slot: { } selectedSlot } &&
                selectedSlot.OpcodeName is { } selOp && MagicU1Simulator.IsU1(selOp))
            {
                target = selectedSlot.RecordOffset;
            }
            else
            {
                MagicU1SlotInput? sclMove = slots.FirstOrDefault(s => s.Opcode == "pppSclMove");
                target = (sclMove ?? slots[0]).RecordOffset;
            }

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames, target, factor);

            int shown = Math.Min(frames, 12);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("frame | scale X before | scale X after | angle Y before (wrap)");
            for (int i = 0; i < shown; i++)
            {
                int f = i * (frames / shown);
                MagicU1FrameState b = r.Original[f];
                MagicU1FrameState m = r.Mutated[f];
                sb.AppendLine($"{f,5} | {b.ScaleX,14:F2} | {m.ScaleX,14:F2} | {b.AngleYDeg,14:F2}");
            }

            string ratioText = double.IsNaN(r.FinalRatioX)
                ? "n/d (base scale 0)"
                : $"{r.FinalRatioX:F3}×";
            SimSummary =
                $"{r.SlotCount} U1 slots · mutation {factor:F2}× on record 0x{target:X} · " +
                $"final scale X ratio: {ratioText} · {frames} frames.";
            SimTable = sb.ToString();
            AppendLog($"U1 simulation: {SimSummary}");
            AppendLog("Warning: simulation ≠ game — covers the U1 family mathematically, not draw/particles/textures.", MagicLogLevel.Warn);
        }

        /// <summary>
        /// Exporta a evidência da última simulação U1 (JSON padrão work/layer_c/t1b/evidence/):
        /// dll, alvo, fator, frames, razão final e tabela antes×depois. READ-ONLY.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasSimSummary))]
        private void ExportSimulation()
        {
            MagicDllFile? file = _document.ParsedFile;
            if (file is null || string.IsNullOrEmpty(SimTable))
            {
                AppendLog("Nothing to export — run the simulation first.", MagicLogLevel.Warn);
                return;
            }

            string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "work", "layer_c", "t1b", "evidence");
            try
            {
                Directory.CreateDirectory(dir);
                string dll = _document.DllName ?? "magic_unknown.dll";
                string name = Path.GetFileNameWithoutExtension(dll) + "_sim_u1_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                string path = Path.Combine(dir, name);

                var ev = new System.Text.Json.Nodes.JsonObject
                {
                    ["dll"] = dll,
                    ["effect"] = EffectName,
                    ["sha_before"] = ShaBefore,
                    ["sim_factor"] = SimFactor,
                    ["sim_frames"] = SimFrames,
                    ["sim_summary"] = SimSummary,
                    ["sim_table"] = SimTable,
                    ["generated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["lane"] = "Jarvis-MAGIC",
                };
                File.WriteAllText(path, ev.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                AppendLog($"Evidence exported: {path}");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to export evidence: {ex.Message}", MagicLogLevel.Error);
            }
        }
#endif

        /// <summary>
        /// Painel "Editor de clone" (read-only): compara os working bytes com o original e
        /// lista os records mutados (opcode, record offset, SHA antes/depois) — o resumo de
        /// mutações ativas do clone. NUNCA escreve no jogo.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasDocument))]
        private void RefreshCloneDiff()
        {
            MagicDllFile? file = _document.ParsedFile;
            if (file is null)
            {
                CloneDiffSummary = string.Empty;
                CloneDiffDetail = string.Empty;
                return;
            }

            byte[]? orig = _document.SourceBytes;
            byte[]? work = _document.WorkingBytes;
            if (orig is null || work is null)
            {
                CloneDiffSummary = Strings.U_Md_NoReferenceBytes;
                CloneDiffDetail = string.Empty;
                return;
            }

            var sb = new System.Text.StringBuilder();
            int mutatedRecords = 0;
            int dataPtr = file.DataSectionRawPtr;
            foreach (MagicDllRoot root in file.Roots)
            foreach (MagicProgram program in root.Programs)
            foreach (MagicSlot slot in program.Slots)
            {
                int abs = dataPtr + slot.RecordOffset;
                if (abs < 0 || abs + slot.Record.Length > work.Length)
                    continue;
                var workSlice = work.AsSpan(abs, slot.Record.Length);
                var origSlice = orig.AsSpan(abs, slot.Record.Length);
                if (!workSlice.SequenceEqual(origSlice))
                {
                    mutatedRecords++;
                    string op = slot.OpcodeName ?? $"handler #{slot.HandlerTableIndex}";
                    sb.AppendLine($"  {op,-20} record @0x{slot.RecordOffset:X} — {ShortSha(slot.RecordSha256)} → {ShortSha(MagicSlot.ComputeSha256Hex(workSlice.ToArray()))}");
                }
            }

            CloneDiffSummary = mutatedRecords == 0
                ? Strings.F2_clone_identical_to_vanilla_0_records_mut_4f5868ae
                : $"{mutatedRecords} record(s) mutado(s) vs vanilla — o clone difere nestes pontos.";
            CloneDiffDetail = sb.ToString();
            AppendLog($"Clone diff: {CloneDiffSummary}");
        }

        // --- Helpers ---

        /// <summary>Pós-carga do documento: estado + árvore completa (Root → Descriptors → Programs → Slots → Fields).</summary>
        private void OnDocumentLoaded(bool closePreview = true)
        {
            if (closePreview) ClosePreview();
            FilePath = _document.SourcePath ?? string.Empty;
            UpdateShaDisplay();
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(HasBackup));
            OnPropertyChanged(nameof(IsDirty));
#if FFX_INCLUDE_DEVTOOLS
            OnPropertyChanged(nameof(CanAddField));
#endif
            SaveCopyCommand.NotifyCanExecuteChanged();
            CloneMagicCommand.NotifyCanExecuteChanged();
            PreviewMagicCommand.NotifyCanExecuteChanged();
            Rt0CheckCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
#if FFX_INCLUDE_DEVTOOLS
            AddFieldCommand.NotifyCanExecuteChanged();
            SimulateU1Command.NotifyCanExecuteChanged();
#endif
            RefreshCloneDiffCommand.NotifyCanExecuteChanged();
            EffectName = _document.DllName != null && MagicEffectNameCatalog.TryGet(_document.DllName, out string? name) ? name : null;
            RootNodes.Clear();
            // Zera o histórico de navegação (nós antigos não existem mais na nova árvore).
            _backStack.Clear();
            _forwardStack.Clear();
            _lastHistoryNode = null;
            OnPropertyChanged(nameof(CanNavigateBack));
            OnPropertyChanged(nameof(CanNavigateForward));
            MagicDllFile? file = _document.ParsedFile;
            if (file is null)
                return;

            foreach (MagicDllRoot root in file.Roots)
            {
                var rootChildren = new List<MagicNode>();

                // Descriptors (32B/entry — referências de recurso modelo/textura)
                if (root.Descriptors.Count > 0)
                {
                    var descriptors = new List<MagicNode>();
                    foreach (MagicDescriptor d in root.Descriptors)
                    {
                        // Nós de campo legíveis para cada word do descriptor (read-only).
                        var dfields = new List<MagicNode>
                        {
                            DescriptorField($"id a  (+0x00)", d.Ptr0),
                            DescriptorField($"id b  (+0x04)", d.Ptr4),
                            DescriptorField($"id c  (+0x08)", d.Ptr8),
                            DescriptorField($"tag a (+0x0C)", d.W12),
                            DescriptorField($"key   (+0x10)", d.W16),
                            DescriptorField($"blob1 (+0x14)", d.W20Rel),
                            DescriptorField($"blob2 (+0x18)", d.W24Rel),
                            DescriptorField($"blob3 (+0x1C)", d.W28Rel),
                        };
                        string summary = $"key 0x{d.W16:X4} · refs {d.Ptr0:X}+{d.Ptr4:X}+{d.Ptr8:X}";
                        descriptors.Add(new MagicDescriptorNode(d.Index, summary, dfields));
                    }
                    rootChildren.Add(new MagicGroupNode("Descriptors", descriptors));
                }

                // Programs → Slots → Fields
                var programs = new List<MagicNode>();
                for (int pi = 0; pi < root.Programs.Count; pi++)
                {
                    MagicProgram program = root.Programs[pi];
                    var slots = new List<MagicNode>();
                    for (int si = 0; si < program.Slots.Count; si++)
                    {
                        MagicSlot slot = program.Slots[si];
                        // Filtro por família/opcode OU nome de campo OU categoria semântica
                        // (case-insensitive; vazio = todos).
                        if (!string.IsNullOrWhiteSpace(SlotFilter))
                        {
                            string f = SlotFilter.Trim();
                            bool opcodeMatch = slot.OpcodeName?.Contains(f, StringComparison.OrdinalIgnoreCase) == true;
                            bool categoryMatch = TryGetFamilySchema(slot, out MagicFamilySchema? catSchema) &&
                                catSchema!.SemanticsCategory?.Contains(f, StringComparison.OrdinalIgnoreCase) == true;
                            if (!opcodeMatch && !categoryMatch)
                            {
                                bool fieldMatch = _document.ResolveAllFields(slot)
                                    .Any(fld => fld.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
                                if (!fieldMatch)
                                    continue;
                            }
                        }
                        var fields = new List<MagicNode>();
                        bool unknown = slot.OpcodeName is null;
                        TryGetFamilySchema(slot, out MagicFamilySchema? familySchema);
                        foreach (MagicField field in _document.ResolveAllFields(slot))
                        {
                            var fieldNode = new MagicFieldNode(
                                field.Name,
                                MapType(field.Type),
                                field.Offset,
                                field.Width,
                                field.Semantics,
                                $"{field.OpcodeName} — record @0x{field.RecordOffset:X}",
                                initialValue: FormatValue(field),
                                schemaAllowsWrite: MagicDllDocument_Wrapper.IsSchemaFieldWritable(
                                    familySchema,
                                    field.Offset,
                                    field.Width))
                            {
                                SourceField = field,
                            };
                            fields.Add(fieldNode);
                        }
                        // Inspector semântico (Eixo B1): categoria + função real da família.
                        string? semCategory = familySchema?.SemanticsCategory;
                        string? realFunc = familySchema?.RealFunc;
                        slots.Add(new MagicSlotNode(
                            si, (int)slot.HandlerTableIndex,
                            slot.OpcodeName ?? $"handler #{slot.HandlerTableIndex}",
                            unknown, fields, slot, semCategory, realFunc));
                    }
                    programs.Add(new MagicProgramNode(pi, $"key 0x{program.Key:X4}", slots,
                        program.Curve1Rel, program.Curve2Rel, program.SeqId,
                        slotSummary: BuildSlotSummary(program)));
                }
                if (programs.Count > 0)
                    rootChildren.Add(new MagicGroupNode("Programs", programs));

                rootChildren.Add(new MagicGroupNode("Handlers used",
                    root.HandlerIndicesUsed.Select(h => ResolveHandlerNode(root, h)).ToList()));

                // Eixo B3: visão agrupada por categoria semântica (mesmos handlers, agrupados).
                if (root.HandlerIndicesUsed.Count > 0)
                {
                    var byCategory = new System.Collections.Generic.Dictionary<string, List<MagicNode>>();
                    foreach (int h in root.HandlerIndicesUsed)
                    {
                        MagicNode node = ResolveHandlerNode(root, h);
                        string? op = root.Programs.SelectMany(p => p.Slots)
                            .FirstOrDefault(s => s.HandlerTableIndex == h && s.OpcodeName != null)?.OpcodeName;
                        string cat = "Uncategorized";
                        if (op != null && _document.FieldMap != null &&
                            _document.FieldMap.TryGet(op, out MagicFamilySchema? cSchema) &&
                            !string.IsNullOrEmpty(cSchema.SemanticsCategory))
                            cat = cSchema.SemanticsCategory!;
                        if (!byCategory.TryGetValue(cat, out List<MagicNode>? list))
                            byCategory[cat] = list = new List<MagicNode>();
                        list.Add(node);
                    }
                    var catGroups = byCategory
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kv => new MagicGroupNode($"{kv.Key} ({kv.Value.Count})", kv.Value))
                        .ToList();
                    rootChildren.Add(new MagicGroupNode("Handlers by category", catGroups));
                }

                RootNodes.Add(new MagicRootNode(file.DllName, rootChildren));
            }
        }

        /// <summary>Reconstrói a árvore preservando a seleção (pós-grow/pós-revert).</summary>
        private void RebuildTreePreservingSelection()
        {
            MagicNode? selected = SelectedNode;
            OnDocumentLoaded(closePreview: false);
            if (selected is not null && !RootNodes.Contains(selected))
                SelectedNode = RootNodes.SelectMany(n => n.Children).FirstOrDefault();
        }

        /// <summary>Coleta todos os nós de campo da árvore.</summary>
        private IEnumerable<MagicFieldNode> CollectFieldNodes()
        {
            foreach (MagicNode root in RootNodes)
            foreach (MagicNode group in root.Children)
            foreach (MagicNode program in group.Children)
            foreach (MagicNode slot in program.Children)
            foreach (MagicNode field in slot.Children)
                if (field is MagicFieldNode f)
                    yield return f;
        }

        /// <summary>Atualiza ShaBefore/ShaAfter do estado.</summary>
        private void UpdateShaDisplay()
        {
            ShaBefore = _document.ShaBefore;
            ShaAfter = _document.ShaAfter;
            OnPropertyChanged(nameof(HasBackup));
            RevertCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Aplica as edições pendentes (dirty) dos nós de campo nos working bytes.
        /// Retorna a quantidade aplicada; -1 em erro (já logado).
        /// </summary>
        private int ApplyPendingFieldEdits()
        {
            int applied = 0;
            foreach (MagicFieldNode field in CollectFieldNodes())
            {
                if (!field.IsDirty || field.SourceField is null)
                    continue;

                if (!TryBuildBytes(field, out byte[]? bytes))
                {
                    AppendLog($"Field '{field.Name}' has an invalid value — save aborted.", MagicLogLevel.Error);
                    return -1;
                }

                if (!_document.TryApplyFieldEdit(field.SourceField, bytes, out string editError))
                {
                    AppendLog($"Failed to apply edit on '{field.Name}': {editError}", MagicLogLevel.Error);
                    return -1;
                }
                applied++;
            }
            return applied;
        }
        /// <summary>Converte o valor editado do nó para bytes LE do tipo do campo.</summary>
        private static bool TryBuildBytes(MagicFieldNode field, out byte[]? bytes)
        {
            bytes = null;
            string text = field.Value;
            switch (field.Type)
            {
                case MagicFieldType.F32:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
                        return false;
                    bytes = BitConverter.GetBytes((float)f);
                    return true;
                case MagicFieldType.S32:
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int si))
                        return false;
                    bytes = BitConverter.GetBytes(si);
                    return true;
                case MagicFieldType.U32:
                    if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ui))
                        return false;
                    bytes = BitConverter.GetBytes(ui);
                    return true;
                case MagicFieldType.S16:
                    if (!short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short s16))
                        return false;
                    bytes = BitConverter.GetBytes(s16);
                    return true;
                case MagicFieldType.U16:
                    if (!TryParseUnsigned(text, out ulong u16) || u16 > ushort.MaxValue)
                        return false;
                    bytes = BitConverter.GetBytes((ushort)u16);
                    return true;
                case MagicFieldType.U8:
                    if (!TryParseUnsigned(text, out ulong u8) || u8 > byte.MaxValue)
                        return false;
                    bytes = new[] { (byte)u8 };
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseUnsigned(string text, out ulong value)
        {
            value = 0;
            string t = text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hex = t[2..];
                return hex.Length > 0 &&
                       ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            return ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Mapeia o tipo do parser para o tipo de exibição do módulo.</summary>
        private static MagicFieldType MapType(FfxLib.MagicDll.MagicFieldType type) => type switch
        {
            FfxLib.MagicDll.MagicFieldType.F32 => MagicFieldType.F32,
            FfxLib.MagicDll.MagicFieldType.S32 => MagicFieldType.S32,
            FfxLib.MagicDll.MagicFieldType.S16 => MagicFieldType.S16,
            FfxLib.MagicDll.MagicFieldType.U32 => MagicFieldType.U32,
            FfxLib.MagicDll.MagicFieldType.U16 => MagicFieldType.U16,
            FfxLib.MagicDll.MagicFieldType.U8 => MagicFieldType.U8,
            _ => MagicFieldType.Raw,
        };

        /// <summary>Formata o valor lido do campo para o texto inicial do nó.</summary>
        private static string FormatValue(MagicField f) => f.Type switch
        {
            FfxLib.MagicDll.MagicFieldType.F32 => f.ValueFloat is float v ? v.ToString("R", CultureInfo.InvariantCulture) : "?",
            FfxLib.MagicDll.MagicFieldType.S32 or FfxLib.MagicDll.MagicFieldType.S16 => f.ValueInt is int i ? i.ToString(CultureInfo.InvariantCulture) : "?",
            FfxLib.MagicDll.MagicFieldType.U32 or FfxLib.MagicDll.MagicFieldType.U16 or FfxLib.MagicDll.MagicFieldType.U8 => f.ValueUInt is uint u ? u.ToString(CultureInfo.InvariantCulture) : "?",
            _ => "?",
        };

        /// <summary>Adiciona uma entrada no log (bind do painel — Text + Level).</summary>
        private void AppendLog(string text, MagicLogLevel level = MagicLogLevel.Info)
        {
            Log.Add(new MagicLogEntry(text, level));
        }

        /// <summary>SHA curto (12 chars) para display.</summary>
        private static string ShortSha(string sha) =>
            string.IsNullOrEmpty(sha) ? "(vazio)" : (sha.Length > 12 ? sha[..12] : sha);

        /// <summary>
        /// Nó de um handler index usado: resolve o nome do opcode a partir do primeiro
        /// slot do documento que usa aquele índice (nome legível) e lista os slots que o usam.
        /// </summary>
        /// <summary>Resolve o schema da família de um slot via FieldMap (null quando slot raw/sem mapa).</summary>
        private bool TryGetFamilySchema(MagicSlot slot, out MagicFamilySchema? schema)
        {
            schema = null;
            if (slot.OpcodeName == null || _document.FieldMap == null)
                return false;
            return _document.FieldMap.TryGet(slot.OpcodeName, out schema);
        }

        private MagicNode ResolveHandlerNode(MagicDllRoot root, int handlerIndex)
        {
            // Deriva o nome a partir do primeiro slot com aquele handler_table_index.
            string? opcodeName = null;
            int count = 0;
            foreach (MagicProgram program in root.Programs)
            {
                foreach (MagicSlot slot in program.Slots)
                {
                    if (slot.HandlerTableIndex == handlerIndex)
                    {
                        count++;
                        opcodeName ??= slot.OpcodeName;
                    }
                }
            }

            // Categoria semântica do handler (Eixo B3) — resolve pelo opcode.
            string? category = null;
            if (opcodeName != null && _document.FieldMap != null &&
                _document.FieldMap.TryGet(opcodeName, out MagicFamilySchema? hSchema))
                category = hSchema.SemanticsCategory;

            string label = opcodeName != null
                ? (string.IsNullOrEmpty(category)
                    ? $"#{handlerIndex} · {opcodeName}"
                    : $"#{handlerIndex} · {opcodeName} · {category}")
                : $"handler #{handlerIndex} (unnamed)";
            // Children: summary of the slots that use this handler (non-empty → expandable).
            var children = new List<MagicNode>();
            int shown = 0;
            foreach (MagicProgram program in root.Programs)
            {
                foreach (MagicSlot slot in program.Slots)
                {
                    if (slot.HandlerTableIndex != handlerIndex || shown >= 8)
                        continue;
                    children.Add(new MagicGroupNode($"slot #{slot.SlotIndex} · {slot.OpcodeName ?? "unnamed"}",
                        Array.Empty<MagicNode>()));
                    shown++;
                }
            }
            if (count > 8)
                children.Add(new MagicGroupNode($"+ {count - 8} more slots", Array.Empty<MagicNode>()));

            return new MagicGroupNode(label, children);
        }

        /// <summary>Readable field node of a descriptor (read-only, hex).</summary>
        private static MagicNode DescriptorField(string name, uint value)
        {
            var fieldNode = new MagicFieldNode(
                name,
                MagicFieldType.U16,
                -1,
                4,
                $"0x{value:X8} ({value})",
                "descriptor (32B) — resource reference, read-only")
            {
                SourceField = null,
            };
            return fieldNode;
        }

        /// <summary>Opcodes distintos dos slots de um program (ex.: "pppSclMove, pppScale").</summary>
        private static string BuildSlotSummary(MagicProgram program)
        {
            var names = program.Slots
                .Select(s => s.OpcodeName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .Take(4)
                .ToList();
            return names.Count == 0 ? "" : string.Join(", ", names);
        }
    }
}

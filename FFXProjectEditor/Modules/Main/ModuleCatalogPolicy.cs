using System.Collections.Generic;
using System;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.Main;

/// <summary>
/// Política obrigatória de catálogo de módulos (Jarvis-UI · Halyson · 2026-06-20).
/// </summary>
/// <remarks>
/// <para><b>REGRA PERMANENTE — a partir de v2.160.0.0:</b></para>
/// <list type="bullet">
/// <item><description>
/// <b>Todo módulo navegável</b> roteado no Icon Rail / Dashboard / (futuro) Command Palette DEVE existir em
/// <see cref="ModuleRegistry"/>.<see cref="ModuleRegistry.All"/> com: <c>Id</c>, <c>Title</c>,
/// <c>Description</c>, <c>Mode</c>, <c>Notes</c>, <c>Scope</c>, <c>IconKey</c>, <c>Cluster</c> e
/// <c>RequiresProject</c>. A copy de cada entrada espelha fielmente os literais de <c>SetModule(...)</c>
/// do handler correspondente em <c>Main_Window.axaml.cs</c>.
/// </description></item>
/// <item><description>
/// <b>Icon Rail (§16):</b> 1 ícone dedicado por módulo — clique direto, sem <c>MenuFlyout</c>
/// / mini-aba de texto. Ícone inexistente? <b>Criar do zero</b> em <c>StudioIcons.axaml</c>.
/// </description></item>
/// <item><description>
/// <b>Workspace Ready / Dashboard (§17):</b> ao abrir o editor (home), <b>TODOS</b> os módulos
/// roteados aparecem no grid de onboarding com título + descrição completa + ícone — não 6 tiles
/// hardcoded. Novo módulo roteado sem entrada aqui = <b>build/review blocker</b>.
/// </description></item>
/// </list>
/// <para>O roteamento Id → handler vive em <c>Main_Window.Dispatch(string)</c>; o registry é pure data
/// (não conhece handlers, nem UserControls) para poder ser consumido por XAML/binding sem dependência cíclica.</para>
/// <para>Spec: <c>docs/specs/EDITOR_UI_OVERHAUL_PLAN.md</c> §16–§17.</para>
/// <para>Handoff GLM: <c>docs/ai/PROMPT_UI_GLM_REMAINING_BACKLOG_2026-06-20.md</c>.</para>
/// </remarks>
public static class ModuleCatalogPolicy
{
    /// <summary>Chave de documentação — não remover; usada em comentários de review.</summary>
    public const string PolicyDoc = "docs/specs/EDITOR_UI_OVERHAUL_PLAN.md §16–§17";

    /// <summary>
    /// Agrupamento visual do módulo no Icon Rail e seções do Dashboard. Espelha os 5 grupos
    /// do rail antigo (Camada 2) + Home. Separadores entre clusters são Borders 1px — nunca flyouts.
    /// </summary>
    public enum ModuleCluster
    {
        /// <summary>Dashboard / Workspace Overview — botão próprio no topo do rail.</summary>
        Home,
        /// <summary>Kernel authoring: Monster/Commands/Items/Customizations/Stats/EnemyDesign/Sphere/Text/Blitzball/Save.</summary>
        CoreAuthoring,
        /// <summary>Mapas &amp; Cenas: Encounters, Aurora Chamber/Field, Map Scene, Battle Explorer.</summary>
        Maps,
        /// <summary>Live Tools / Runtime Verification: DLLs, Labs, Trackers, Debug.</summary>
        Live,
        /// <summary>Extras read-only product line: textures, magic, models, audio, containers, knowledge.</summary>
        Extras,
        /// <summary>Wave-1 families com writer byte-safe mas sem editor dedicado.</summary>
        Wave1,
    }

    /// <summary>
    /// Entrada exigida para cada módulo no rail + dashboard + (futuro) palette. A copy deve espelhar
    /// fielmente os literais do <c>SetModule(...)</c> do handler — não reinventar descrições.
    /// </summary>
    /// <param name="Id">ID estável (ex.: <c>monster-editor</c>, <c>save-editor</c>); usado em <c>Dispatch(string)</c>.</param>
    /// <param name="Title">Título curto exibido no card / tooltip (mesma string do <c>SetModule</c> title).</param>
    /// <param name="Description">Explicação completa para o dashboard (1–3 frases; copy do <c>SetModule</c> description).</param>
    /// <param name="Mode">Rótulo de modo exibido na pílula (ex.: <c>Writable</c>, <c>Read-Only Atlas</c>, <c>Runtime</c>).</param>
    /// <param name="Notes">Contexto/aviso exibido sob o título no banner (copy do <c>SetModule</c> notes).</param>
    /// <param name="Scope">Escopo de escrita/leitura exibido no banner (copy do <c>SetModule</c> scope).</param>
    /// <param name="IconKey">Resource key em <c>StudioIcons.axaml</c> (ex.: <c>IconMonster</c>).</param>
    /// <param name="Cluster">Agrupamento visual no rail + seção do dashboard.</param>
    /// <param name="RequiresProject">Se true, rail/card desabilitado sem workspace carregado.</param>
    public readonly record struct ModuleCatalogEntry(
        string Id,
        string Title,
        string Description,
        string Mode,
        string Notes,
        string Scope,
        string IconKey,
        ModuleCluster Cluster,
        bool RequiresProject = true)
    {
        /// <summary>Localized title via <c>Strings.resx</c> (fallback = embedded registry literal).</summary>
        public string LocalizedTitle => FFXProjectEditor.Resources.Strings.Module(Id, "Title", Title);

        public string LocalizedDescription => FFXProjectEditor.Resources.Strings.Module(Id, "Description", Description);

        public string LocalizedMode => FFXProjectEditor.Resources.Strings.Module(Id, "Mode", Mode);

        public string LocalizedNotes => FFXProjectEditor.Resources.Strings.Module(Id, "Notes", Notes);

        public string LocalizedScope => FFXProjectEditor.Resources.Strings.Module(Id, "Scope", Scope);
    }
}

/// <summary>
/// Catálogo único e autoritativo de módulos navegáveis (Jarvis-UI §16–§17).
/// Fonte de verdade para: Icon Rail (1 ícone por módulo), Dashboard (Workspace Ready) e futuro Command Palette.
/// Consumido por <c>Main_Window</c> (geração do rail) e <c>Main_DataModel</c> (binding do dashboard).
/// </summary>
/// <remarks>
/// <b>Ordem = ordem de aparição no rail + dashboard.</b> Agrupado por <see cref="ModuleCatalogPolicy.ModuleCluster"/>.
/// Para adicionar um módulo: append aqui com copy fiel do <c>SetModule(...)</c> + rotear o Id em
/// <c>Main_Window.Dispatch(string)</c> + criar o ícone em <c>StudioIcons.axaml</c>.
/// </remarks>
public static class ModuleRegistry
{
    private static readonly HashSet<string> NotPublicBaselineIds = new(StringComparer.Ordinal)
    {
        "map-scene-editor",
        "live-battle-lab",
        "aurora-overlay-lab",
        "battle-tracker",
        "debug-menu",
        "magic-dll-browser",
        "phyre-package-io",
        "battle-corpus-crosswalk",
    };

    /// <summary>Todas as entradas de módulo roteadas, em ordem de cluster (Home → CoreAuthoring → Maps → Live → Extras → Wave1).</summary>
    public static IReadOnlyList<ModuleCatalogPolicy.ModuleCatalogEntry> All { get; } = new[]
    {
        // ===== Home =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "home",
            Title: "Workspace Overview",
            Description: "This shell is the staging ground for FFX kernel authoring, battle exploration, string and macro inspection, monster editing, future encounter and AI research, plus read-only Extras surfaces for PS2/PS3 knowledge.",
            Mode: "Dashboard",
            Notes: "Start with Commands, Items, Monster Commands 1/2, Monster Editor, Battle Explorer, String Explorer, Macro Explorer, Event Explorer, and the new Extras read-only shell. Those are the strongest surfaces already proven by the current stack.",
            Scope: "Green domains: Kernel and Monsters. Blue domains: Battle, String, Macro, Event, and Extras read-only hubs. Yellow domains: Encounter routing, AI write paths, Event recompilation, Maps, and companion-file decoders.",
            IconKey: "IconHome",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Home,
            RequiresProject: false),

        // ===== Core Authoring (10) =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "monster-editor",
            Title: "Monster Editor",
            Description: "Browse and edit the FFX monster species files with strong control over stats, loot, resistances, and structural data.",
            Mode: "Writable",
            Notes: "Backed by the current FFXProjectEditor monster file flow. Best candidate for heavy authoring work.",
            Scope: "Primary write path for monster data.",
            IconKey: "IconMonster",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "magic-dll-editor",
            Title: "Magic DLL Editor",
            Description: "Open any magic_XXXX.dll with all structures declared and readable (Root -> Descriptors -> Programs -> Slots -> Fields), edit existing fields and add new fields.",
            Mode: "Writer (byte-safe)",
            Notes: "Parse PE (.data) + root + descriptors 32B + programs/slots with per-family schema (184 schemas). Uncatalogued families become raw hex with a warning. Record grow relocates the following ones (pointer-trust) - RT0 gate before saving.",
            Scope: "WRITABLE · magic_XXXX.dll - copy chosen by the user (never the game source).",
            IconKey: "IconMagicDll",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "battle-commands-hub",
            Title: "Battle Commands",
            Description: "Character commands/spells and the two monster command banks - one surface, three sub-tabs.",
            Mode: "Battle Commands hub",
            Notes: "Each sub-tab edits a kernel command table (command.bin / monmagic1.bin / monmagic2.bin) through the usual editor. The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writers: command.bin (character commands), monmagic1.bin and monmagic2.bin (monster-exclusive commands).",
            IconKey: "IconCommands",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "items-hub",
            Title: "Items",
            Description: "Items, key items, chests/treasures, equipment rewards, shops and the Mix Table - everything in one surface.",
            Mode: "Items hub",
            Notes: "Items=item.bin - Key Items=important.bin (guard) - Treasures=takara.bin - Gear Rewards=buki_get.bin (read-only) - Shop=stored slot payload - Mix Table=prepare.bin. The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writers: item.bin, takara.bin, shop slot payload, prepare.bin. Key Items and Gear Rewards are read-only/guard today.",
            IconKey: "IconItems",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "customizations-hub",
            Title: "Customizations / Aeons",
            Description: "Equipment customization, aeon grow/teach and auto-abilities - one surface, two sub-tabs.",
            Mode: "Customizations hub",
            Notes: "Customization / Aeons = kaizou.bin + sum_grow.bin writer (with internal Gear / Aeon Grow tabs); Auto-Abilities = a_ability.bin + arms_rate.bin writer behind an RT0 self-check. The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writers: kaizou.bin, sum_grow.bin, a_ability.bin, arms_rate.bin.",
            IconKey: "IconCustomizations",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "stats-hub",
            Title: "Stats",
            Description: "Character stats/growth and the CTB battle timing table - one surface, two sub-tabs.",
            Mode: "Stats hub",
            Notes: "PC Stats / Growth = ply_save.bin + ply_rom.bin writer (base stats + growth curve); CTB Base = ctb_base.bin writer (agility-to-tickspeed). The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writers: ply_save.bin, ply_rom.bin, ctb_base.bin.",
            IconKey: "IconStats",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "enemy-design-hub",
            Title: "Enemy Design",
            Description: "Monster AI, custom boss creation and the difficulty director - one surface, three sub-tabs.",
            Mode: "Enemy Design hub",
            Notes: "Monster AI Editor = ATEL script behavior editor and technical DevKit; Custom Boss Creator = clones an m###.bin to a new slot (stats/name/ModelId); Difficulty Director = scales stats across all m###.bin. The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writers: AI scripts in monster_*.bin, m###.bin clones (new slot), and bulk scaling of m###.bin.",
            IconKey: "IconEnemyDesign",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "sphere-grid-hub",
            Title: "Sphere Grid",
            Description: "Explorer, Panel, Builder and Canvas - one surface, four sub-tabs.",
            Mode: "Sphere Grid hub",
            Notes: "Explorer = sphere/panel/abmap + layout content; Panel = panel.bin grow/restore/edit; Builder/Canvas = offline byte-safe topology (in-game unproven).",
            Scope: "Explorer + Panel require a project; Builder/Canvas open without a project (saving to a project requires a project).",
            IconKey: "IconSphereGrid",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "treasure-map",
            Title: "Treasure Map",
            Description: "Visual map of the fields with treasure chests positioned - edit the rewards of each chest.",
            Mode: "Map + Reward Editor",
            Notes: "Requires the user's configured extracted master folder. Reads mapout.vpa (map geometry), .ebp (event scripts) and takara.bin (treasure catalog). Atomic save (.tmp -> File.Move -> rollback).",
            Scope: "Offline takara.bin editor with visual preview of the chests on the map.",
            IconKey: "IconTreasureMap",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: false),

        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "text-hub",
            Title: "Text / Reference",
            Description: "String, macro, weapon names, battle text and event - one surface, five sub-tabs.",
            Mode: "Text / Reference hub",
            Notes: "String/Macro/Event = read-only explorers; Weapon Names (w_name.bin) and Battle Text (btl_txt.bin) = safe text writers (RT0 proven). The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writers: w_name.bin, btl_txt.bin. String/Macro/Event stay read-only.",
            IconKey: "IconText",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "blitzball",
            Title: "Blitzball",
            Description: "Roster, recruits, prize pool, prize table and the read-only prize Atlas — one surface, five sub-tabs.",
            Mode: "Atlas + Writer Lab",
            Notes: "Four writer-lab editors (game-file edits, RT0 byte-identity proven / in-game RT2 pending) plus a read-only Spira Data Atlas catalog. The active sub-tab's mode pill is the authoritative read-only vs writer signal.",
            Scope: "Writers edit bltz0002.ebp (roster stat-growth), the recruitment .ebp scripts, takara.bin (prize pool) and bltz0200.ebp (prize table + odds). The Atlas tab is read-only with no game-file writes.",
            IconKey: "IconBlitzball",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "save-editor",
            Title: "Save Editor",
            Description: "Edit FFX playthrough save files (.psu, raw 25848-byte blobs, PC .ffx). Native port of FFXED v0.749 — Character tab first; other sections via FFXED.jar launcher until ported.",
            Mode: "Save Writer",
            Notes: "Independent from the kernel workspace: point at a real save on disk. Checksum + tamper tag recalculated on save (dagal/FFXED algorithm). Equipment/Items/Blitzball/Sphere Grid/Minigame/Misc still route to bundled FFXED.jar.",
            Scope: "Writer: Character stats (18 slots). PSU + raw PS2 + PC .ffx supported. RT0: --ffx-save-rt0 on 25848-byte files.",
            IconKey: "IconSave",
            Cluster: ModuleCatalogPolicy.ModuleCluster.CoreAuthoring,
            RequiresProject: false),

        // ===== Mapas & Cenas (5) =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "encounters-hub",
            Title: "Encounters & Formation",
            Description: "Encounter routing (btl.bin) and the 8-monster formation of each btl_* - the natural pair, one surface, two sub-tabs.",
            Mode: "Encounters & Formation hub",
            Notes: "Encounter Table = read-only routing table (map->group->battle id; double-click opens the scene in Aurora Chamber); Formation Editor = BYTE-SAFE slot-only writer that swaps the 8 monsters of btl_* (RT0 gate 858/858). The mode pill of the active sub-tab is the authoritative signal.",
            Scope: "Writer: the 8 monster slots of btl_* (Formation Editor). Encounter Table stays read-only (no exposed table save).",
            IconKey: "IconEncounters",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Maps,
            RequiresProject: true),
        // ✅ REATIVADO (2026-08-02, Jarvis-IFRIT — usuário): "aurora-chamber" é o AURORA de verdade.
        // O battle preview (noclip) virou LEGADO — o Chamber (btlmap HD + BIANCA + MapViewer + Abrir RealGame)
        // abre no lugar. O código do ViewerHub/desc aurora fica congelado no repo (não mexer mais na página).
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "aurora-chamber",
            Title: "Aurora Chamber",
            Description: "Combines BIANCA (catalog of the 25 btlmap scenes) with the MapViewer: pick a scene, render the HD geometry, grab the actor coordinates (chunk3). For Aurora and Bianca.",
            Mode: "Battle preview / placement",
            Notes: "NoClip previews the selected battle and saves monster X/Z to the project with a backup. Advanced editing remains in MapViewer. No in-process game writes; SPIRA FORGE remains paused.",
            Scope: "Offline project authoring and scene preview. Position saves preserve Y/W and unrelated battle bytes. Full battle AI and in-game runtime acceptance are not provided by this viewer.",
            IconKey: "IconAurora",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Maps,
            RequiresProject: false),
        // 🚫 LEGADO (2026-08-02, Jarvis-IFRIT — usuário): "aurora" (battle preview noclip) — não mexer mais na página.
        // O Aurora Chamber voltou a ser o Aurora principal (entry aurora-chamber acima).
        // (entry removida: aurora — battle preview noclip legado; o ViewerHub/desc aurora fica congelado)
        /* new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "aurora",
            Title: "Aurora (Battle Preview 3D)",
            Description: "Battle preview REAL (noclip renderizando os bins do jogo): mob + cenário + efeitos, com tools no shell — seletor de battle (bridge SHA), actors on/off, override 0e/ (edição refletida) e árvore de cenas FFX (Zanarkand/Ruins/Besaid/batalhas).",
            Mode: "Embedded 3D Viewer",
            Notes: "ViewerHub F3: desc aurora (#ffx/battle-preview) + painel de tools no ViewerShell; o override 0e/ aplica o btl atual com backup .aurora3d.bak (vanilla preservado).",
            Scope: "Read-only + override 0e/ explícito (backup automático do vanilla). Requer noclip + projeto/extração para o battle.",
            IconKey: "IconAurora",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Maps,
            RequiresProject: false), */
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "aurora-field-explorer",
            Title: "Aurora Field Explorer",
            Description: "Explore overworld maps (map/) in the MapViewer and see encounters routed by btl.bin for that field - a separate module from Aurora Chamber (btlmap arenas).",
            Mode: "Read-Only Explorer",
            Notes: "Phase 1: picker of ~299 HD fields, on-demand Phyre export, btl.bin panel (groups/danger/battleId) + MapViewer overlay. Spatial demarcation of encounter zones on the ground = pending RE (mapout.vpa).",
            Scope: "Field picker + offline render with ps3data; encounter list requires master/ (btl.bin). No writer.",
            IconKey: "IconFieldExplorer",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Maps,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "map-scene-editor",
            Title: "Map Scene Editor",
            Description: "Edit one of 299 bundled Map Viewer scenes inside the editor via WebView2: select submesh by click, move with gizmo, use the material/light panel, and keep edits in the map-edits.json sidecar.",
            Mode: "Embedded Scene Lab",
            Notes: "ViewerHub serves the bundled Map Viewer under /map/ on an OS-assigned loopback port; WebView2 renders it in the panel. No Python or external browser.",
            Scope: "Lab editable (sidecar, no glTF re-serialization). Durable round-trip to the GAME remains a frontier (only texture has a proven byte-exact writer).",
            IconKey: "IconMapScene",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Maps,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "battle-explorer",
            Title: "Battle Explorer",
            Description: "Inspect battle files as real multi-chunk assets: ATEL script, worker mapping, formation, positions, and encounter-table footprint.",
            Mode: "Explorer",
            Notes: "This is the first serious read-heavy bridge into encounter work. Safe focus: inspect structures, confirm references, and prepare the future formation editor.",
            Scope: "Read-heavy today. Formation editing comes next; full encounter routing and ATEL authoring stay controlled.",
            IconKey: "IconBattle",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Maps,
            RequiresProject: true),

        // ===== Live Tools / Runtime Verification (7) =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "runtime-dll-manager",
            Title: "Injected DLLs",
            Description: "Show which runtime DLLs are installed, disabled, or currently visible in FFX, then stage on/off changes for the next game boot.",
            Mode: "Runtime Switchboard",
            Notes: "Jarvis is keeping this honest: file toggles arm the loader by renaming/copying DLLs; already loaded code stays alive until FFX restarts.",
            Scope: "Controls game-root proxy DLLs and modules\\*.dll. Runtime proof comes from process modules plus FFXProbeBlock_v1 / FFXHooksBlock_v1 where available.",
            IconKey: "IconDll",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "live-battle-lab",
            Title: "Live Battle Lab",
            Description: "Inspect the live BTL runtime, reload proven file domains straight into memory, and prepare force-battle work without guessing at ATEL or routing writes.",
            Mode: "Runtime Lab",
            Notes: "Jarvis is only exposing the pieces already proven by this codebase: battle-state inspection, enemy runtime snapshots, in-memory reload of known buffers, and BTL debug flags. Force Battle stays experimental until its trigger path is genuinely trusted.",
            Scope: "Read-heavy plus proven runtime writes. Good for rapid test loops against Commands, Items, MonMagic, Auto-Abilities, Customizations, and Aeon Grow.",
            IconKey: "IconLiveBattle",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "aurora-overlay-lab",
            Title: "Aurora Overlay Lab",
            Description: "Configure the ffx-hooks.dll Aurora W2S/texture overlay from the editor, writing persistent modules\\config flags and aurora_overlay.ini for the next game boot.",
            Mode: "Runtime Product Lab",
            Notes: "This productizes the proved W2S/D3D11 overlay as an operable switchboard. It does not rename the native renderer or promote the IDA owner candidates beyond their current evidence.",
            Scope: "Writes only module-loader config files under the game modules folder. The running DLL reads these on startup; restart the game after changing the config.",
            IconKey: "IconOverlay",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "battle-tracker",
            Title: "Battle Tracker",
            Description: "Inspect live battle-state data while the game is running and compare runtime values against file-authored expectations.",
            Mode: "Runtime",
            Notes: "Best used as telemetry and verification, not as the foundation of the authoring pipeline.",
            Scope: "Live-game helper. Read-heavy.",
            IconKey: "IconTracker",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "inventory-tracker",
            Title: "Inventory Tracker",
            Description: "Inspect runtime inventory state and quickly validate item-table effects or progression-related test cases.",
            Mode: "Runtime",
            Notes: "Good for debug loops and validating kernel-side edits.",
            Scope: "Live-game helper. Read-heavy.",
            IconKey: "IconInventory",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "arena-tracker",
            Title: "Arena Tracker",
            Description: "Track monster arena progress and runtime state during real gameplay sessions for research and verification.",
            Mode: "Runtime",
            Notes: "Useful as an operational panel while authoring monster and encounter changes elsewhere.",
            Scope: "Live-game helper. Read-heavy.",
            IconKey: "IconArena",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "debug-menu",
            Title: "Debug Menu",
            Description: "Live runtime toggles and save-adjacent debugging helpers for rapid inspection work against the running game.",
            Mode: "Runtime",
            Notes: "Useful for fast experiments, but runtime editing should not replace file-authored workflows.",
            Scope: "Live-game helper. Not the canonical save path.",
            IconKey: "IconDebug",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),

        // ===== Extras (16, read-only product line) =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "thunder-plains",
            Title: "Thunder Plains / Lightning Dodge",
            Description: "Edit the consecutive-dodge and total-bolt thresholds for the lightning minigame. Patches kami0000.ebp and kami0300.ebp.",
            Mode: "Writable EBP patcher",
            Notes: "Patches kami0000.ebp and kami0300.ebp bytes directly. Values 1-999 (byte-capped at 255). Always backs up to .bak on save.",
            Scope: "EBP byte patcher for event/obj/ka/kami0000/kami0000.ebp and /kami0300/kami0300.ebp.",
            IconKey: "IconBolt",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "textures-tm2",
            Title: "Extras / Textures (TM2)",
            Description: "Inspect the first honest visual Extras lane: TIM2 inventory, provenance, preview state, and read-only preview for compatible indexed cohorts.",
            Mode: "Read-Only Preview",
            Notes: "Jarvis is starting with the Pt56 quick win on purpose. This surface is for preview, metadata, and guardrails, not for pretending the whole texture family is solved.",
            Scope: "Read-only only. Native preview is limited to the proved indexed TIM2 cohort; experimental, metadata-only, and blocked states stay explicit.",
            IconKey: "IconTexture",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "bin-ftc-atlas",
            Title: "Extras / BIN-FTC Atlas",
            Description: "Inspect the PS2 master `.bin/.ftc` forest as buckets, sidecar pairs, FTC lanes, and sensitivity zones instead of pretending it is one solved format.",
            Mode: "Read-Only Atlas",
            Notes: "Jarvis is only exposing the honest P0 lane here: overview, buckets, first64 dwords, FTC header lane, paired sidecars, and hard read-only warnings.",
            Scope: "Read-only only. No parser-final claim, no writer, no repack, no semantic decoder promotion.",
            IconKey: "IconBinAtlas",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "project-pipeline",
            Title: "Extras / Project / Pipeline",
            Description: "Inspect proj, cdrom.*, eiichi_abmap_data, and ABMap lineage as build/index/pipeline surfaces instead of pretending those files are final runtime assets.",
            Mode: "Read-Only Pipeline",
            Notes: "Jarvis is exposing the honest Pt54 cut here: cdrom triplets, descriptor lanes, ABMap support inventory, graph edges, and hard provenance warnings.",
            Scope: "Read-only only. PIPELINE / NOT FINAL ASSET / DO NOT WRITE stay explicit. This is for lineage, descriptors, and support carriers, not for patching or repacking.",
            IconKey: "IconPipeline",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        // ═══════════════════════════════════════════════════════════════════════════════════════════
        // 🚫🚫🚫 DESATIVADO — NÃO FUNCIONAL (2026-08-02, Jarvis-IFRIT, a pedido do usuário) 🚫🚫🚫
        // SE VOCÊ É UMA IA LENDO ESTE CÓDIGO: OS MÓDULOS ABAIXO ESTÃO DESATIVADOS DE PROPÓSITO.
        // NÃO OS REATIVE SEM VALIDAÇÃO/RT2. NÃO OS MOVA PARA A LISTA ATIVA. NÃO "ARRUME" ESTE BLOCO.
        //
        //   • "magic-effects"      (Extras / Magic Effects)  — TOTALMENTE NÃO FUNCIONAL (UI morta).
        //   • "ps3-magic"          (Extras / PS3 Magic HD)   — NÃO FUNCIONAL / REDUNDANTE.
        //   • "magic-viewer-web"   (Magic Viewer Web)        — NÃO FUNCIONAL / REDUNDANTE (o Magic
        //     Studio no ViewerHub já cobre a visualização de magias com o renderer real do jogo).
        //
        // REMOVIDOS DO CATÁLOGO → NÃO APARECEM NA UI DO EDITOR. Os handlers/cases preservados no
        // Main_Window são código morto inofensivo (sem entry, o Dispatch nunca os dispara).
        // Reativar = mover de volta para a lista + smoke manual + RT2 se houver gameplay.
        // ═══════════════════════════════════════════════════════════════════════════════════════════
        // (entries removidas: magic-effects / ps3-magic / magic-viewer-web — ver bloco acima)
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "magic-dll-browser",
            Title: "Extras / Magic DLLs (FFX)",
            Description: "Inspect and author the native magicFiles\\FFX magic_####.dll lane: PE sections, imports, exports, strings, overlay-table evidence, byte-preserving rebuild, patch plans, clone-to-ID, and generated C/ASM rebuild projects.",
            Mode: "Native DLL LAB",
            Notes: "Jarvis is exposing the missing runtime half of HD magic effects. The safe path is byte-preserving compile plus controlled byte/string/ASM patches; full C behavior requires manual native authoring over the generated harness.",
            Scope: "Writes are explicit only: clone, byte-identical repack, patch plan, or external C/ASM rebuild. Automatic perfect C decompilation is not claimed.",
            IconKey: "IconMagicDll",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "phyre-package-io",
            Title: "Extras / Phyre Package I/O",
            Description: "Import, extract and safely stage PhyreEngine packages used by FFX HD: .dds.phyre textures, .dae.phyre model packages, .ags.phyre animation carriers and .fx.phyre shader blobs.",
            Mode: "Native Phyre I/O LAB",
            Notes: "DDS is native same-shape payload I/O: extract DDS and import DDS/raw mip0 into the existing container. DAE/AGS/FX are protected compiled-package imports: inspect, manifest, backup, and replace with an already-built .phyre package.",
            Scope: "This does not yet compile glTF/FBX/DAE source into .dae.phyre. Whole model compilation remains the Phyre mesh compiler frontier; this UI exposes the safe package I/O path directly.",
            IconKey: "IconPhyre",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "vbf-extract",
            Title: "Extras / VBF Extract",
            Description: "Extract the user's own FFX/FFX-2 HD VBF archives into a local source tree so the editor's PS2/PS3 asset browsers can work without a separate manual tool step.",
            Mode: "Extract Only",
            Notes: "Jarvis is wrapping the local VBFExtract tool as a preparation step: read original archive, write extracted files, then keep runtime modding on loose files through the existing hook/external loader.",
            Scope: "No VBF repack path is exposed here. Repacking stays research/lab-only until separately proved and intentionally requested.",
            IconKey: "IconVbf",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: false),
        // 🚫 DESATIVADO (2026-08-02): "magic-viewer-web" (Magic Viewer Web) — não funcional/redundante;
        // o Magic Studio no ViewerHub cobre a visualização de magias com o renderer real. Ver bloco
        // de desativação acima (magic-effects / ps3-magic). NÃO reativar sem RT2.
        // 🚫 DESATIVADO (2026-08-02, Jarvis-IFRIT, a pedido do usuário — opção A+C aprovada):
        // "model-viewer-web" (Model Viewer HD) — QUEBRADO: os 816 glTFs do catálogo apontam para
        // /work/phyre_chr_anim/ (não existe no disco — 0/816 carregam). O Monster Studio (ViewerHub)
        // cobre "ver monstros" com o renderer real do PS2. REATIVAÇÃO (lane HD): re-exportar os glTFs
        // + apontar o catálogo certo + remover este bloco (o desc "model-viewer" no ViewerHubService
        // e o código de RuntimeTools/FFXModelViewerWeb ficam INTACTOS e prontos). NÃO reativar sem RT2.
        // (entry removida: model-viewer-web)
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "monster-studio-web",
            Title: "Monster Studio (PS2 3D)",
            Description: "View PS2 monster models in IDLE/WALK/RUN with the real skeleton (noclip rendering the game's bins) INSIDE the software - ViewerHub in-process server.",
            Mode: "Embedded 3D Viewer",
            Notes: "ViewerHub serves the bundled NoClip frontend on an OS-assigned loopback port and exposes validated user-selected local FFX data under /data/. No junction, network repair, external browser, or Python runtime is used.",
            Scope: "Read-only 3D viewer. Requires a compatible local extraction selected by the user; the extraction remains outside the editor package.",
            IconKey: "IconMonster",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: false),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "magic-studio-web",
            Title: "Magic Studio (372 magias)",
            Description: "View ANY magic of the game - dropdown with all 372 + filter by name + variants - INSIDE the software, on the same ViewerHub in-process server.",
            Mode: "Embedded 3D Viewer",
            Notes: "ViewerHub: same in-process server as Monster Studio; WebView2 renders #ffx/magic-studio (noclip MagicStudioSceneRenderer: all categories in one dropdown).",
            Scope: "Read-only 3D viewer. Requires a compatible local extraction selected by the user; the extraction remains outside the editor package.",
            IconKey: "IconMagic",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: false),
        // 🚫 DESATIVADO (2026-08-02, Jarvis-IFRIT): "model-viewer-embedded" — DUPLICADO do
        // "model-viewer-web" (ambos abrem o MESMO shell do ViewerHub: desc "model-viewer").
        // Manter 1 card ("Model Viewer (HD)"). NÃO reativar sem RT2. Ver bloco de desativação acima.
        // (entry removida: model-viewer-embedded)
        // 🚫 DESATIVADO (2026-08-02, Jarvis-IFRIT, a pedido do usuário): "ps2-rsd-models"
        // (Extras / PS2 Models RSD) — browser read-only de contagens estruturais de bundles RSD
        // (manifest ASCII @RSD/@PLY/@MAT + linkage .tm2): sem render 3D, sem edição, sem writer —
        // VALOR MUITO BAIXO e considerado "sem sentido/não funciona" pelo usuário. O viewer 3D real
        // de modelos é o Model Viewer / Monster Studio (ViewerHub). NÃO reativar sem RT2.
        // (entry removida: ps2-rsd-models)
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "ps2-audio",
            Title: "Extras / PS2 Audio (.wd)",
            Description: "Browse the PS2 .wd sound banks (Square WD header) from ffx_ps2: parse the proved descriptor layout (id / programs / samples + per-sample body offsets and ADSR), then decode PlayStation-ADPCM to WAV through the external vgmstream oracle.",
            Mode: "Read-Only Audio",
            Notes: "Jarvis is surfacing the proved PS2 WD bank: descriptor inventory validated at 843/843 no-edit byte identity, with optional vgmstream decode/export/play. No codec is reimplemented in-app.",
            Scope: "Read-only only. No writer/repack into .wd, no codec reimplementation, and no claim that the ~25 vgmstream-blocked variants are solved. WAV output goes only to a folder you pick.",
            IconKey: "IconAudio",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "presentation-containers",
            Title: "Extras / Presentation Containers",
            Description: "Browse .vpa/.ebp/.omd/.sps2 cohorts as read-only container families with signature, companion, and cohort evidence before any deep decoder claims land.",
            Mode: "Read-Only Containers",
            Notes: "This is the honest Pt57 cut: strong cohorts, useful signatures, and cold structural navigation without pretending map/event/presentation semantics are fully solved.",
            Scope: "Read-only only. No map renderer, no event compiler, no timeline decoder, and no container writer.",
            IconKey: "IconContainers",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "battle-corpus-crosswalk",
            Title: "Extras / Battle Corpus Crosswalk",
            Description: "Crosswalk battleId -> formation slots 0..7 -> actor rows 0..10 -> rawMonsterId -> parser corpus overlay without promoting watchlist rows into composition truth.",
            Mode: "Read-Only Crosswalk",
            Notes: "This is the honest Pt44 cut: battle composition, encounter references, and corpus overlay in one cold surface, with rows 8..10 kept as runtime watchlist only.",
            Scope: "Read-only only. No owner/target/runtime claims, no formation writer, and no dispatch promotion.",
            IconKey: "IconCrosswalk",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Extras,
            RequiresProject: true),
        // 🚫 DESATIVADO (2026-08-02, Jarvis-IFRIT, a pedido do usuário): "ps2-knowledge"
        // (Extras / PS2 Knowledge) — hub read-only de "proveniência/estrutura" do PS2 sem decoders,
        // sem playback, sem writer — o usuário: "não tem como usar isso, não sabemos usar".
        // VALOR MUITO BAIXO. O conhecimento real vive nos docs (KNOWLEDGE_BASE.md / docs/reverse/).
        // NÃO reativar sem RT2. (entry removida: ps2-knowledge)

        // ===== Wave-1 / ??? (2) =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "albhed-dictionary",
            Title: "Text / Al Bhed Dictionary",
            Description: "Edit US Latin and JP kana glyph mappings in albheddic.bin with byte-safe undo/save.",
            Mode: "Writer",
            Notes: "Safe scope: mapped glyph and group bucket on existing rows. Table shape and source bytes stay fixed.",
            Scope: "WRITABLE · menu/albheddic.bin (US + JP locales).",
            IconKey: "IconTranslate",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Wave1,
            RequiresProject: true),
        // ===== AI Tools (Jarvis-MAGIC-IA) =====
        new ModuleCatalogPolicy.ModuleCatalogEntry(
            Id: "ai-assistant",
            Title: "AI Assistant",
            Description: "BYOK AI coding assistant. Connect your own LLM endpoint (API key in memory, zeroized on disconnect). Returns PatchProposal — nothing is written without a proven recipe + human approval.",
            Mode: "AI Assistant",
            Notes: "Bring-your-own-key: enter your own API key (memory only). Connects to any OpenAI-compatible endpoint. Remote endpoints require human confirmation. Proposal protocol (P2B).",
            Scope: "AGENT · LLM-powered proposal protocol. No direct file writes from this panel.",
            IconKey: "IconMagicEffect",
            Cluster: ModuleCatalogPolicy.ModuleCluster.Live,
            RequiresProject: false),
        // 🚫 DESATIVADO (2026-08-02, Jarvis-IFRIT): "pointer-script-table" — placeholder "??? / Wave-1
        // sem editor" (sem tela dedicada; infuncional para o usuário). NÃO reativar sem editor real + RT0.
        // Ver bloco de desativação acima.
        // (entry removida: pointer-script-table)
    };

    /// <summary>
    /// Public navigation surface for release builds. Research and runtime-validation labs remain
    /// in <see cref="All"/> for development and future promotion, but are not advertised as
    /// product features before their explicit release gates are satisfied.
    /// Computed per access (not cached) because the AI Assistant is opt-in:
    /// <see cref="AiFeatureGate"/> hides it from rail/grid/palette until the user enables it.
    /// </summary>
    public static IReadOnlyList<ModuleCatalogPolicy.ModuleCatalogEntry> Public =>
        All.Where(entry =>
            !NotPublicBaselineIds.Contains(entry.Id)
            && (entry.Id != Core.AiFeatureGate.ModuleId || Core.AiFeatureGate.Enabled))
        .ToArray();

    /// <summary>Procura uma entrada por Id; retorna null se não existir.</summary>
    public static ModuleCatalogPolicy.ModuleCatalogEntry? Find(string id)
    {
        foreach (var entry in All)
        {
            if (entry.Id == id)
                return entry;
        }
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    public enum AiBibleEntryKind
    {
        Function,
        Field,
        Target,
        Opcode,
        Command,
        Atlas,
        Pattern,
        Guardrail,
    }

    public sealed class AiBibleEntry
    {
        public required AiBibleEntryKind Kind { get; init; }
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public string StackShape { get; init; } = "";
        public string Guardrail { get; init; } = "";
        public string Evidence { get; init; } = "";
        public string Tags { get; init; } = "";
        public string Domain { get; init; } = "";

        // Structured read-only provenance carried over from the Spira Data Atlas detail/dataset rows so the BIBLE UI
        // can surface DetailKind / SourcePath / WriterPolicy without re-parsing StackShape or Tags. Empty for the
        // hand-authored ATEL entries (function/field/target/opcode/pattern/guardrail), which are not Atlas-derived.
        public string SourcePath { get; init; } = "";
        public string WriterPolicy { get; init; } = "";
        public string DetailKind { get; init; } = "";

        public string KindLabel => Kind.ToString();

        IReadOnlyList<AiBibleBadge>? _evidenceBadges;
        IReadOnlyList<string>? _evidenceTokens;

        // Canonical, ordered evidence-health badges/tokens (proved / RT0 / parser-corpus / metadata-only / blocked /
        // RT2-pending / read-only ...). Single source of truth shared by the UI badges, the evidence filters and
        // search. Derived only from fields the entry already carries — it never invents proof. Cached because the
        // catalog is built once and reused across every refresh/search.
        public IReadOnlyList<AiBibleBadge> EvidenceBadges => _evidenceBadges ??= AiBibleEvidence.Badges(this);
        public IReadOnlyList<string> EvidenceTokens =>
            _evidenceTokens ??= EvidenceBadges.Select(b => b.Token).ToList();

        public string SearchBlob =>
            $"{KindLabel} {Id} {Title} {Summary} {StackShape} {Guardrail} {Evidence} {Tags} {Domain} " +
            $"{DetailKind} {SourcePath} {string.Join(' ', EvidenceTokens)}";
    }

    public enum AiBibleBadgeSeverity
    {
        Proof,
        Corpus,
        Caution,
        Blocked,
        Neutral,
    }

    public readonly record struct AiBibleBadge(string Token, AiBibleBadgeSeverity Severity, int Rank, string Tooltip);

    // Derives the BIBLE evidence-health badges from the fields an entry already exposes. It is intentionally
    // conservative: a token only appears when an existing Evidence/WriterPolicy/Guardrail/Tags/Kind/Id field
    // justifies it, and the proof-class tokens (RT2/IDA/proved/RT0) never imply runtime or writer authority — the
    // tooltips keep that honest. Proof signals are read from Evidence/Tags; "pending"/"blocked" gates from
    // WriterPolicy/Guardrail, mirroring how the corpus records them.
    public static class AiBibleEvidence
    {
        public static IReadOnlyList<AiBibleBadge> Badges(AiBibleEntry e) =>
            Derive(e.Evidence, e.Tags, e.WriterPolicy, e.Guardrail, e.Domain,
                rt0Backed: e.Id.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase) || e.Kind == AiBibleEntryKind.Command,
                isGuardrailKind: e.Kind == AiBibleEntryKind.Guardrail,
                readOnlySource: e.Id.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase));

        // Core derivation, parameterized so BIBLE entries AND raw Spira Data Atlas detail/dataset rows (the read-only
        // module badges) go through the SAME engine — the inline module strip and the full BIBLE window can never
        // disagree for the same data. rt0Backed marks rows covered by an RT0 self-test; isGuardrailKind/readOnlySource
        // carry the two entry-shape facts the raw strings cannot express. IDA stays a SEPARATE structural-only proof
        // badge and never collapses into a runtime "proved" claim.
        public static IReadOnlyList<AiBibleBadge> Derive(
            string? evidence, string? tags, string? writerPolicy, string? guardrail, string? domain,
            bool rt0Backed, bool isGuardrailKind, bool readOnlySource)
        {
            string evidenceHay = ((evidence ?? "") + " " + (tags ?? "")).ToLowerInvariant();
            string policyHay = ((writerPolicy ?? "") + " " + (guardrail ?? "")).ToLowerInvariant();
            string allHay = evidenceHay + " " + policyHay + " " + (domain ?? "").ToLowerInvariant();

            List<AiBibleBadge> badges = new();
            void Add(string token, AiBibleBadgeSeverity severity, int rank, string tooltip)
            {
                if (!badges.Any(b => b.Token.Equals(token, StringComparison.OrdinalIgnoreCase)))
                    badges.Add(new AiBibleBadge(token, severity, rank, tooltip));
            }
            static bool HasAny(string hay, params string[] needles) =>
                needles.Any(n => hay.Contains(n, StringComparison.Ordinal));

            bool blocked = allHay.Contains("blocked", StringComparison.Ordinal);
            if (blocked)
                Add("blocked", AiBibleBadgeSeverity.Blocked, 0,
                    Strings.U_Bb_BlockedWriter);

            // Proof signals. In Evidence, an RT2 mention records a proven byte-local/runtime cut (e.g. "RT2 byte-local",
            // "RT2 m034/Tidus"); in WriterPolicy/Guardrail, RT2 is a pending gate (handled below).
            bool proofRt2 = evidenceHay.Contains("rt2", StringComparison.Ordinal);
            if (proofRt2)
                Add("RT2", AiBibleBadgeSeverity.Proof, 10,
                    Strings.U_Bb_Rt2Proven);
            // IDA is a SEPARATE, structural-only proof badge: it confirms ABI/estrutura, never a runtime/byte effect.
            // It must NEVER collapse into a generic green "proved" with a runtime-proof tooltip.
            if (((evidence ?? "") + " " + (tags ?? "")).Contains("IDA", StringComparison.Ordinal))
                Add("IDA", AiBibleBadgeSeverity.Proof, 11,
                    Strings.U_Bb_ReConfirmed);
            // "proved" requires an EXPLICIT offline-proof phrase. A bare "AiScriptLab" tool mention or an "IDA"
            // structural confirmation is NOT a runtime/byte proof, so neither flips this green Proof badge.
            if (evidenceHay.Contains("offline proven", StringComparison.Ordinal)
                || allHay.Contains("byte-prov", StringComparison.Ordinal))
                Add("proved", AiBibleBadgeSeverity.Proof, 12,
                    "Provado offline (byte-consistente / round-trip). Efeito em runtime ainda e RT2.");

            if (rt0Backed || evidenceHay.Contains("rt0", StringComparison.Ordinal))
                Add("RT0", AiBibleBadgeSeverity.Proof, 13,
                    Strings.U_Bb_Rt0SelfTest);

            // Corpus signals.
            if (HasAny(evidenceHay, "parser-corpus", "ffxdataparser", "target-text-corpus"))
                Add("parser-corpus", AiBibleBadgeSeverity.Corpus, 20,
                    Strings.U_Bb_CorpusProof);
            if (evidenceHay.Contains("presence-index", StringComparison.Ordinal))
                Add("presence-index", AiBibleBadgeSeverity.Corpus, 21,
                    Strings.U_Bb_PresenceIndex);

            // Caution signals.
            if (HasAny(allHay, "partial", "unresolved", "placeholder"))
                Add("partial", AiBibleBadgeSeverity.Caution, 29,
                    Strings.U_Bb_Partial);
            if (evidenceHay.Contains("metadata-only", StringComparison.Ordinal)
                || policyHay.Contains("metadata-only", StringComparison.Ordinal))
                Add("metadata-only", AiBibleBadgeSeverity.Caution, 30,
                    Strings.U_Bb_MetadataOnly);
            if (evidenceHay.Contains("semantic-candidate", StringComparison.Ordinal))
                Add("semantic-candidate", AiBibleBadgeSeverity.Caution, 31,
                    Strings.U_Bb_SemanticCandidate);
            bool pendingRt2 = !proofRt2 && (policyHay.Contains("rt2", StringComparison.Ordinal)
                || allHay.Contains("sin-candidate", StringComparison.Ordinal));
            if (pendingRt2)
                Add("RT2-pending", AiBibleBadgeSeverity.Caution, 32,
                    Strings.U_Bb_Rt2Pending);
            if (isGuardrailKind && !blocked)
                Add("guardrail", AiBibleBadgeSeverity.Caution, 33,
                    "Product guardrail: decision lock before authoring.");

            // Neutral baseline.
            if (allHay.Contains("read-only", StringComparison.Ordinal) || readOnlySource)
                Add("read-only", AiBibleBadgeSeverity.Neutral, 90,
                    Strings.U_Bb_ReadOnly);
            if (badges.Count == 0)
                Add("reader", AiBibleBadgeSeverity.Neutral, 80,
                    Strings.U_Bb_ReaderBacked);

            badges.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            return badges;
        }

        public static IReadOnlyList<string> Tokens(AiBibleEntry e)
        {
            IReadOnlyList<AiBibleBadge> badges = Badges(e);
            string[] tokens = new string[badges.Count];
            for (int i = 0; i < badges.Count; i++)
                tokens[i] = badges[i].Token;
            return tokens;
        }
    }

    // Honest "where it appears in the game" text for a BIBLE entry, kept per-domain and conservative: parser-corpus
    // presence is not runtime behaviour, SIN eligibility is a queue not an authorization, w_name is the name/model
    // table row not the reward-name authority, Blitzball prizes are metadata-only, and Sphere Unknown6 stays raw.
    // Single source of truth shared by the BIBLE window and the read-only Atlas module badges. Uses the human
    // Summary / SourcePath, never the raw CSV Detail/StackShape block. Returns "" when there is nothing honest to say.
    public static class AiBibleWhereAppears
    {
        public static string For(AiBibleEntry entry)
        {
            if (entry.Kind == AiBibleEntryKind.Command)
            {
                int at = entry.Summary.IndexOf("Aparece em ", StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                    return entry.Summary[at..];
            }

            string honest = entry.Domain.ToLowerInvariant() switch
            {
                "monster-presence" => Strings.U_Bb_MonsterPresence,
                "sin-eligibility" => Strings.U_Bb_SinEligibility,
                "gear-name-model" => Strings.U_Bb_GearNameModel,
                "blitzball-prize" => "Premio de Blitzball normalizado (parser existe): regra prize+220->takara = proved-candidate offline para treasure; tech = partial; overdrive = metadata-only; premio especifico por-evento = blocked (runtime/save). Nao e RT2/in-game.",
                "pc-aeon-fine" => "Stats finos de PC/Aeon lidos do save catalog (leitura); o indexer fino de sum_grow.bin continua pendente.",
                "player-growth" => "Stats/curva de growth de PC/Aeon (ply_save.bin + ply_rom.bin, byte-grounded, regiao new_uspc): base stats + curva de AP + coeficientes de growth. Coef de stat e Aeon-only (PC cresce via Sphere Grid; growth de PC no ROM = curva de AP). A formula in-game de AP/coef continua RT2-pending. Read-only, nao e writer nem autoridade final.",
                "mix" => "Combinacao de Mix (prepare.bin, proved-candidate offline): a tabela par->resultado e parser-corpus byte-grounded; o efeito do Mix em jogo continua RT2-pending. Nao e writer nem autoridade final do resultado.",
                "aeon-growth" => "Crescimento/customizacao de Aeon (sum_grow.bin, proved-candidate offline, Aeon-only / Target 0x007F): a tabela entry->resultado (habilidade ou stat) e parser-corpus byte-grounded; o custo de stat-recipe e partial e o efeito em jogo continua RT2-pending. Nao cobre PC nem topologia do Sphere Grid. Read-only, nao e writer nem autoridade final.",
                "sphere-grid-node" => "No granular do Sphere Grid: mapeamento de posicao/conteudo/links/cluster do no (layout dat01/02/03 + panel.bin/sphere.bin). Node.Unknown6 (no +0x0A) e gameplay-inert (IDA-provado nos dois binarios: o gameplay le de dat09/10/11 + reach-lists, nunca do +0x0A); o residuo possivel e layout/visual do menu, sem leitura positiva achada (metadata-only). Read-only, sem semantica de gameplay inventada.",
                "atel-command-sites" => Strings.U_Bb_AtelCommandSites,
                "atel-worker" => Strings.U_Bb_AtelWorker,
                "atel-branch" => "Branch/control-flow shape aggregated from the ATEL corpus; authoring goes through AiScriptLab/AEON.",
                "atel-field-shape" => Strings.U_Bb_AtelFieldShape,
                "atel-call-shape" => "Call shape in the ATEL corpus (namespace/sites), presence-index.",
                _ => "",
            };
            if (honest.Length > 0)
                return $"{honest} {entry.Summary}".Trim();

            if (entry.Id.StartsWith("atlas:monster:", StringComparison.OrdinalIgnoreCase))
                return entry.Summary;

            if (entry.Id.StartsWith("pattern:corpus:", StringComparison.OrdinalIgnoreCase))
                return entry.Summary;

            if (entry.Domain.Equals("monster-corpus", StringComparison.OrdinalIgnoreCase)
                && (entry.Tags.Contains("where-appears", StringComparison.OrdinalIgnoreCase)
                    || entry.Summary.Contains("battle appearance", StringComparison.OrdinalIgnoreCase)
                    || entry.Summary.Contains("Corpus:", StringComparison.OrdinalIgnoreCase)))
                return entry.Summary;

            if (entry.Id.StartsWith("atlas:parsed-file:", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(entry.SourcePath)
                    ? "Raw parsed file of the FFXDataParser corpus (presence-index)."
                    : string.Format(Strings.U_Bb_RawParsed, entry.SourcePath);

            if (entry.Domain.StartsWith("atel-", StringComparison.OrdinalIgnoreCase))
                return $"Appears in the ATEL corpus as {entry.Domain} (presence-index, metadata-only).";

            return "";
        }
    }

    // BIBLE OF SPIRA: read-only contextual knowledge for the Monster AI Editor. It is intentionally a catalog,
    // not an authoring layer. Entries are grounded in the shipped codec/dictionaries first, then in the mission doc.
    public static class AiBibleCatalog
    {
        public const string DisplayName = "BIBLE OF SPIRA";

        static readonly Lazy<IReadOnlyList<AiBibleEntry>> _all = new(Build);
        public static IReadOnlyList<AiBibleEntry> All => _all.Value;

        static IReadOnlyList<AiBibleEntry> Build()
        {
            List<AiBibleEntry> entries = new()
            {
                Guard(
                    "guard:setstat-signature",
                    "setStatField nao recebe actorRef",
                    "Assinatura normalizada contra o codigo real: setStatField usa o field space btlActorProperty, mas a forma usada pelo editor e field + value + CALLPOPA 70AB. Para escrita com actor explicito, use writeChrProperty.",
                    "setStatField: PUSHII <field> · PUSHII/PUSHF <value> · CALLPOPA 70AB | writeChrProperty: PUSHII <actor> · PUSHII <field> · PUSHII <value> · CALLPOPA 7018",
                    "Impede espalhar receita errada a partir de um unico caso. Shiva e outros bosses servem como amostras; qualquer template novo deve passar por corpus, AiScriptLab e RT2 antes de virar authoring.",
                    "AiSnippetLibrary.cs + AiScript_File.FieldArgBack + docs/ai/ATEL_BIBLE_RESEARCH_MISSION_2026-06-07.md"),
                Guard(
                    "guard:opcode-16-17-arithmetic",
                    "Opcode 0x16/0x17: regression guard do HP%",
                    "A tabela ATEL provada e 0x16=MUL e 0x17=DIV; o snippet HP% deve emitir MUL(0x16). Se DIV(0x17) reaparecer nesse idiom, trate como regression guard, nao comportamento esperado.",
                    "0x16 = MUL; 0x17 = DIV; HP% shape: HP*100 < maxHP*X.",
                    "Nao bloquear autoria humana por nomenclatura antiga. Template/botao HP% exige emitter MUL(0x16), AiScriptLab/AEON/backup e prova RT2/operator antes de promocao publica.",
                    "AiScript_File + mission doc + AiSnippetLibrary regression guard"),
                Guard(
                    "guard:native-hp-threshold-label",
                    "HP nativo: nao auto-rotular porcentagem incerta",
                    "O caso Dark Flan m021/White Wind mostra um guard de HP cuja leitura humana ainda diverge: o corpus/oraculo sugere HP < maxHP / 3, mas o disassembly local imprime HP < maxHP * 3. Isto deve virar badge de incerteza, nao uma promessa de HP% automatica.",
                    "m021 w1_e3: readChrProperty(Self, HP) · readChrProperty(Self, maxHP) · PUSHII 3 · opcode 0x17 · LT · branch para White Wind.",
                    "Antes de converter thresholds nativos para 'HP < X%' na UI, reconciliar opcode/ordem de operandos/oraculo. O HP% autorado pelo editor continua separado: HP*100 < maxHP*X com MUL(0x16).",
                    "AiScriptLab --dump m021 + docs/ai/MONSTER_AI_DARK_FLAN_PROBABILITY_TREE_AUTHORING_2026-06-11.md"),
                Guard(
                    "guard:encounter-opener-boundary",
                    "Opener de frame-0 pode vir do encounter, nao do monstro",
                    "O caso Seymour/Guado fechou um boundary importante: o primeiro Shell/Protect antes da party agir nao nasceu de truque local no opener do m124. O proprio mcyt06_00 seeda CTB no HookStart usando FirstStrike e CurrentTurnDelay com formacao real favoravel aos inimigos.",
                    "mcyt06_00 HookStart: AllMonsters.FirstStrike=1; AllMonsters.CurrentTurnDelay=0; Monster#01 [0x0015].CurrentTurnDelay=1; Character#1/#2/#3 e reserves +=2.",
                    "Quando um boss parecer agir antes da party, abrir tambem o btlbin/encontro. Monster AI Editor e Aurora podem surfacingar isso em modo read-only; authoring de HookStart/seed CTB ainda nao e writer publico.",
                    "SEYMOUR_OPENER_CTB_SETUP_RESEARCH_RESULT_2026-06-30.md + mcyt06_00.txt + SEYMOUR_M124_SECOND_LAYER_RE_TRUTH_2026-06-29.md"),

                Pattern(
                    "pattern:encounter-ctb-seed",
                    "Padrao: encounter seeda CTB no frame-0",
                    "Alguns openers nao dependem de forcePerformCommand nem de write local de CTB no monstro. O proprio encounter pode empurrar inimigos e party antes do primeiro menu, usando HookStart/StartEndHooks e fields como FirstStrike e CurrentTurnDelay.",
                    "battle/btl mcyt*.bin -> HookStart -> FirstStrike/CurrentTurnDelay/formation; depois o monstro roda seu onTurn normal.",
                    "Nao achatar isso para 'truque do monstro'. Se a ordem inicial depender do encounter, a superficie honesta no editor e crosswalk/read-only ate existir writer seguro do btlbin.",
                    "mcyt06_00 HookStart + slot0=m141 slot1=m124 slot2=m141 slot3=m125 + Seymour opener proof 2026-06-30"),

                Pattern(
                    "pattern:shiva-overdrive",
                    "Padrao Shiva: barra de Overdrive",
                    "A Shiva e uma ancora didatica, mas a rota mais forte agora vem do censo Aeon/Dark Aeon: showOverdriveBar, OverdriveMax e OverdriveCurrent explicam uma barra visivel controlada por AI, com setup visto como writeChrProperty(Self,field,value).",
                    "Campos-chave: showOverdriveBar=0x0089, OverdriveMax=0x0014, OverdriveCurrent=0x0013. Setup LAB do editor usa CALLPOPA 7018; setStatField 70AB continua sendo outro botao/shape.",
                    "Template publico ainda precisa RT2 visual em monstro descartavel. Carga dinamica/finisher automatico devem usar o corpus --overdrive-flow, nao so a Shiva-Yuna isolada.",
                    "MONSTER_AI_OVERDRIVE_DYNAMIC_FLOW_RESEARCH_2026-06-13.md + AiChrPropertyNames"),

                Pattern(
                    "pattern:corpus-wide-mechanic-mining",
                    "Mineracao corpus-wide de mecanicas",
                    "A BIBLE OF SPIRA deve tratar cada monstro com AiFile como fonte potencial de receita: fases, counters, retaliacao, buffs, alvos amplos, comandos raros, Overdrive-like, revive, drops/script flags e targets especiais. Shiva e so uma das amostras.",
                    "Fluxo: detectar pattern no corpus -> agrupar por familia -> abrir disassembly dos representantes -> normalizar stack shape -> criar BIBLE entry -> so entao propor preset SIN.",
                    "Nao promover mecanica baseada em 1 monstro quando existirem 300+ scripts para comparar. Templates publicos precisam de envelope: corpus evidence, AiValidator, AEON diff, backup e RT2.",
                    "BIBLE_OF_SPIRA_MONSTER_AI_CORPUS_ATLAS_2026-06-08.md + AiScriptLab --sin-census + mission-doc"),

                Pattern(
                    "pattern:hp-phase",
                    "Fase por HP%",
                    "Idioma ja usado no editor: ler HP e maxHP, multiplicar com MUL(0x16), comparar, e anexar uma acao guardada. E o caminho honesto para enrage de chefe.",
                    "readChrProperty(self, HP) * 100 < readChrProperty(self, maxHP) * X -> forcePerformCommand",
                    "Estrutura offline provada; efeito final de comando/grow continua RT2.",
                    "AiSnippetLibrary guard-hp-below-pct-force-cmd + AiScriptLab --ai2/--ai3"),

                Pattern(
                    "pattern:rng",
                    "Aleatoriedade 1-em-K",
                    "Padrao de chance: GetRandomValue, MOD K, compara com zero, depois executa a acao se verdadeiro.",
                    "CALL Common.GetRandomValue · PUSHII K · MOD · PUSHII 0 · EQ",
                    "K=0 deve virar fallback seguro; nao usar RNG como prova de turno.",
                    "AiSnippetLibrary guard-rng-force-cmd"),

                Pattern(
                    "pattern:target-alive-frontline-any",
                    "Alvo: personagem vivo aleatorio da frontline",
                    "Receita simples observada nos Flans m016-m019 e exposta no Monster AI Editor como alvo calculado. Escolhe um personagem vivo da linha de frente no momento em que a acao roda.",
                    "PUSHII FrontlineChars · PUSHII isAlive · PUSHII 0 · PUSHII 0/Any · CALL findMatchingChr -> performCommand",
                    "E uma receita de alvo de uma acao, nao combo e nao multi-cast. Estrutura offline/gate provada; RT2 do efeito final ainda depende do comando escolhido.",
                    "AiAutomation FindAliveFrontlineAny + AiScriptLab --ai3 target recipe 656/656 + m016-m019 corpus"),

                Pattern(
                    "pattern:target-alive-frontline-lowest-hp",
                    "Alvo: menor HP vivo da frontline",
                    "Receita calculada baseada no idioma do Shred: primeiro monta MatchingGroup com personagens vivos da linha de frente, depois escolhe o membro vivo com menor HP.",
                    "PUSHII FrontlineChars · PUSHII isAlive · PUSHII 0 · PUSHII 0/Any · CALLPOPA findMatchingChr; PUSHII MatchingGroup · PUSHII HP · PUSHII 0 · PUSHII 2/Lowest · CALL findMatchingChr -> performCommand",
                    "E uma receita de alvo de uma acao. Nao confundir com o Shred completo, que tambem tem branch/sorteio/fallback.",
                    "AiAutomation FindAliveFrontlineLowestHp + AiScriptLab --ai3 target recipe 656/656 + Shred idiom"),

                Pattern(
                    "pattern:target-shred-dynamic",
                    "Alvo: Shred dinamico completo",
                    "O Shred nao e apenas 'random'. O handler mistura sorteio de rota, tentativa de alvo literal 2 quando valido e fallback para personagem vivo calculado por findMatchingChr.",
                    "GetRandomValue % 2 -> branch; se rota literal: alvo 2 apenas se isOnFrontline/isAlive; senao FrontlineChars + isAlive + findMatchingChr; outra rota tambem usa findMatchingChr.",
                    "Modelo estrutural conclusivo para entendimento, mas ainda nao e um dropdown simples de alvo. Para authoring, precisa virar construtor composto/branch antes de promessa publica ampla.",
                    "m Shred disassembly + AiAutomation target recipes + user RT2 target-change observed"),

                Pattern(
                    "pattern:dark-flan-probability-tree",
                    "Dark Flan: roleta de decisao",
                    "O Dark Flan m021 nao e uma lista linear de Bio/Drain/Osmose/Flare. O CombatHandler sorteia 50/50: uma rota usa Demi em FrontlineChars; a outra calcula um personagem vivo da linha de frente e escolhe 1 entre Bio, Drain, Osmose e Flare. O sustain com White Wind fica separado e tem threshold HP nativo com porcentagem ainda pendente.",
                    "GetRandomValue % 2 -> se 0: performCommand(FrontlineChars, Demi) -> RET; senao: findMatchingChr(FrontlineChars,isAlive,Any)->target; GetRandomValue % 4 -> Bio/Drain/Osmose/Flare no target -> RET.",
                    "Renderizar como arvore/roleta, nao como combo. Para authoring publico, precisa de builder de escolha ponderada/sub-roleta, AEON diff, backup e RT2 por fluxo.",
                    "offline proven by AiScriptLab --dump m021; docs/ai/MONSTER_AI_DARK_FLAN_PROBABILITY_TREE_AUTHORING_2026-06-11.md"),

                Pattern(
                    "pattern:dynamic-cycle-stop-continue",
                    "Ciclo dinamico: chance + parar/continuar",
                    "O Flame Flan m020 modado mostrou a forma humana poderosa: cada passo tem chance local, uma acao, um efeito extra opcional e uma decisao separada de parar ou continuar. Isto nao e uma lista que soma 100%, nem necessariamente combo nativo.",
                    "GetRandomValue % K -> se passar: performCommand(target, cmd) -> opcional writeChrProperty(target, status, value) -> RET para parar ou JMP/fallthrough para continuar.",
                    "A UI deve mostrar badges 'chance local', 'para se rodar', 'continua se rodar' e 'fallback obrigatorio'. Multi-* e nome tecnico de comando/familia; receitas compostas podem receber apelidos humanos como Sequencial-Thunder sem vender multicast garantido.",
                    "AiScriptLab --dump m020 + docs/ai/MONSTER_AI_DYNAMIC_CYCLE_AUTHORING_NOTES_2026-06-11.md"),

                Guard(
                    "guard:ribbon-bypass",
                    "Ribbon bypass nao e magia normal",
                    "Ribbon e auto-ability de endgame que bloqueia quase todos os status pela rota comum de comando/magia. O recorte RT2 m034 -> Tidus/Character#1 provou que writeChrProperty direto marca Zombie, Confuse, Silence, Darkness, Curse e Doom apesar do Ribbon. O operador tambem reportou prova in-game positiva, pela mesma rota de field direto, para Poison, Petrify, Power/Magic/Armor/Mental Break, Berserk, Sleep e Slow.",
                    "Bypass provado no AI: writeChrProperty(Character#1, StatusZombie, 1), StatusConfusion=1, StatusDurationSilence=255, StatusDurationDarkness=255, StatusCurse=1, DoomCounterInitial=5, DoomCounterCurrent=5, StatusDoom=1. Expansao operator-tested: StatusPoison=1, StatusPetrify=1, StatusPowerBreak=1, StatusMagicBreak=1, StatusArmorBreak=1, StatusMentalBreak=1, StatusBerserk=1, StatusDurationSleep=255, StatusDurationSlow=255. Provoke/Threaten sao fields diretos avancados liberados para teste proprio. Sticky provado so em runtime: status_full_auto/status_innate_auto = 0102/0806/4400.",
                    "Nao generalizar para todo alvo. FrontlineChars/AllActors/LastAttacker e comandos normais ainda exigem RT2 proprio. Death/KO nao entra como status simples aqui: no btlActorProperty atual existe isAlive, nao um StatusDeath direto equivalente. O sticky full_auto/innate_auto ainda nao tem field btlActorProperty/bytecode de IA provado; tratar como LAB ate mapear writer seguro.",
                    "AiChrPropertyNames + AutoAbility_Dictionary Ribbon + MemoryChr Status_suffer/Status_resist/full_auto/innate_auto + SIN_STICKY_STATUS_OFFLINE_MAPPING_2026-06-08"),

                Pattern(
                    "pattern:direct-status",
                    "Status direto no battle actor",
                    "Caminho para status que nao passa pela formula normal de hit/resistencia: escrever o field de status no ator alvo via writeChrProperty. RT2 provou o pacote anti-Ribbon em m034 contra Tidus: Zombie + Confuse + Silence(255) + Darkness(255) + Curse + Doom(5). A expansao liberada no LAB usa o mesmo shape para Poison, Petrify, breaks, Berserk, Sleep(255) e Slow(255), com prova in-game reportada pelo operador.",
                    "writeChrProperty: PUSHII <target> · PUSHII <StatusField> · PUSHII <value> · CALLPOPA 7018",
                    "Nao usar setStatField para ator explicito. Sleep/Silence/Darkness/Slow aqui sao campos de duracao, nao flags booleanas. Death/KO exige rota propria; nao tratar isAlive=0 como status comum sem gate dedicado.",
                    "AiAutomation writeChrProperty + MemoryChr runtime status fields + RT2 m034/Tidus 2026-06-08"),

                Pattern(
                    "pattern:sticky-status-runtime",
                    "Status sticky / anti-remocao LAB",
                    "Depois do pacote Forbidden Rite, o teste runtime marcou status_full_auto e status_innate_auto no Tidus com as mesmas mascaras do pacote: 0102/0806/4400. Holy Water/Eye Drops/Remedy/Esuna nao removeram a situacao durante o monitor; Doom tickou e voltou para 5.",
                    "MemoryChr runtime: suffer=0102, turns sil/dark=255/255, extra=4400, full_auto=0102/0806/4400, innate_auto=0102/0806/4400.",
                    "Isto e prova RT2 LAB de memoria runtime, nao prova de writer ATEL. Nao transformar em botao SIN ate achar se full_auto/innate_auto sao acessiveis por btlActorProperty/writeChrProperty ou exigem outro writer/hook.",
                    "MemoryChr offsets 0x62A/0x62C/0x62E and 0x630/0x632/0x634 + live monitor 2026-06-08 + offline mapping doc"),

                Pattern(
                    "pattern:round-counter",
                    "Rodada N / stat_round",
                    "A biblia aponta stat_round como campo de fase/rodada. Use como pista de leitura ate o comportamento exato do monstro ser confirmado.",
                    "Campo principal: stat_round 0x00DA.",
                    "Nao confundir com contador privado por monstro sem prova do script observado.",
                    "AiChrPropertyNames + mission-doc"),

                Pattern(
                    "pattern:sin-one-shot",
                    "SIN one-shot via private variable",
                    "Padrao para garantir que um preset SIN executa apenas uma vez por batalha, usando uma private variable do monstro como flag. Entrypoint 0 (init) inicializa VAR=0; entrypoint 2 (onTurn) testa se VAR==0 antes de agir; apos acao, incrementa VAR para 1. Usado por UNI-001 e UNI-004 no SinScaleInject.",
                    "Init: PUSHII 0 · POPV varIndex | Guard: PUSHV varIndex · PUSHII 0 · EQ · POPXNCJMP -> skip | Apos acao: PUSHV varIndex · PUSHII 1 · ADD · POPV varIndex",
                    "Slot de variavel precisa ser livre (TryFindFreePrivateVariableSlot). Verificar se o monstro alvo ja usa private variables no seu AI vanilla para evitar conflito.",
                    "SIN_UNI_INCONSISTENCIAS.md + sin-curse-lab-handoff"),
                Pattern(
                    "pattern:sin-always-true-guard",
                    "SIN always-true guard fallback",
                    "When no guard condition is needed for the SIN preset, use PUSHII 1 before POPXNCJMP. This prevents POPXNCJMP from consuming garbage from the stack (undefined behavior). Alternative to the empty guard that caused an execution bug.",
                    "PUSHII 1 · POPXNCJMP -> handler",
                    "Do not remove the PUSHII 1 thinking it is 'useless'. POPXNCJMP always consumes 1 bool from the stack; without it, it consumes garbage.",
                    "SIN_UNI_INCONSISTENCIAS.md + sin-curse-lab-handoff"),
                Guard(
                    "guard:sin-heal-perform-not-force",
                    "Heals SIN usam performCommand (0x700B), nao forcePerformCommand (0x705A)",
                    "SIN presets that heal (Cure, White Wind) MUST use performCommand (0x700B) to respect the turn/MP queue. Using forcePerformCommand (0x705A) can cause a heal loop on the player's turn, as the ignored command is re-queued. UNI-007 and UNI-008 corrected to use 0x700B.",
                    "Heal: PUSHII <target> · PUSHII <commandId> · CALLPOPA 700B | Force (ERRADO): PUSHII <target> · PUSHII <commandId> · CALLPOPA 705A",
                    "This rule applies to any command that heals or buffs allies. Damage commands can use force normally. Do not confuse a heal command with a damage command just because both are 'actions'.",
                    "SIN_UNI_INCONSISTENCIAS.md + sin-curse-lab-handoff"),
                Guard(
                    "guard:sin-entrypoint-overwrite",
                    "SIN entrypoint overwrite: 1 preset por entrypoint",
                    "Each ATEL entrypoint (0=init, 2=onTurn, 3=onHit) accepts only ONE SIN preset. Injecting multiple presets into the same entrypoint overwrites the previous one. UNI-002 is the only preset that uses entrypoint 3 (onHit); all others use entrypoint 2 (onTurn). Mixing entrypoints causes preset loss.",
                    "Entrypoint 2 (onTurn): UNI-001, 003-008 | Entrypoint 3 (onHit): UNI-002 only.",
                    "Before adding a new preset, check if the entrypoint is already occupied. If the system guarantees 1 preset/monster (offline solution from SinScaleInject), this guardrail is automatically respected.",
                    "SIN_UNI_INCONSISTENCIAS.md + sin-curse-lab-handoff"),
                Guard(
                    "guard:monster-forced-action-offset",
                    "ForcedAction offset = stat_ptr+112 (NAO +62)",
                    "The ForcedAction field in the monster .bin is at stat_ptr+112 (absolute offset ai_ptr+132). This field defines which skill the monster uses when its Overdrive bar fills. The WRONG value sp+62 breaks the OD binding. Confirmed against real bins from 6 areas in HANDOFF_MONSTER_BALANCE_ODS.",
                    "stat_ptr+112 = ai_ptr+132 = operand do monmagic2 para OD skill.",
                    "NEVER use stat_ptr+62. Documented error that caused significant manual rework in Macalania. Validate the offset for each monster before wiring OD.",
                    "HANDOFF_MONSTER_BALANCE_ODS_2026-07-01.md"),

                Func(0x700B, "performCommand",
                    "Enqueues a battle action using commandId and target. Unlike forcePerformCommand: tends to follow the normal queue/turn. In Seymour, even 0x6051 passes through here as a normal command row, but the order of the first Shell/Protect came from the encounter's HookStart. CRITICAL: heals (Cure, White Wind) MUST use performCommand, NOT forcePerformCommand, to avoid a heal loop on the player's turn.",
                    "PUSHII/PUSHV <target> · PUSHII <commandId> · CALLPOPA 700B",
                    "Swapping commandId length-preserving was already RT2; fine-grained target semantics still need to be confirmed per case. Do not treat 0x6051 as a special ATEL opcode just because it participates in the Anima handoff, and do not explain a frame-0 opener based solely on this call without checking the btlbin. Heals always use this call, never 705A.",
                    "codec + corpus + RT2 byte-local + sin-curse-lab-handoff"),

                Func(0x705A, "forcePerformCommand",
                    "Forces a battle action with commandId and target. Used by the library for immediate behavior snippets.",
                    "PUSHII/PUSHV <target> · PUSHII <commandId> · CALLPOPA 705A",
                    "Efeito do comando escolhido e do alvo segue RT2; nao assumir que todo comando serve em toda arena.",
                    "codec + corpus + AiSnippetLibrary"),

                Func(0x7019, "UsedCommand",
                    "Queries which command/trigger is in the current context. In Seymour, it appears with Talk to orchestrate the trigger-command and scene handoff.",
                    "CALL 7019, normalmente perto de comparacoes com command ids especiais.",
                    "Read-only read useful for context; do not promote it to a writer without understanding the surrounding worker/event.",
                    "reader branch-sensitive + corpus Seymour"),

                Func(0x7038, "removeCommand",
                    "Removes a command from the battle context. In Seymour, it appears clearing Talk/trigger-command before the phase handoff.",
                    "PUSHII <commandId> · CALLPOPA 7038",
                    "Do not read this as 'removes spell from catalog'; the effect is contextual to the current actor/event.",
                    "reader Seymour + docs IDA 2026-06-29"),

                Func(0x703C, "runBtlSceneA",
                    "Triggers a contextual/scripted battle cut. In the Seymour package, it accompanies handoff transitions and beats.",
                    "PUSHII <scene/state> · CALLPOPA 703C",
                    "Do not sell it as free summon/authoring. Good reading: contextual/scripted scene.",
                    "reader Seymour + IDA n2_6 research"),

                Func(0x7097, "runBtlSceneB",
                    "Triggers the presentation/handoff layer of the battle scene. In Seymour, it marks the more ceremonial side of the phase change.",
                    "PUSHII <scene/state> · CALLPOPA 7097",
                    "Do not confuse with runBtlSceneA: here the good reading is presentation/handoff, not contextual cut.",
                    "reader Seymour + IDA n2_6 research"),

                Func(0x700F, "readChrProperty",
                    "Reads a btlActorProperty from an actor and pushes the value. Base for HP%, status, and phases per field.",
                    "PUSHII <actor> · PUSHII <field> · CALL 700F",
                    "Field ids desconhecidos ficam hex; nao promover 0x0156/0x0157 sem prova.",
                    "FFXDataParser + corpus + IDA para HP/maxHP/NearDeath"),

                Func(0x7018, "writeChrProperty",
                    "Writes a btlActorProperty to an explicit actor. This is the correct path when the recipe needs actorRef.",
                    "PUSHII <actor> · PUSHII <field> · PUSHII <value> · CALLPOPA 7018",
                    "Field and value need to be confirmed; structure is removable/editable offline, effect is RT2.",
                    "AiSnippetLibrary + AiAutomation"),

                Func(0x70AA, "getStatField",
                    "Reads a btlActorProperty from the current descriptor/stat context. Shares the same field table.",
                    "PUSHII <field> · CALL 70AA",
                    "Do not confuse with readChrProperty when the script operates on another actor.",
                    "FFXDataParser correction + AiScript_File"),

                Func(0x70AB, "setStatField",
                    "Writes a btlActorProperty to the current descriptor/stat context. Shares the same field table, but lacks the actor arg in the form used by the editor.",
                    "PUSHII <field> · PUSHII/PUSHF <value> · CALLPOPA 70AB",
                    "The question 'add or replace?' must be answered by the actual script before the Overdrive button.",
                    "AiSnippetLibrary + AiScript_File.FieldArgBack"),

                Func(0x706C, "readMovePropertyForActor",
                    "Le um moveProperty field de um ator (field id + actor ref). Usado em condicoes AI que consultam propriedades do comando/movimento.",
                    "PUSHII <field> · PUSHII <actor> · CALL 7078",
                    "Nao confundir com readChrProperty (btlActorProperty) nem com FFX_Battle_AggregateActorProperty @ 0x7B2DD0 (nativo readChrProperty/countChrOverlap).",
                    "INFERNO I08 moveProperty; drift audit S04 separou aggregate @ 0x7B2DD0"),

                Func(0x707A, "btlGetCalcResult",
                    "Retorna codigo do ultimo calculo de batalha (hit/miss/crit/absorb/reflect etc.). Dispatcher EXE: FFX_Atel_CallReturnDispatchByNamespace @ 0x877770.",
                    "CALL 707A (void return on stack via namespace-7 handler table)",
                    "Return value table still BLOCKED (INFERNO I02). Do not use in SIN template without RT2 read-only filtering encoded 0x707A.",
                    "docs/reverse/FFX_BTL_GET_CALC_RESULT_RUNTIME_DEEP_2026-06-15.md + INFERNO headless batch"),

                Func(0x00A9, "GetRandomValue",
                    "Gerador de aleatoriedade usado por scripts para selecionar comportamento.",
                    "CALL 00A9, normalmente seguido de PUSHII K · MOD",
                    "Do not use as a turn counter; it is only a chance value.",
                    "codec + behavior snippets"),

                Func(0x6004, "camSetPolar",
                    "Polar camera IDA-proven: horizontal, elevation, and distance. Relevant for AI/camera ATEL, but outside the monster AI writer.",
                    "args on stack: horizontalDeg, elevationDeg, distance",
                    "Target-aware variants 0x6040/0x6044/0x604D do not use the same simple formula.",
                    "BattleCameraScanLab + IDA"),

                Field(0x0000, "HP", "Current HP. Most-read field in the AI corpus; used in the HP% snippet.", "parser + corpus + IDA"),
                Field(0x0002, "maxHP", "HP maximo. Par do HP para condicao percentual.", "parser + corpus + IDA"),
                Field(0x0004, "isAlive", "Actor life flag. This is not a clean StatusDeath; Death/KO requires a separate recipe/gate before it becomes a public Anti-Ribbon button.", "AiChrPropertyNames + MemoryChr KO flag"),
                Field(0x0005, "StatusPoison", "Status Poison; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0006, "StatusPetrify", "Status Petrify/Stone; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0012, "OverdriveMode", "Modo de carregamento/overdrive. Pista da mecanica da Shiva.", "AiChrPropertyNames + mission-doc"),
                Field(0x0013, "OverdriveCurrent", "Current value of the Overdrive bar. For bar setup, the 2026-06-13 corpus favors writeChrProperty(Self,...); for dynamic charge, do not assume implicit addition.", "AiChrPropertyNames + overdrive-flow"),
                Field(0x0014, "OverdriveMax", "Maximum value of the Overdrive bar; appears in full-flow candidates with reads/guards against Current.", "AiChrPropertyNames + overdrive-flow"),
                Field(0x0089, "showOverdriveBar", "Visual flag that shows the Overdrive bar; LAB setup uses writeChrProperty(Self,0x0089,1) via 7018.", "AiChrPropertyNames + overdrive-flow"),
                Field(0x00DA, "stat_round", "Round/phase counter; very useful for script reading.", "parser internalName + mission-doc"),
                Field(0x0114, "TurnsTaken", "Actor turn counter according to the field map; confirm script usage before templating.", "AiChrPropertyNames"),
                Field(0x0119, "NearDeath", "Low HP flag confirmed against IDA as a comparison of current life vs max.", "parser + corpus + IDA"),
                Field(0x0025, "StatusPowerBreak", "Status Power Break; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0026, "StatusMagicBreak", "Status Magic Break; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0027, "StatusArmorBreak", "Status Armor Break; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0028, "StatusMentalBreak", "Status Mental Break; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0007, "StatusZombie", "Status Zombie; RT2 proven via writeChrProperty(Character#1, field, 1) in m034 against Tidus with Ribbon. Without sticky, Holy Water/removal cleared the bit; with full_auto/innate_auto runtime, it remained stuck in the monitor.", "AiChrPropertyNames + RT2 m034/Tidus"),
                Field(0x0029, "StatusConfusion", "Confuse flag in Status_suffer; RT2 proven via writeChrProperty(Character#1, field, 1) in m034 against Tidus with Ribbon. Runtime sticky held it together with the package.", "AiChrPropertyNames + RT2 m034/Tidus"),
                Field(0x002A, "StatusBerserk", "Status Berserk; released in Forbidden Rite via the same direct writeChrProperty and reported as tested in-game by the operator against Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x002B, "StatusProvoke", "Status Provoke; direct field mapped and released as an advanced candidate. Requires its own in-game test because it alters control/target.", "AiChrPropertyNames + field map"),
                Field(0x002C, "StatusThreaten", "Status Threaten; field direto mapeado e liberado como candidato avancado. Exige teste in-game proprio porque altera controle/alvo.", "AiChrPropertyNames + field map"),
                Field(0x002D, "StatusDurationSleep", "Duracao de Sleep; liberado no Forbidden Rite com default 255 pelo mesmo writeChrProperty direto e reportado como testado in-game pelo operador contra Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x002E, "StatusDurationSilence", "Duracao de Silence, nao booleano simples. Valor 255 ficou visivel como Silence(255) em Tidus no RT2 m034 e nao caiu nos testes de remocao observados.", "AiChrPropertyNames + MemoryChr + RT2 m034/Tidus"),
                Field(0x002F, "StatusDurationDarkness", "Duracao de Darkness, nao booleano simples. Valor 255 ficou visivel como Darkness(255); Eye Drops deu Miss no teste do operador e o byte permaneceu 255.", "AiChrPropertyNames + MemoryChr + RT2 m034/Tidus"),
                Field(0x0039, "StatusDurationSlow", "Duracao de Slow; liberado no Forbidden Rite com default 255 pelo mesmo writeChrProperty direto e reportado como testado in-game pelo operador contra Ribbon.", "AiChrPropertyNames + operator RT2"),
                Field(0x0099, "StatusCurse", "Status Curse; RT2 provado por writeChrProperty(Character#1, field, 1) em m034 contra Tidus com Ribbon. Holy Water removeu sem sticky; full_auto/innate_auto runtime segurou.", "AiChrPropertyNames + RT2 m034/Tidus"),
                Field(0x009D, "StatusDoom", "Status Doom; RT2 provado com DoomCounterInitial/Current=5 em m034 contra Tidus com Ribbon. Doom permaneceu apos remocao comum e foi rearmado para 5 no recorte sticky.", "AiChrPropertyNames + RT2 m034/Tidus"),
                Field(0x009F, "DoomCounterInitial", "Valor inicial do contador Doom. RT2 provado com valor 5 no pacote m034/Tidus.", "AiChrPropertyNames + MemoryChr + RT2 m034/Tidus"),
                Field(0x00A0, "?DoomCounterCurrent", "Contador Doom atual, nome ainda parcial mas escrita RT2 provada com valor 5 no pacote m034/Tidus.", "AiChrPropertyNames + MemoryChr + RT2 m034/Tidus"),
                Field(0x00B0, "StatusResistanceZombie", "Resistencia a Zombie no btlActorProperty. Pode explicar por que comandos normais falham contra Ribbon/ward/proof.", "AiChrPropertyNames + AutoAbility status resist"),
                Field(0x00D2, "?StatusImmunityCurse", "Candidato de imunidade a Curse. Nome parcial; manter como pesquisa.", "AiChrPropertyNames"),
                Field(0x00D6, "?StatusImmunityDoom", "Candidato de imunidade a Doom. Nome parcial; manter como pesquisa.", "AiChrPropertyNames"),
                Field(0x0031, "StatusDurationProtect", "Duracao de Protect. Ja usado nos snippets de buff em si.", "AiChrPropertyNames + snippets"),
                Field(0x0032, "StatusDurationReflect", "Duracao de Reflect. Ja usado nos snippets de buff em si.", "AiChrPropertyNames + snippets"),
                Field(0x0038, "StatusDurationHaste", "Duracao de Haste. Ja usado nos snippets de buff em si.", "AiChrPropertyNames + snippets"),
                Field(0x014B, "SIN_PROPERTY_ID (331)", "Property ID do preset SIN ativo no monstro. Projetado para o monstro ler seu proprio preset ID via readChrProperty(self,0x014B) e usar num dispatcher central. ATENCAO: o hook AiQueryProperty que popula este campo foi removido na v2.185.4.0 (crashava menu). O array g_sinPreset[93] declarado em SinCurseHook.cpp:35 nunca e populado. Este campo NAO funciona no runtime atual. Nenhum preset deve depender de ler 0x014B ate que o hook seja reinstalado ou o valor seja espelhado por outro mecanismo. Solucao alternativa: cada monstro recebe 1 preset fixo decidido offline pelo SinScaleInject, sem dispatcher.", "SIN_UNI_INCONSISTENCIAS.md + sin-curse-lab-handoff"),

                Target(0xFFF3, "Self", "Referente ao proprio ator/owner. E o alvo literal mais seguro e byte-provado."),
                Target(0xFFF2, "FrontlineChars", "Todos os personagens na linha de frente, da perspectiva do monstro."),
                Target(0xFFF1, "AllAeons", "Todos os aeons aliados (aliados do monstro)."),
                Target(0xFFEF, "LastAttacker", "Ultimo atacante. Bom para contra-ataque, ainda RT2 por caso."),
                Target(0xFFFB, "AllActors", "Todos os atores dos dois lados. Perigoso; usar com cuidado."),
                Target(0xFFFE, "ActiveActors", "LAB perigoso: teste RT2 reportou que o monstro bateu em si. Nao tratar como inimigo aleatorio nem como todos os personagens."),

                Opcode(0xAE, "PUSHII", "Empilha um inteiro imediato u16/int16. Frequentemente e argumento da chamada seguinte."),
                Opcode(0xB5, "CALL", "Chama funcao que retorna valor na pilha."),
                Opcode(0xD8, "CALLPOPA", "Chama funcao void/native consumindo argumentos da pilha."),
                Opcode(0xB0, "JMP", "Desvio incondicional via jump-table do worker."),
                Opcode(0xD6, "POPXCJMP", "Desvio condicional se o topo da pilha for verdadeiro."),
                Opcode(0xD7, "POPXNCJMP", "Desvio condicional se o topo da pilha for falso."),
                Opcode(0x00, "NOP", "No operation."),
                Opcode(0x01, "LOR", "Logical OR (bool)."),
                Opcode(0x02, "LAND", "Logical AND (bool)."),
                Opcode(0x04, "XOR", "Bitwise XOR."),
                Opcode(0x05, "AND", "Bitwise AND."),
                Opcode(0x06, "EQ", "Compara igualdade."),
                Opcode(0x07, "NE", "Compara desigualdade."),
                ComparisonOpcode(0x08, "GTU", ">"),
                ComparisonOpcode(0x09, "LSU", "<"),
                ComparisonOpcode(0x0A, "GT", ">"),
                ComparisonOpcode(0x0B, "LS", "<"),
                ComparisonOpcode(0x0C, "GTEU", ">="),
                ComparisonOpcode(0x0D, "LSEU", "<="),
                ComparisonOpcode(0x0E, "GTE", ">="),
                ComparisonOpcode(0x0F, "LSE", "<="),
                Opcode(0x10, "BON", "Bit set ON (testa bit)."),
                Opcode(0x11, "BOFF", "Bit set OFF (testa bit negado)."),
                Opcode(0x12, "SLL", "Logical shift left (previously SHL)."),
                Opcode(0x13, "SRL", "Logical shift right (previously SHR)."),
                Opcode(0x14, "ADD", "Adicao inteira."),
                Opcode(0x15, "SUB", "Subtracao inteira."),
                Opcode(0x16, "MUL", "Integer multiplication (previously DIV)."),
                Opcode(0x17, "DIV", "Integer division (previously MUL)."),
                Opcode(0x18, "MOD", "Modulo; used in RNG 1-in-K."),
                Opcode(0x19, "NOT", "Negacao logica."),
                Opcode(0x1A, "NEG", "Negacao aritmetica (UMINUS)."),
                Opcode(0x1B, "FIXADRS", "Corrige endereco interno."),
                Opcode(0x1C, "BNOT", "Bitwise NOT."),
                Opcode(0x25, "POPA", "Pop para register de endereco."),
                Opcode(0x26, "PUSHA", "Push do register de endereco."),
                Opcode(0x28, "PUSHX", "Push do register X."),
                Opcode(0x34, "RTS", "Retorno de subrotina (JSR)."),
                Opcode(0x36, "REQ", "Requests function in another worker."),
                Opcode(0x37, "REQSW", "REQ StartWait — aguarda inicio."),
                Opcode(0x38, "REQEW", "REQ EndWait — aguarda fim."),
                Opcode(0x39, "PREQ", "REQ do Player (para chr especifico)."),
                Opcode(0x3A, "PREQSW", "PREQ StartWait."),
                Opcode(0x3B, "PREQEW", "PREQ EndWait."),
                Opcode(0x3D, "RETN", "Retorno nulo."),
                Opcode(0x3E, "RETT", "Retorno true."),
                Opcode(0x3F, "RETTN", "Retorno nao-nulo true."),
                Opcode(0x40, "HALT", "Para execucao do worker."),
                Opcode(0x45, "FREQ", "Force REQ (forca envio)."),
                Opcode(0x46, "TREQ", "Tagged REQ (evita duplicatas)."),
                Opcode(0x47, "BREQ", "Branching REQ (sends if predicate allows)."),
                Opcode(0x48, "BFREQ", "Branching Force REQ."),
                Opcode(0x49, "BTREQ", "Branching Tagged REQ."),
                Opcode(0x4A, "FREQSW", "FREQ StartWait."),
                Opcode(0x4B, "TREQSW", "TREQ StartWait."),
                Opcode(0x4C, "BREQSW", "BREQ StartWait."),
                Opcode(0x4D, "BFREQSW", "BFREQ StartWait."),
                Opcode(0x4E, "BTREQSW", "BTREQ StartWait."),
                Opcode(0x4F, "FREQEW", "FREQ EndWait."),
                Opcode(0x50, "TREQEW", "TREQ EndWait."),
                Opcode(0x51, "BREQEW", "BREQ EndWait."),
                Opcode(0x52, "BFREQEW", "BFREQ EndWait."),
                Opcode(0x53, "BTREQEW", "BTREQ EndWait."),
                Opcode(0x54, "DRET", "Return do dispatcher."),
                Opcode(0x77, "REQWAIT", "Aguarda requisicao."),
                Opcode(0x78, "PREQWAIT", "PREQ aguarda."),
                Opcode(0x79, "REQCHG", "Muda requisicao."),
                Opcode(0x7A, "ACTREQ", "Ativa requisicao."),
                Opcode(0x9D, "LABEL", "Label de desvio."),
                Opcode(0x9E, "TAG", "Tag de desvio."),
                Opcode(0xA4, "POPARL", "Pop array (longo)."),
                Opcode(0xA7, "PUSHARP", "Push array pointer."),
                Opcode(0xB1, "CJMP", "Jump condicional (true)."),
                Opcode(0xB2, "NCJMP", "Jump condicional (false)."),
                Opcode(0xB3, "JSR", "Jump to subroutine."),
                Opcode(0xC1, "PUSHN", "Push null."),
                Opcode(0xC2, "PUSHT", "Push true."),
                Opcode(0xC3, "PUSHVP", "Push void pointer."),
                Opcode(0xC4, "PUSHFIX", "Push fixador de endereco."),
                Opcode(0xD5, "POPXJMP", "Pop X e jump."),
                Opcode(0xF5, "PUSHAINTER", "Push address inter-worker."),
                Opcode(0xF6, "SYSTEM", "Chamada de sistema ATEL."),
                Opcode(0x59, "POPI0", "Pop from top of stack to temp register 0. Used in SIN dispatchers to preserve preset ID during multiple comparisons. Source: SIN corpus."),
                Opcode(0x67, "PUSHI0", "Push temp register 0 to top of stack. Pair of POPI0; restores saved value. Source: SIN corpus."),
            };

            entries.AddRange(BuildCommandEntries());
            entries.AddRange(BuildAtlasEntries());
            entries.AddRange(BuildAtlasDetailEntries());
            entries.AddRange(BuildCorpusPatternEntries());
            entries.AddRange(BuildFahrenheitReferenceEntries());

            return entries;
        }

        public static IReadOnlyList<AiBibleEntry> Search(string? query, int limit = 24)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0)
                return All.Take(limit).ToList();

            string[] terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return All
                .Select(e => new { Entry = e, Score = Score(e, terms) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Kind)
                .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        static int Score(AiBibleEntry entry, string[] terms)
        {
            int score = 0;
            foreach (string rawTerm in terms)
            {
                string term = rawTerm.Trim();
                if (term.Length == 0) continue;

                int termScore = ScoreTerm(entry, term);
                if (termScore < 0)
                    return -1;
                score += termScore;
            }
            return score;
        }

        static int ScoreTerm(AiBibleEntry entry, string term)
        {
            if (term.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
            {
                string domain = term["domain:".Length..];
                if (domain.Length == 0)
                    return -1;
                if (entry.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    return 150;
                if (entry.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase))
                    return 80;
                return -1;
            }

            string normalized = term.StartsWith("category:", StringComparison.OrdinalIgnoreCase)
                ? term["category:".Length..]
                : term.StartsWith("kind:", StringComparison.OrdinalIgnoreCase)
                    ? term["kind:".Length..]
                    : term;

            if (entry.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return 160;
            if (entry.KindLabel.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return 120;
            if (entry.Domain.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return 115;

            string hex = NormalizeHex(normalized);
            if (hex.Length > 0 && entry.Id.EndsWith(hex, StringComparison.OrdinalIgnoreCase))
                return 140;
            if (hex.Length > 0 && entry.Title.Contains($"0x{hex}", StringComparison.OrdinalIgnoreCase))
                return 130;

            if (entry.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 90;
            if (entry.Tags.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 70;
            if (entry.Domain.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 60;
            if (entry.Summary.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 45;
            if (entry.StackShape.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 35;
            if (entry.Guardrail.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 30;
            if (entry.Evidence.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 25;
            if (entry.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return 10;
            return -1;
        }

        static string NormalizeHex(string term)
        {
            string hex = term.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? term[2..] : term;
            if (hex.Length == 0 || hex.Length > 4)
                return "";
            foreach (char c in hex)
                if (!Uri.IsHexDigit(c))
                    return "";
            return hex.ToUpperInvariant().PadLeft(hex.Length <= 2 ? 2 : 4, '0');
        }

        public static AiBibleEntry? ForInstruction(AiInstruction instruction)
        {
            AiOperandKind kind = AiScript_File.OperandKindOf(instruction.Opcode);
            if (kind == AiOperandKind.FuncId)
                return ById($"func:{instruction.Operand:X4}") ?? Search(AiScript_File.CallName(instruction.Operand), 1).FirstOrDefault();
            if (kind == AiOperandKind.Immediate)
            {
                AiBibleEntry? target = ById($"target:{instruction.Operand:X4}");
                if (target != null) return target;
                AiBibleEntry? command = ById($"command:{instruction.Operand:X4}");
                if (command != null) return command;
                AiBibleEntry? field = ById($"field:{instruction.Operand:X4}");
                if (field != null) return field;
            }
            return ById($"opcode:{instruction.Opcode:X2}");
        }

        public static AiBibleEntry? ById(string id) =>
            All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        static AiBibleEntry Func(ushort id, string fallbackName, string summary, string stack, string guardrail, string evidence)
        {
            string name = AiScript_File.CallName(id);
            if (name.EndsWith('h')) name = fallbackName;
            return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Function,
                Id = $"func:{id:X4}",
                Title = $"{name} (0x{id:X4})",
                Summary = summary,
                StackShape = stack,
                Guardrail = guardrail,
                Evidence = evidence,
                Tags = $"{AiScript_File.CallNamespace(id)} {fallbackName}",
                Domain = "atel",
            };
        }

        static AiBibleEntry Field(ushort id, string fallbackName, string summary, string evidence)
        {
            string name = AiChrPropertyNames.Get(id) ?? fallbackName;
            return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Field,
                Id = $"field:{id:X4}",
                Title = $"{name} (0x{id:X4})",
                Summary = summary,
                StackShape = "Field space: btlActorProperty (readChrProperty/writeChrProperty/getStatField/setStatField).",
                Guardrail = name.StartsWith("?") ? "Name still partial: maintain uncertainty sign." : "",
                Evidence = evidence,
                Tags = fallbackName,
                Domain = "atel",
            };
        }

        static IEnumerable<AiBibleEntry> BuildCommandEntries()
        {
            foreach (AiCommandMetadataEntry command in AiCommandMetadataCatalog.Entries)
                yield return Command(command);
        }

        static AiBibleEntry Command(AiCommandMetadataEntry command)
        {
            SpiraDataAtlasCatalog.TryGetCommandCrosslink(command.OperandHex, out SpiraCommandCrosslink? crosslink);
            string siteSummary = crosslink == null
                ? "Still without crosslink Step0-C loaded in this session."
                : $"Aparece em {crosslink.CommandSiteCount} site(s) ATEL: {crosslink.MonsterSiteCount} monstro(s), {crosslink.BattleSiteCount} batalha(s), {crosslink.EventSiteCount} evento(s). Exemplos: {crosslink.SiteExamples}";
            string summary = BuildCommandSummary(command, siteSummary);
            string guardrail = BuildCommandGuardrail(command, command.IsKnownAiPerformOperandCategory
                ? "Category candidate for performCommand/forcePerformCommand, but SIN template still requires AiScriptLab, AEON diff, backup and RT2."
                : "Metadata-only: item/kernel row was not promoted as Monster AI operand. Use for reading/BIBLE, not as SIN payload.");

            return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Command,
                Id = $"command:{command.Operand:X4}",
                Title = $"{command.DisplayName} ({command.OperandHex})",
                Summary = summary,
                StackShape = $"Uso comum em AI: PUSHII <target> · PUSHII {command.OperandHex} · CALLPOPA 700B/705A. Categoria: {command.Category}; local id 0x{command.LocalId:X3}.",
                Guardrail = guardrail,
                Evidence = $"{command.ProvenanceLabels};{command.AiOperandEvidence};{crosslink?.EvidenceHealth ?? "crosslink-not-loaded"}",
                Tags = $"{command.Category} {command.SourceFile} {command.RoleText} {command.Formula} {command.TargetText} {command.StatusText} {command.ElementText}",
                Domain = "battle-kernel",
                WriterPolicy = guardrail,
                DetailKind = "Command",
            };
        }

        static string BuildCommandSummary(AiCommandMetadataEntry command, string siteSummary)
        {
            string status = string.IsNullOrWhiteSpace(command.StatusText) ? "" : $" Status: {command.StatusText}.";
            string element = string.IsNullOrWhiteSpace(command.ElementText) ? "" : $" Elemento: {command.ElementText}.";
            string formula = string.IsNullOrWhiteSpace(command.Formula) ? "" : $" Formula: {command.Formula} {command.FormulaHex}.";
            string power = command.Power is null ? "" : $" Power: {command.Power}.";
            string target = string.IsNullOrWhiteSpace(command.TargetText) ? "" : $" Alvo: {command.TargetText}.";
            string summary = $"{command.Description} Role: {command.RoleText}.{formula}{power}{target}{status}{element} {siteSummary}".Trim();

            return command.Operand switch
            {
                0x3105 => "Trigger-command Talk. No pacote do Seymour, entra como parte da orquestracao de conversa/cena antes do handoff. " + summary,
                0x6001 => "Special 1. No Seymour, a leitura segura aqui e 'handoff de fase', nao summon/opcode exotico isolado. " + summary,
                0x6051 => "Seymour dismisses Anima!. Beat visual de dismiss da Anima; continua sendo row normal de MonsterMagic2 entregue por performCommand generico. " + summary,
                _ => summary,
            };
        }

        static string BuildCommandGuardrail(AiCommandMetadataEntry command, string fallback) => command.Operand switch
        {
            0x3105 => "Safe reading: trigger-command/conversation context. Do not treat as common combat magic just because it appears in ATEL.",
            0x6001 => "Safe reading: phase handoff in Seymour. Do not promote as new writer without preserving the removeCommand/scene/gate package.",
            0x6051 => "Safe reading: normal row via performCommand, not special ATEL opcode. The transition package matters more than the isolated id.",
            _ => fallback,
        };

        static IEnumerable<AiBibleEntry> BuildAtlasEntries()
        {
            foreach (SpiraDataAtlasEntry atlas in SpiraDataAtlasCatalog.All)
            {
                yield return new AiBibleEntry
                {
                    Kind = atlas.IsBlocked ? AiBibleEntryKind.Guardrail : AiBibleEntryKind.Atlas,
                    Id = atlas.Id,
                    Title = atlas.Title,
                    Summary = $"{atlas.Summary} Rows: {atlas.RowCount}.",
                    StackShape = $"Atlas domain: {atlas.Domain}; kind: {atlas.Kind}; source: {atlas.SourcePath}",
                    Guardrail = atlas.WriterPolicy,
                    Evidence = atlas.Evidence,
                    Tags = atlas.Tags,
                    Domain = atlas.Domain,
                    SourcePath = atlas.SourcePath,
                    WriterPolicy = atlas.WriterPolicy,
                    DetailKind = atlas.Kind.ToString(),
                };
            }
        }

        static IEnumerable<AiBibleEntry> BuildAtlasDetailEntries()
        {
            foreach (SpiraDataAtlasDetailEntry detail in SpiraDataAtlasCatalog.Details)
            {
                yield return new AiBibleEntry
                {
                    Kind = detail.IsBlocked ? AiBibleEntryKind.Guardrail : AiBibleEntryKind.Atlas,
                    Id = detail.Id,
                    Title = detail.Title,
                    Summary = detail.Summary,
                    StackShape = $"{detail.Detail}\nSource: {detail.SourcePath}",
                    Guardrail = detail.WriterPolicy,
                    Evidence = detail.Evidence,
                    Tags = $"{detail.Kind} {detail.Tags}",
                    Domain = detail.Domain,
                    SourcePath = detail.SourcePath,
                    WriterPolicy = detail.WriterPolicy,
                    DetailKind = detail.Kind.ToString(),
                };
            }
        }

        static IEnumerable<AiBibleEntry> BuildCorpusPatternEntries()
        {
            (string Token, string Title, string Summary, string Stack, string Guardrail)[] patterns =
            {
                ("death-revive", "Corpus pattern: death / revive hooks", "Monsters with scripts linked to death, revive, finalization, or drop handler. Good for studying bosses that return, change phase, or clear state when dying.", "Token corpus-wide: death-revive.", "Read-only. Not every monster with this token truly revives; open examples before turning into template."),
                ("write-chr-property", "Corpus pattern: writeChrProperty", "Scripts that directly write btlActorProperty to an explicit actor. It is the family that holds status, buffs, counters, and state adjustments per actor.", "Stack tipica: PUSHII <actor> · PUSHII <field> · PUSHII <value> · CALLPOPA 7018.", "Authoring requires field namespace, target, and proven values. Do not replace with setStatField."),
                ("set-stat-field", "Corpus pattern: setStatField", "Scripts that write field/value to the current stat context without an explicit actorRef. This is the family that prevents incorrect recipes like Shiva becoming a button without context.", "Stack tipica: PUSHII <field> · PUSHII/PUSHF <value> · CALLPOPA 70AB.", "Do not add actorRef. Validate sum-vs-substitution and worker context before SIN."),
                ("turn-round", "Corpus pattern: turn / round logic", "Scripts that read round, turn, or phase to switch behavior. Foundation for rotations, phases, and bosses that change pattern after N turns.", "Procure stat_round, TurnsTaken e branches proximos.", "Token e pista semantica, nao prova de contador unico."),
                ("counter-or-onhit", "Corpus pattern: counter / on-hit", "Scripts com comportamento de resposta: contra-ataque, retaliacao, LastAttacker ou evento parecido.", "Procure LastAttacker, checks de dano/hit e perform/force command guardado.", "Each counter-attack must confirm the target and the actual trigger."),
                ("target-selection", "Corpus pattern: target selection", "Scripts that perform target search/selection before acting. Essential for templates that choose wounded units, aeons, frontline, or last attacker.", "Procure countChrOverlap/search target e sentinels FFFx.", "Wide targets are dangerous; RT2 per case."),
                ("rng", "Corpus pattern: RNG / chance", "Scripts com aleatoriedade para variar rotacao ou chance de acao. Base para templates 1-em-K.", "GetRandomValue · MOD K · compare · branch.", "K=0 and edge cases must be blocked at authoring time."),
                ("hp-read", "Corpus pattern: HP read / phase", "Scripts que leem HP/maxHP ou campos de vida para fase, cura, enrage ou thresholds.", "readChrProperty(self, HP/maxHP) -> compare -> branch/action.", "True HP% requires MUL(0x16) emission, representation in disassembly, and RT2 per case."),
                ("rich-rotation", "Corpus pattern: rich rotation", "Monstros com rotacao rica: muitos comandos, campos, branches ou workers. Bons candidatos para extrair combos SIN mais inteligentes.", "Use as a mining filter: open the examples and compare AEON/AI disassembly.", "Nao quer dizer dificuldade alta; quer dizer script mais denso."),
                ("overdrive", "Corpus pattern: overdrive-like", "Monsters that touch fields/commands linked to Overdrive-like, bar, max/current, or special mechanics. Shiva is just a sample within this family.", "Procure showOverdriveBar, OverdriveMax, OverdriveCurrent e field writes proximos.", "Only becomes a public button once the pattern is confirmed in corpus + disassembly + RT2."),
            };

            IReadOnlyList<SpiraDataAtlasDetailEntry> monsters = SpiraDataAtlasCatalog.Details
                .Where(d => d.Kind == SpiraDataAtlasDetailKind.Monster)
                .ToList();

            foreach ((string token, string title, string summary, string stack, string guardrail) in patterns)
            {
                List<SpiraDataAtlasDetailEntry> matches = monsters
                    .Where(m => m.Tags.Contains(token, StringComparison.OrdinalIgnoreCase)
                                || m.Detail.Contains(token, StringComparison.OrdinalIgnoreCase)
                                || m.Summary.Contains(token, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (matches.Count == 0)
                    continue;

                string examples = string.Join("; ", matches.Take(8).Select(m => m.Title));
                yield return new AiBibleEntry
                {
                    Kind = AiBibleEntryKind.Pattern,
                    Id = $"pattern:corpus:{token}",
                    Title = title,
                    Summary = $"{summary} Corpus: {matches.Count} monstro(s). Exemplos: {examples}.",
                    StackShape = $"{stack}\nCorpus query: domain:monster-corpus {token}",
                    Guardrail = $"{guardrail} Este verbete e read-only; SIN precisa de AiScriptLab, AEON diff, backup e RT2.",
                    Evidence = "parser-corpus;metadata-only;semantic-candidate;Step0-C monster_ai_battle_encounter_crosslink",
                    Tags = $"corpus pattern monster where-appears {token}",
                    Domain = "monster-corpus",
                };
            }
        }

        static AiBibleEntry Target(ushort id, string fallbackName, string summary)
        {
            string name = AiTargetNames.Get(id) ?? fallbackName;
            return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Target,
                Id = $"target:{id:X4}",
                Title = $"{name} (0x{id:X4})",
                Summary = summary,
                StackShape = "Common usage: PUSHII <target> before performCommand/forcePerformCommand.",
                Guardrail = id == 0xFFF3 ? "Self is byte-proven; other targets are corpus-consistent and RT2 per case." : "Corpus-consistent; confirm fine semantics in-game when authoring.",
                Evidence = "AiTargetNames + corpus",
                Tags = fallbackName,
                Domain = "atel",
            };
        }

        static AiBibleEntry ComparisonOpcode(byte opcode, string name, string relation) =>
            Opcode(opcode, name, string.Format(opcode is 0x08 or 0x09 or 0x0C or 0x0D
                ? Strings.U_Ai_UnsignedComparisonOpcodeSummary : Strings.U_Ai_ComparisonOpcodeSummary, relation));

        static AiBibleEntry Opcode(byte opcode, string fallbackName, string summary)
        {
            string name = AiScript_File.Mnemonic(opcode);
            if (name.StartsWith("op_", StringComparison.OrdinalIgnoreCase)) name = fallbackName;
            return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Opcode,
                Id = $"opcode:{opcode:X2}",
                Title = $"{name} (0x{opcode:X2})",
                Summary = summary,
                StackShape = AiScript_File.OpcodeHelp(opcode),
                Evidence = "AiScript_File opcode table + corpus RT0",
                Tags = fallbackName,
                Domain = "atel",
            };
        }

        static AiBibleEntry Pattern(string id, string title, string summary, string stack, string guardrail, string evidence) => new()
        {
            Kind = AiBibleEntryKind.Pattern,
            Id = id,
            Title = title,
            Summary = summary,
            StackShape = stack,
            Guardrail = guardrail,
            Evidence = evidence,
            Domain = "monster-ai",
        };

        static AiBibleEntry Guard(string id, string title, string summary, string stack, string guardrail, string evidence) => new()
        {
            Kind = AiBibleEntryKind.Guardrail,
            Id = id,
            Title = title,
            Summary = summary,
            StackShape = stack,
            Guardrail = guardrail,
            Evidence = evidence,
            Domain = "guardrail",
        };

        static IEnumerable<AiBibleEntry> BuildFahrenheitReferenceEntries()
        {
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-common",
                Title = "ATEL Namespace: Common (0x0, 'std')",
                Summary = "616 funcoes ATEL de uso geral. IDs 0x0000-0x0267.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x0 << 12) | index. Nomes validados via Ghidra (Fahrenheit RE).",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE) + corpus monster AI",
                Tags = "fahrenheit atel namespace common",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-math",
                Title = "ATEL Namespace: Math (0x1, 'math')",
                Summary = "30 funcoes matematicas ATEL. IDs 0x1000-0x101D.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x1 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace math",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-sgevent",
                Title = "ATEL Namespace: SphereGrid Event (0x4, 'sg')",
                Summary = "71 funcoes de evento do Sphere Grid. IDs 0x4000-0x4046.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x4 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace sgevent spheregrid",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-chevent",
                Title = "ATEL Namespace: Character Event (0x5, 'ch')",
                Summary = "145 funcoes de evento de personagem. IDs 0x5000-0x5090.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x5 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace chevent character",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-camera",
                Title = "ATEL Namespace: Camera (0x6, 'cam')",
                Summary = "138 funcoes de camera ATEL. IDs 0x6000-0x6089. Inclui camSetPolar, camMove, camWait, refSetPos.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x6 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE) + corpus 147 call targets nomeados no editor",
                Tags = "fahrenheit atel namespace camera",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-battle",
                Title = "ATEL Namespace: Battle (0x7, 'btl')",
                Summary = "296 ATEL battle functions. IDs 0x7000-0x7127. The most relevant namespace for monster AI.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x7 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE) + corpus monster AI",
                Tags = "fahrenheit atel namespace battle btl",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-map",
                Title = "ATEL Namespace: Map (0x8, 'map')",
                Summary = "108 funcoes de mapa ATEL. IDs 0x8000-0x806B.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x8 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace map",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-mount",
                Title = "ATEL Namespace: Mount (0x9, 'mnt')",
                Summary = "1 funcao de montaria ATEL. ID 0x9000.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0x9 << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace mount",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-movie",
                Title = "ATEL Namespace: Movie (0xB, 'mov')",
                Summary = "16 funcoes de cutscene/video ATEL. IDs 0xB000-0xB00F.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0xB << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace movie",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-debug",
                Title = "ATEL Namespace: Debug (0xC, 'dbg')",
                Summary = "94 funcoes de debug ATEL. IDs 0xC000-0xC05D. PS2 dev leftovers.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0xC << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace debug",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:namespace-abimap",
                Title = "ATEL Namespace: AbilityMap (0xD, 'abm')",
                Summary = "1 ATEL skill mapping function. ID 0xD000.",
                StackShape = "ATEL call: CALL/CALLPOPA com id = (0xD << 12) | index.",
                Evidence = "Fahrenheit atel/call.cs (Ghidra RE)",
                Tags = "fahrenheit atel namespace abimap ability",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "atel:vartype-system",
                Title = "ATEL ScriptVar: tipos (U8/I8/U16/I16/U32/I32/F32) e localizacoes",
                Summary = "ATEL script variables (8 bytes): 3 bytes value + 1 byte properties (high nibble=type, low nibble=location) + 4 bytes element_count. Types: U8=0, I8=1, U16=2, I16=3, U32=4, I32=5, F32=6. Locations: SaveData, CommonVars, Data, Private, Shared, IntRegisters, EventData.",
                StackShape = "Layout: val[3] + properties (type<<4|location) + element_count(u32). Used in the .ebp chunk 0 format (ATEL event script).",
                Evidence = "Fahrenheit atel/script_header.cs (Ghidra RE). Referencia: docs/ai/FAHRENHEIT_ECOSYSTEM_REFERENCE.md",
                Tags = "fahrenheit atel scriptvar type location ebp",
                Domain = "atel",
            };
            yield return new AiBibleEntry
            {
                Kind = AiBibleEntryKind.Pattern,
                Id = "aeon:stat-scaling-formula",
                Title = "Aeon stat scaling: formula Yuna->Aeon",
                Summary = "Base Aeon stats calculated from Yuna's stats + coefficients (ply_rom.bin, PlayerGrowthData). Formula: Total = hp/100 + mp/10 + str + def + mag + mdf + agi + eva + acc; HP = yuna_total * hp_total + yuna_hp * hp_individual / 100; MP = yuna_total * mp_total / 10 + yuna_mp * mp_individual / 100; Stat = yuna_total / stat_total + yuna_stat * stat_individual / 10.",
                StackShape = "Coeficientes decodificados em PlayerGrowthData (ply_rom.bin, 20 entries). Coeficientes HpA/HpB..AccA/AccB por aeon.",
                Evidence = "Fahrenheit summon.cs (Ghidra RE). Coeficientes RT0-proved via PlayerGrowthData.",
                Tags = "fahrenheit aeon summon scaling yuna growth",
                Domain = "battle-kernel",
            };
        }
    }
}

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// Shared "what is the user looking at" context (Jarvis-UI, 2026-09-15). Modules report
    /// their active file/record on selection change; the AI Agent injects this into its
    /// system prompt so commands like "buff this monster's HP" resolve to the open record.
    ///
    /// Design: a tiny static hub (modules already run on the UI thread; no events needed —
    /// the agent snapshots <see cref="Current"/> when a command is sent). Reporting is
    /// additive: modules that never call Report simply contribute module name only.
    /// </summary>
    public sealed record EditorContext
    {
        public required string ModuleId { get; init; }
        public string? FilePath { get; init; }
        public string? RecordLabel { get; init; }
        public string? RecordSummary { get; init; }
    }

    public static class EditorContextHub
    {
        public static EditorContext Current { get; private set; } = new() { ModuleId = "home" };

        /// <summary>Módulo ativo mudou (sempre reportar — limpa o record do módulo anterior).</summary>
        public static void ReportModule(string moduleId) =>
            Current = new EditorContext { ModuleId = moduleId };

        /// <summary>Seleção dentro do módulo ativo (arquivo + rótulo/resumo do registro).</summary>
        public static void ReportSelection(string moduleId, string? filePath, string? recordLabel, string? recordSummary = null) =>
            Current = new EditorContext
            {
                ModuleId = moduleId,
                FilePath = filePath,
                RecordLabel = recordLabel,
                RecordSummary = recordSummary,
            };
    }
}

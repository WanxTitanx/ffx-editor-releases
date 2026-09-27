namespace FFXProjectEditor.FfxLib.Dictionaries
{
    /// <summary>
    /// Best-effort magic_#### (HD overlay folder id, 0..1023 kernel namespace) -> spell/ability NAME.
    ///
    /// ⚠️ OWNER-AUTHORIZED HYPOTHESIS JOIN. The magic-id -> kernel-command-name bridge is NOT byte-proven
    /// (the catalog crosswalk says "no names proved"). The owner explicitly overrode the no-fabricate rule
    /// (2026-06-07: "agora é pra fazer caralho") to surface a usable name anyway. The 12-bit folder id is
    /// looked up in the kernel command/ability dictionaries in priority order; the matched SOURCE is returned
    /// alongside so the UI can label confidence and the owner can judge plausibility on screen (the project's
    /// verify-on-screen rule). Names from low ids will skew toward player commands (the dense 0..~90 range).
    /// Pure lookup, no I/O.
    /// </summary>
    public static class MagicSpellNameResolver
    {
        /// <summary>Returns (name, source) — name is null when no dictionary holds the id.</summary>
        public static (string? Name, string Source) Resolve(int magicId)
        {
            ushort id = (ushort)(magicId & 0x0FFF);
            if (CommandCharacter_Dictionary.Instance.TryGetValue(id, out string? c)) return (c, "char-cmd");
            if (CommandMonster1_Dictionary.Instance.TryGetValue(id, out string? m1)) return (m1, "mon-cmd1");
            if (CommandMonster2_Dictionary.Instance.TryGetValue(id, out string? m2)) return (m2, "mon-cmd2");
            if (Item_Dictionary.Instance.TryGetValue(id, out string? it)) return (it, "item");
            return (null, "");
        }

        /// <summary>A display string for a magic id: "Firaga ~char-cmd" (best-effort) or "magic_0148" when unmapped.
        /// The leading '~' marks the name as a non-byte-proven hypothesis join.</summary>
        public static string DisplayName(int magicId)
        {
            (string? name, string source) = Resolve(magicId);
            return name == null ? $"magic_{magicId:D4}" : $"{name}  ~{source}";
        }
    }
}

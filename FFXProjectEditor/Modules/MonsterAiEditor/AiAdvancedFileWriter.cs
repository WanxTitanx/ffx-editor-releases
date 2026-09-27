using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

/// <summary>AI authoring saves require a fresh baseline and a successful backup.</summary>
internal static class AiAdvancedFileWriter
{
    public static bool HasPendingEdits(AiScriptFile script) =>
        !AiScript_File.Write(script).SequenceEqual(script.OriginalAiFileBytes);

    public static void Save(string path, byte[] expectedAi, byte[] editedAi)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? currentAi = AiScript_File.SliceAiFileFromMonster(monster);
        if (currentAi == null || !currentAi.SequenceEqual(expectedAi))
            throw new InvalidOperationException(Strings.AiAdvancedFileChanged);

        byte[] output = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, editedAi);
        byte[]? readback = AiScript_File.SliceAiFileFromMonster(output);
        // The monster container aligns the next partition to 16 bytes. Verify
        // the complete payload and exactly that zero padding before promotion.
        int paddedLength = checked(editedAi.Length + 15) & ~15;
        if (readback == null || readback.Length != paddedLength
            || !readback.AsSpan(0, editedAi.Length).SequenceEqual(editedAi)
            || readback.AsSpan(editedAi.Length).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException(Strings.AiAdvancedRebuildMismatch);
        // Applying unchanged fields must not consume the previous real edit's undo.
        if (output.SequenceEqual(monster) && File.Exists(path + ".prev.bak")) return;
        Promote(path, monster, output, backup: true);
    }

    public static void RestoreBackup(string path, byte[] expectedAi)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? currentAi = AiScript_File.SliceAiFileFromMonster(monster);
        if (currentAi == null || !currentAi.SequenceEqual(expectedAi))
            throw new InvalidOperationException(Strings.AiAdvancedFileChanged);
        byte[] backup = File.ReadAllBytes(path + ".prev.bak");
        byte[] ai = AiScript_File.SliceAiFileFromMonster(backup)
            ?? throw new InvalidDataException(Strings.U_Ai_AslNoAiPartition);
        AiScriptFile script = AiScript_File.Read(ai);
        if (script.HasScript && !script.CodeWalkClosedExactly)
            throw new InvalidDataException(Strings.AiAdvancedRebuildMismatch);
        // Retain the undo source. A malformed backup must be rejected before
        // opening the destination for replacement.
        if (!backup.SequenceEqual(monster)) Promote(path, monster, backup, backup: false);
    }

    static void Promote(string path, byte[] expectedMonster, byte[] output, bool backup)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, output);
            // Recheck after preparation; preserve changes in other monster chunks.
            if (!File.ReadAllBytes(path).SequenceEqual(expectedMonster))
                throw new InvalidOperationException(Strings.AiAdvancedFileChanged);
            if (backup) File.Copy(path, path + ".prev.bak", overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

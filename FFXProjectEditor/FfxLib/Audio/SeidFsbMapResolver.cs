using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>
    /// Offline map: battle seId → FSB subsong via 9999_common.txt, matching EXE
    /// <c>FFX_FmodSfx_ResolveSequence@0x70FB60</c> → <c>sub_710370</c> (char-truncated key)
    /// with <c>sub_710BC0</c> full-int fallback for keys outside 0..121 low-byte range.
    /// </summary>
    public static class SeidFsbMapResolver
    {
        public const uint SeIdEventKeyBase = 9000;
        public const int MaxCommonKey = 121;

        public sealed record ResolveResult(int FsbSampleIndex, string Evidence, string Confidence);

        public static ResolveResult? TryResolve(
            int magicId,
            uint seId,
            ushort? waveDataId,
            IReadOnlyDictionary<uint, int> keyToFsb)
        {
            if (keyToFsb.Count == 0)
                return null;

            uint lowByte = seId & 0xFF;
            if (keyToFsb.TryGetValue(lowByte, out int byLowByte))
                return new ResolveResult(
                    byLowByte,
                    $"common_key=seId_low_byte:{lowByte}",
                    "high");

            if (seId >= SeIdEventKeyBase)
            {
                uint eventKey = seId - SeIdEventKeyBase;
                if (eventKey <= MaxCommonKey && keyToFsb.TryGetValue(eventKey, out int byEventKey))
                    return new ResolveResult(
                        byEventKey,
                        $"common_key=seId-{SeIdEventKeyBase}:{eventKey}",
                        "high");
            }

            if (keyToFsb.TryGetValue(seId, out int byFullSeId))
                return new ResolveResult(
                    byFullSeId,
                    $"common_key=seId_full:{seId}",
                    "medium");

            return null;
        }
    }
}

using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.Ps3
{
    internal sealed record Ps3MagicColorTransform(
        double RedScale,
        double GreenScale,
        double BlueScale,
        double AlphaScale)
    {
        public bool IsIdentity =>
            Nearly(RedScale, 1.0)
            && Nearly(GreenScale, 1.0)
            && Nearly(BlueScale, 1.0)
            && Nearly(AlphaScale, 1.0);

        static bool Nearly(double a, double b) => Math.Abs(a - b) < 0.000001;

        public static Ps3MagicColorTransform Identity => new(1.0, 1.0, 1.0, 1.0);

        /// <summary>RT2 smoke-test: crush green, boost red+blue — orange fire becomes hot magenta.</summary>
        public static Ps3MagicColorTransform DrasticMagenta => new(3.0, 0.05, 3.0, 1.0);

        /// <summary>Prism Flare violet tint: boost red+blue, tame green (safe PS3 texture path).</summary>
        public static Ps3MagicColorTransform PrismViolet => new(1.25, 0.65, 1.55, 1.0);

        /// <summary>Stronger violet for fire atlases — crushes orange DXT blocks harder.</summary>
        public static Ps3MagicColorTransform PrismVioletStrong => new(1.85, 0.22, 2.35, 1.0);

        /// <summary>ThundaFira phase-1 (716): cool cyan bolts converging — RT2-approved look.</summary>
        public static Ps3MagicColorTransform ThundaFiraBlueThunder => new(0.55, 1.05, 2.5, 1.0);

        /// <summary>ThundaFira phase-2 (717): cyan-white burst (clone of 0082, blue-shifted).</summary>
        public static Ps3MagicColorTransform ThundaFiraBlueBurst => new(0.7, 1.0, 2.35, 1.0);

        /// <summary>Alias kept for pack tool — same as blue thunder phase.</summary>
        public static Ps3MagicColorTransform ThundaFiraThunder => ThundaFiraBlueThunder;

        /// <summary>ThundaFira phase-2 (717): keep Firaga donor colors (clone of `0090`).</summary>
        public static Ps3MagicColorTransform ThundaFiraFiragaBurst => Identity;

        /// <summary>Alias kept for pack tool — Firaga burst is native orange (no cyan shift).</summary>
        public static Ps3MagicColorTransform ThundaFiraFire => ThundaFiraFiragaBurst;

        /// <summary>ThundaFira bolt sheets (`128_*`) — phyre leg (DLL still required for halo).</summary>
        public static Ps3MagicColorTransform ThundaFiraOrangeBolt => new(2.6, 0.65, 0.12, 1.0);

        public static Ps3MagicColorTransform ThundaFiraOrangeBoltStrong => new(3.0, 0.55, 0.08, 1.0);

        /// <summary>Explosion burst sheets inside Thundaga — RT2 probe palette.</summary>
        public static Ps3MagicColorTransform ThundaFiraGreenBurst => new(0.28, 3.4, 0.22, 1.0);

        public static Ps3MagicColorTransform ThundaFiraGreenBurstStrong => new(0.18, 4.2, 0.12, 1.0);

        /// <summary>ThundaFira red-safe phase-1 (716): warm crimson bolts — crush blue channel hard.</summary>
        public static Ps3MagicColorTransform ThundaFiraRedSafeThunder => new(3.0, 0.42, 0.12, 1.0);

        /// <summary>716 large atlases — all bolt sheets get strong red (paired with ≤4 DLL blue vec4).</summary>
        public static Ps3MagicColorTransform ThundaFiraRedSafeThunderStrong => new(3.5, 0.35, 0.08, 1.0);

        /// <summary>ThundaFira red-safe phase-2 (717): warm impact, simpler than lava-aggressive.</summary>
        public static Ps3MagicColorTransform ThundaFiraRedSafeBurst => new(1.65, 0.58, 0.44, 1.0);

        /// <summary>717 large atlases — mild warm boost.</summary>
        public static Ps3MagicColorTransform ThundaFiraRedSafeBurstStrong => new(1.8, 0.52, 0.38, 1.0);

        /// <summary>AGGRESSIVE — RT2 broke VFX (2D lance). Use only with --lava-aggressive --dll-tint.</summary>
        public static Ps3MagicColorTransform ThundaFiraLavaThunder => new(4.2, 0.2, 0.02, 1.0);

        /// <summary>All 716 atlases — max red on bolt sheets.</summary>
        public static Ps3MagicColorTransform ThundaFiraLavaThunderStrong => new(4.8, 0.12, 0.01, 1.0);

        /// <summary>ThundaFira lava phase-2 (717): softer ground/explosion impact.</summary>
        public static Ps3MagicColorTransform ThundaFiraLavaBurst => new(1.25, 0.72, 0.38, 1.0);

        /// <summary>Large burst atlases — mild warm red, not nuclear.</summary>
        public static Ps3MagicColorTransform ThundaFiraLavaBurstStrong => new(1.45, 0.65, 0.32, 1.0);

        /// <summary>Legacy single transform — prefer phase-specific lava presets.</summary>
        public static Ps3MagicColorTransform ThundaFiraLava => ThundaFiraLavaThunder;

        public static Ps3MagicColorTransform ThundaFiraLavaStrong => ThundaFiraLavaThunderStrong;

        /// <summary>FlameFlan LAB — cast (`718`): coral/lava claro (menos vermelho vivo).</summary>
        public static Ps3MagicColorTransform FlameFlanMagmaCast => new(3.4, 0.48, 0.10, 1.0);

        public static Ps3MagicColorTransform FlameFlanMagmaCastStrong => new(3.85, 0.40, 0.08, 1.0);

        /// <summary>FlameFlan LAB — burst (`719`): impacto quente salmão.</summary>
        public static Ps3MagicColorTransform FlameFlanMagmaBurst => new(3.6, 0.42, 0.09, 1.0);

        public static Ps3MagicColorTransform FlameFlanMagmaBurstStrong => new(4.0, 0.36, 0.07, 1.0);

        /// <summary>Cast opener (`128_64`, `128_128`) — phyre only; não usar DLL tint (RT2 timing fail).</summary>
        public static Ps3MagicColorTransform FlameFlanMagmaSpark => new(4.1, 0.35, 0.08, 1.0);
    }

    internal sealed record Ps3MagicRecolorResult(
        string SourcePath,
        string OutputPath,
        Ps3PhyreMip0Layout Layout,
        int RecoloredBlocksOrPixels,
        Ps3MagicColorTransform Transform,
        Ps3PhyreWriteResult WriteResult);

    internal static class Ps3MagicTextureColorWriter
    {
        public static Ps3MagicRecolorResult WriteRecoloredMip0(string sourcePath, string outputPath, Ps3MagicColorTransform transform)
        {
            if (!Ps3MagicTextureWriter.TryReadMip0Layout(sourcePath, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                throw new InvalidDataException(note);

            byte[] mip0 = Ps3MagicTextureWriter.ReadMip0Payload(sourcePath);
            int changed = RecolorMip0InPlace(mip0, layout, transform);
            Ps3PhyreWriteResult write = Ps3MagicTextureWriter.WriteSameShapeMip0(sourcePath, mip0, outputPath);

            return new Ps3MagicRecolorResult(sourcePath, outputPath, layout, changed, transform, write);
        }

        public static int RecolorMip0InPlace(byte[] mip0, Ps3PhyreMip0Layout layout, Ps3MagicColorTransform transform)
        {
            if (transform.IsIdentity)
                return 0;

            return layout.Format switch
            {
                "ARGB8" => RecolorArgb8(mip0, transform),
                "L8" => RecolorL8(mip0, transform),
                "DXT1" => RecolorDxtColorBlocks(mip0, 8, 0, layout.Width, layout.Height, transform),
                "DXT3" or "DXT5" => RecolorDxtColorBlocks(mip0, 16, 8, layout.Width, layout.Height, transform),
                _ => throw new InvalidDataException($"Unsupported recolor format '{layout.Format}'.")
            };
        }

        static int RecolorArgb8(byte[] mip0, Ps3MagicColorTransform transform)
        {
            int pixels = mip0.Length / 4;
            for (int i = 0; i < pixels; i++)
            {
                int p = i * 4;
                // The renderer treats the raw bytes as BGRA8888.
                mip0[p + 0] = ScaleByte(mip0[p + 0], transform.BlueScale);
                mip0[p + 1] = ScaleByte(mip0[p + 1], transform.GreenScale);
                mip0[p + 2] = ScaleByte(mip0[p + 2], transform.RedScale);
                mip0[p + 3] = ScaleByte(mip0[p + 3], transform.AlphaScale);
            }
            return pixels;
        }

        static int RecolorL8(byte[] mip0, Ps3MagicColorTransform transform)
        {
            double luminanceScale = (transform.RedScale + transform.GreenScale + transform.BlueScale) / 3.0;
            for (int i = 0; i < mip0.Length; i++)
                mip0[i] = ScaleByte(mip0[i], luminanceScale);
            return mip0.Length;
        }

        static int RecolorDxtColorBlocks(byte[] mip0, int blockBytes, int colorOffset, int width, int height, Ps3MagicColorTransform transform)
        {
            int bx = (width + 3) / 4;
            int by = (height + 3) / 4;
            int blocks = 0;
            for (int block = 0; block < bx * by; block++)
            {
                int p = block * blockBytes + colorOffset;
                if (p + 4 > mip0.Length)
                    break;

                ushort c0 = (ushort)(mip0[p] | (mip0[p + 1] << 8));
                ushort c1 = (ushort)(mip0[p + 2] | (mip0[p + 3] << 8));
                c0 = RecolorRgb565(c0, transform);
                c1 = RecolorRgb565(c1, transform);
                mip0[p] = (byte)(c0 & 0xFF);
                mip0[p + 1] = (byte)(c0 >> 8);
                mip0[p + 2] = (byte)(c1 & 0xFF);
                mip0[p + 3] = (byte)(c1 >> 8);
                blocks++;
            }
            return blocks;
        }

        static ushort RecolorRgb565(ushort value, Ps3MagicColorTransform transform)
        {
            int r = ((value >> 11) & 31) * 255 / 31;
            int g = ((value >> 5) & 63) * 255 / 63;
            int b = (value & 31) * 255 / 31;
            r = ScaleInt(r, transform.RedScale);
            g = ScaleInt(g, transform.GreenScale);
            b = ScaleInt(b, transform.BlueScale);
            int r5 = r * 31 / 255;
            int g6 = g * 63 / 255;
            int b5 = b * 31 / 255;
            return (ushort)((r5 << 11) | (g6 << 5) | b5);
        }

        static byte ScaleByte(byte value, double scale) => (byte)ScaleInt(value, scale);

        static int ScaleInt(int value, double scale)
        {
            int scaled = (int)Math.Round(value * scale);
            return Math.Clamp(scaled, 0, 255);
        }
    }
}

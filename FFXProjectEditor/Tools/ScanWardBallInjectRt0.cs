using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Scan-UI art track (DLL-independent): author a Holy (radiant gold) and a Dark (violet)
    /// element "ball" into free space of the battle widget atlas (atlas id 16128 =
    /// menu_us/D3D11/battle.dds.phyre, 1024x1024 DXT5) by lossless block-copying the existing
    /// white (Blizzard) ball and recoloring only its DXT color endpoints. Alpha (the round
    /// silhouette) is copied verbatim, so the new balls keep the exact sphere shape.
    ///
    /// Output: a modded .dds.phyre (same Phyre layout) plus the exact game-space UVs the Scan-UI
    /// hook must use to draw the two new balls. No game files are touched; deploy happens with the
    /// hook later. See docs/reverse/FFX_SCAN_WEAKNESS_UI_RENDER_LOOP_2026-06-16.md.
    /// </summary>
    internal static class ScanWardBallInjectRt0
    {
        const string DefaultSource = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\menu_us\d3d11\battle.dds.phyre";
        const int BlockBytes = 16;
        const int ColorOffset = 8; // DXT5: 8 alpha bytes, then color block

        // White (Blizzard) ball -> recolor targets. The source silver ball is only ~mid-gray (~123),
        // so we BOOST (scale > 1) to push highlights toward 255 (radiant core) while the hue ratio
        // shapes the color; the spherical shading gradient is preserved multiplicatively.
        static readonly (double r, double g, double b) HolyGold = (2.00, 1.62, 0.62);   // radiant white-gold
        static readonly (double r, double g, double b) DarkViolet = (1.15, 0.40, 1.75); // vivid violet

        public static int Run(string[] args)
        {
            try
            {
                string sourcePath = args.Length > 1 ? args[1] : DefaultSource;
                if (!File.Exists(sourcePath))
                {
                    Console.WriteLine($"FAIL: source atlas not found: {sourcePath}");
                    Console.WriteLine("Usage: --scanward-ball-inject-rt0 [battle.dds.phyre] [output.dds.phyre]");
                    return 1;
                }

                if (!Ps3MagicTextureWriter.TryReadMip0Layout(sourcePath, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                {
                    Console.WriteLine($"FAIL: layout unreadable: {note}");
                    return 1;
                }
                if (layout.Format != "DXT5" || layout.Width != 1024 || layout.Height != 1024)
                {
                    Console.WriteLine($"FAIL: expected DXT5 1024x1024, got {layout.Summary}");
                    return 1;
                }

                int blocksW = layout.Width / 4;   // 256
                int blocksH = layout.Height / 4;  // 256

                byte[] mip0 = Ps3MagicTextureWriter.ReadMip0Payload(sourcePath);
                byte[] original = (byte[])mip0.Clone();

                // ---- 1. detect the 4 strip balls + isolated mask ball ----
                List<Blob> blobs = DetectBalls(mip0, blocksW);
                Console.WriteLine($"detected {blobs.Count} ball blob(s) in the top strip band:");
                foreach (Blob b in blobs.OrderBy(b => b.Cx))
                {
                    var (r, g, bl) = BlockColor(mip0, blocksW, b.Cx, b.Cy);
                    Console.WriteLine($"  blob px=({b.MinBX * 4}..{b.MaxBX * 4},{b.MinBY * 4}..{b.MaxBY * 4}) center=({b.Cx * 4},{b.Cy * 4}) rgb=({r},{g},{bl}) minCh={Math.Min(r, Math.Min(g, bl))}");
                }

                Blob? white = PickWhiteBall(mip0, blocksW, blobs);
                if (white == null)
                {
                    Console.WriteLine("FAIL: could not identify the white (Blizzard) ball. Aborting (no fallback applied).");
                    return 1;
                }
                var (wr, wg, wb) = BlockColor(mip0, blocksW, white.Cx, white.Cy);
                Console.WriteLine($"source = white ball: px x={white.MinBX * 4}..{white.MaxBX * 4} y={white.MinBY * 4}..{white.MaxBY * 4} rgb=({wr},{wg},{wb})");

                // copy tile = white bbox padded by 1 block (captures full alpha silhouette + clean edge)
                int tbx0 = Math.Max(0, white.MinBX - 1);
                int tby0 = Math.Max(0, white.MinBY - 1);
                int tbx1 = Math.Min(blocksW - 1, white.MaxBX + 1);
                int tby1 = Math.Min(blocksH - 1, white.MaxBY + 1);
                int tileW = tbx1 - tbx0 + 1;
                int tileH = tby1 - tby0 + 1;
                Console.WriteLine($"copy tile = {tileW}x{tileH} blocks ({tileW * 4}x{tileH * 4}px) at block ({tbx0},{tby0})");

                // ---- 2. find 2 transparent destination tiles (with a 1-block transparent border) ----
                var reserved = new List<(int bx0, int by0, int bx1, int by1)>();
                foreach (Blob b in blobs) reserved.Add((b.MinBX - 1, b.MinBY - 1, b.MaxBX + 1, b.MaxBY + 1));
                reserved.Add((tbx0, tby0, tbx1, tby1));

                List<(int bx, int by)> dst = FindTransparentTiles(mip0, blocksW, blocksH, tileW, tileH, reserved, 2);
                if (dst.Count < 2)
                {
                    int tcount = 0;
                    for (int by = 0; by < blocksH; by++)
                        for (int bx = 0; bx < blocksW; bx++)
                            if (Transparent(mip0, blocksW, bx, by)) tcount++;
                    Console.WriteLine($"FAIL: needed 2 free transparent {tileW}x{tileH} tiles, found {dst.Count}. " +
                        $"(transparent blocks total={tcount}/{blocksW * blocksH})");
                    return 1;
                }

                // ---- 3. copy + recolor: dst[0]=Holy(gold), dst[1]=Dark(violet) ----
                CopyBlockRect(mip0, blocksW, tbx0, tby0, dst[0].bx, dst[0].by, tileW, tileH);
                CopyBlockRect(mip0, blocksW, tbx0, tby0, dst[1].bx, dst[1].by, tileW, tileH);
                RecolorBlockRectEndpoints(mip0, blocksW, dst[0].bx, dst[0].by, tileW, tileH, HolyGold);
                RecolorBlockRectEndpoints(mip0, blocksW, dst[1].bx, dst[1].by, tileW, tileH, DarkViolet);

                // ball offset within the copy tile (so the hook UV targets the ball, not the padded tile)
                int ballOffBX = white.MinBX - tbx0;
                int ballOffBY = white.MinBY - tby0;
                int ballW = white.MaxBX - white.MinBX + 1;
                int ballH = white.MaxBY - white.MinBY + 1;

                BallPlacement holy = PlacementFor(dst[0].bx + ballOffBX, dst[0].by + ballOffBY, ballW, ballH);
                BallPlacement dark = PlacementFor(dst[1].bx + ballOffBX, dst[1].by + ballOffBY, ballW, ballH);

                // ---- 4. write modded atlas (same Phyre shape) ----
                string outDir = Path.Combine("work", "scanui");
                Directory.CreateDirectory(outDir);
                string outPath = args.Length > 2 ? args[2] : Path.Combine(outDir, "battle_us_wards.dds.phyre");
                Ps3PhyreWriteResult write = Ps3MagicTextureWriter.WriteSameShapeMip0(sourcePath, mip0, outPath);

                // ---- 5. self-check: only the 2 dst tiles changed in mip0 ----
                bool confined = DiffConfinedToTiles(original, mip0, blocksW,
                    new[] { (dst[0].bx, dst[0].by, tileW, tileH), (dst[1].bx, dst[1].by, tileW, tileH) },
                    out int changedBlocks, out int strayBlocks);

                Console.WriteLine();
                Console.WriteLine(write.SameLayout && confined && strayBlocks == 0
                    ? "PASS: Holy + Dark balls authored; Phyre layout preserved; edits confined to the 2 new tiles."
                    : "FAIL: layout drifted or edits leaked outside the 2 new tiles.");
                Console.WriteLine($"source : {sourcePath}");
                Console.WriteLine($"output : {outPath}");
                Console.WriteLine($"layout : {write.OutputLayout.Summary}");
                Console.WriteLine($"changed blocks={changedBlocks} (stray outside tiles={strayBlocks})");
                Console.WriteLine();
                Console.WriteLine("=== HOOK ART COORDS (atlas 16128, game-UV; drop into FFX_Menu2D_DrawTexQuadSolid) ===");
                Console.WriteLine($"  Holy(0x10): tile@block({dst[0].bx},{dst[0].by})  ball px x={holy.X0}..{holy.X1} y={holy.Y0}..{holy.Y1}");
                Console.WriteLine($"             UV = ({holy.U0:F4}, {holy.V0:F4}, {holy.U1:F4}, {holy.V1:F4})");
                Console.WriteLine($"  Dark(0x80): tile@block({dst[1].bx},{dst[1].by})  ball px x={dark.X0}..{dark.X1} y={dark.Y0}..{dark.Y1}");
                Console.WriteLine($"             UV = ({dark.U0:F4}, {dark.V0:F4}, {dark.U1:F4}, {dark.V1:F4})");
                Console.WriteLine("  (existing strip slots are at rowX +125/+188/+251/+316; new balls go at rowX +379 (Holy) / +442 (Dark), 63px spacing.)");
                return write.SameLayout && confined && strayBlocks == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        sealed class Blob
        {
            public int MinBX, MinBY, MaxBX, MaxBY, Area;
            public int Cx => (MinBX + MaxBX) / 2;
            public int Cy => (MinBY + MaxBY) / 2;
        }

        readonly record struct BallPlacement(int X0, int Y0, int X1, int Y1, double U0, double V0, double U1, double V1);

        // game-UV: u not flipped (u=x/1024); v flipped (rasterY=(1-v)*1024) -> matches the vanilla
        // strip UV (0.225,0.949,0.466,0.988) at rasterY 12..52. v0 corresponds to the larger rasterY.
        static BallPlacement PlacementFor(int bx, int by, int wBlocks, int hBlocks)
        {
            int x0 = bx * 4, y0 = by * 4;
            int x1 = (bx + wBlocks) * 4, y1 = (by + hBlocks) * 4;
            double u0 = x0 / 1024.0, u1 = x1 / 1024.0;
            double v0 = 1.0 - y1 / 1024.0; // larger rasterY -> v0 (mirror of strip)
            double v1 = 1.0 - y0 / 1024.0;
            return new BallPlacement(x0, y0, x1, y1, u0, v0, u1, v1);
        }

        static bool Ink(byte[] mip0, int blocksW, int bx, int by)
        {
            int blk = (by * blocksW + bx) * BlockBytes;
            return Math.Max(mip0[blk], mip0[blk + 1]) >= 64;
        }

        // True only if every one of the block's 16 pixels decodes to alpha 0 (handles both DXT5
        // alpha modes; an empty atlas cell may be a0=0,a1=0 OR a0=0,a1=255 with all indices->a0).
        static bool Transparent(byte[] mip0, int blocksW, int bx, int by)
        {
            int blk = (by * blocksW + bx) * BlockBytes;
            int a0 = mip0[blk], a1 = mip0[blk + 1];
            Span<int> a = stackalloc int[8];
            a[0] = a0; a[1] = a1;
            if (a0 > a1) { for (int i = 1; i <= 6; i++) a[i + 1] = ((7 - i) * a0 + i * a1) / 7; }
            else { for (int i = 1; i <= 4; i++) a[i + 1] = ((5 - i) * a0 + i * a1) / 5; a[6] = 0; a[7] = 255; }
            long bits = 0;
            for (int i = 0; i < 6; i++) bits |= (long)mip0[blk + 2 + i] << (8 * i);
            for (int i = 0; i < 16; i++)
                if (a[(int)((bits >> (3 * i)) & 7)] != 0) return false;
            return true;
        }

        static (int r, int g, int b) BlockColor(byte[] mip0, int blocksW, int bx, int by)
        {
            int p = (by * blocksW + bx) * BlockBytes + ColorOffset;
            ushort c0 = (ushort)(mip0[p] | (mip0[p + 1] << 8));
            int r = ((c0 >> 11) & 31) * 255 / 31;
            int g = ((c0 >> 5) & 63) * 255 / 63;
            int b = (c0 & 31) * 255 / 31;
            return (r, g, b);
        }

        // The 4 strip balls are joined by a ~1-2 block thin connector line. Erode vertically
        // (require a 5-tall ink run) to drop the connector so the balls separate, then 4-connect.
        static List<Blob> DetectBalls(byte[] mip0, int blocksW)
        {
            const int bandTop = 0, bandBot = 18;
            int w = blocksW;
            bool[,] core = new bool[w, bandBot + 1];
            for (int by = bandTop + 2; by <= bandBot - 2; by++)
                for (int bx = 0; bx < w; bx++)
                    core[bx, by] = Ink(mip0, w, bx, by) && Ink(mip0, w, bx, by - 1) && Ink(mip0, w, bx, by + 1)
                                   && Ink(mip0, w, bx, by - 2) && Ink(mip0, w, bx, by + 2);

            var blobs = new List<Blob>();
            bool[,] seen = new bool[w, bandBot + 1];
            for (int by = 0; by <= bandBot; by++)
                for (int bx = 0; bx < w; bx++)
                {
                    if (!core[bx, by] || seen[bx, by]) continue;
                    var blob = new Blob { MinBX = bx, MinBY = by, MaxBX = bx, MaxBY = by };
                    var stack = new Stack<(int, int)>();
                    stack.Push((bx, by));
                    seen[bx, by] = true;
                    while (stack.Count > 0)
                    {
                        var (x, y) = stack.Pop();
                        blob.Area++;
                        if (x < blob.MinBX) blob.MinBX = x;
                        if (x > blob.MaxBX) blob.MaxBX = x;
                        if (y < blob.MinBY) blob.MinBY = y;
                        if (y > blob.MaxBY) blob.MaxBY = y;
                        foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                        {
                            if (nx < 0 || nx >= w || ny < 0 || ny > bandBot) continue;
                            if (core[nx, ny] && !seen[nx, ny]) { seen[nx, ny] = true; stack.Push((nx, ny)); }
                        }
                    }
                    if (blob.Area >= 8)
                    {
                        // dilate back by 2 to recover the eroded ball rim
                        blob.MinBX = Math.Max(0, blob.MinBX - 2);
                        blob.MinBY = Math.Max(0, blob.MinBY - 2);
                        blob.MaxBX = Math.Min(w - 1, blob.MaxBX + 2);
                        blob.MaxBY = Math.Min(bandBot, blob.MaxBY + 2);
                        blobs.Add(blob);
                    }
                }
            return blobs;
        }

        // The 4 strip balls sit on one row (center y=28 -> cy=7), are ball-sized (~11-12 blocks),
        // and the Blizzard ball is the bright neutral (silver) one. Filter to the strip row + ball
        // size + brightness, then pick the least-saturated -> the white/silver Blizzard ball.
        // (Rejects the gold label trio at x40..140, the navy mask, and the larger y=40 text blobs.)
        static Blob? PickWhiteBall(byte[] mip0, int blocksW, List<Blob> blobs)
        {
            Blob? best = null;
            int bestSat = int.MaxValue;
            foreach (Blob b in blobs)
            {
                int w = b.MaxBX - b.MinBX + 1, h = b.MaxBY - b.MinBY + 1;
                if (b.Cy < 5 || b.Cy > 9) continue;        // strip row only
                if (w < 9 || w > 15 || h < 7 || h > 14) continue; // ball-sized only
                var (r, g, bl) = BlockColor(mip0, blocksW, b.Cx, b.Cy);
                int mn = Math.Min(r, Math.Min(g, bl));
                if (mn < 80) continue;                     // bright (rejects dark navy mask)
                int sat = Math.Max(r, Math.Max(g, bl)) - mn;
                if (sat < bestSat) { bestSat = sat; best = b; }
            }
            return bestSat <= 40 ? best : null;            // require near-neutral (silver/white)
        }

        static List<(int bx, int by)> FindTransparentTiles(byte[] mip0, int blocksW, int blocksH, int tileW, int tileH,
            List<(int bx0, int by0, int bx1, int by1)> reserved, int count)
        {
            var picked = new List<(int bx, int by)>();
            int stepX = 4, stepY = 4;
            for (int by = 0; by + tileH + 1 < blocksH && picked.Count < count; by += stepY)
                for (int bx = 0; bx + tileW + 1 < blocksW && picked.Count < count; bx += stepX)
                {
                    if (!RegionTransparent(mip0, blocksW, bx - 1, by - 1, tileW + 2, tileH + 2)) continue;
                    if (reserved.Any(r => Overlaps(bx, by, tileW, tileH, r))) continue;
                    if (picked.Any(p => Math.Abs(p.bx - bx) < tileW + 4 && Math.Abs(p.by - by) < tileH + 4)) continue;
                    picked.Add((bx, by));
                }
            return picked;
        }

        static bool RegionTransparent(byte[] mip0, int blocksW, int bx0, int by0, int w, int h)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int x = bx0 + i, y = by0 + j;
                    if (x < 0 || y < 0) continue;
                    if (!Transparent(mip0, blocksW, x, y)) return false;
                }
            return true;
        }

        static bool Overlaps(int bx, int by, int w, int h, (int bx0, int by0, int bx1, int by1) r)
            => bx <= r.bx1 && bx + w - 1 >= r.bx0 && by <= r.by1 && by + h - 1 >= r.by0;

        static void CopyBlockRect(byte[] mip0, int blocksW, int sbx, int sby, int dbx, int dby, int w, int h)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int s = ((sby + j) * blocksW + (sbx + i)) * BlockBytes;
                    int d = ((dby + j) * blocksW + (dbx + i)) * BlockBytes;
                    Array.Copy(mip0, s, mip0, d, BlockBytes);
                }
        }

        static void RecolorBlockRectEndpoints(byte[] mip0, int blocksW, int bx0, int by0, int w, int h, (double r, double g, double b) t)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int p = ((by0 + j) * blocksW + (bx0 + i)) * BlockBytes + ColorOffset;
                    ushort c0 = (ushort)(mip0[p] | (mip0[p + 1] << 8));
                    ushort c1 = (ushort)(mip0[p + 2] | (mip0[p + 3] << 8));
                    c0 = ScaleRgb565(c0, t);
                    c1 = ScaleRgb565(c1, t);
                    mip0[p] = (byte)(c0 & 0xFF); mip0[p + 1] = (byte)(c0 >> 8);
                    mip0[p + 2] = (byte)(c1 & 0xFF); mip0[p + 3] = (byte)(c1 >> 8);
                }
        }

        static ushort ScaleRgb565(ushort value, (double r, double g, double b) t)
        {
            int r = ((value >> 11) & 31) * 255 / 31;
            int g = ((value >> 5) & 63) * 255 / 63;
            int b = (value & 31) * 255 / 31;
            r = Math.Clamp((int)Math.Round(r * t.r), 0, 255);
            g = Math.Clamp((int)Math.Round(g * t.g), 0, 255);
            b = Math.Clamp((int)Math.Round(b * t.b), 0, 255);
            return (ushort)(((r * 31 / 255) << 11) | ((g * 63 / 255) << 5) | (b * 31 / 255));
        }

        static bool DiffConfinedToTiles(byte[] a, byte[] b, int blocksW, (int bx, int by, int w, int h)[] tiles,
            out int changedBlocks, out int strayBlocks)
        {
            changedBlocks = 0; strayBlocks = 0;
            int totalBlocks = a.Length / BlockBytes;
            for (int blk = 0; blk < totalBlocks; blk++)
            {
                int off = blk * BlockBytes;
                bool diff = false;
                for (int k = 0; k < BlockBytes; k++) if (a[off + k] != b[off + k]) { diff = true; break; }
                if (!diff) continue;
                changedBlocks++;
                int bx = blk % blocksW, by = blk / blocksW;
                bool inTile = tiles.Any(t => bx >= t.bx && bx < t.bx + t.w && by >= t.by && by < t.by + t.h);
                if (!inTile) strayBlocks++;
            }
            return changedBlocks > 0;
        }
    }
}

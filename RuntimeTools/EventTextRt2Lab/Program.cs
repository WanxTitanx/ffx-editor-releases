// eventtextrt2 — RT2 LIVE editor for EVENT dialogue text (chunk 1 = Japanese Text), via the ffx-probe.dll MMF.
//
// This speaks the SAME Command Block protocol as RuntimeTools/FfxDinput8Probe/ctl (op 1=READ, 2=WRITE on the
// game's MAIN THREAD), but as a STANDALONE tool so it never edits the shared probe sources (Codex lane).
//
// It resolves the live event ATEL text chunk via the IDA-proven global pointer
//   g_FFX_Event_AtelScriptChunkPtr? no — g_FFX_Event_Chunk1Ptr @ VA 0x1135DF0  (= ev01_base + *(ev01_base+8) = chunk1 text)
// (see docs/reverse/FFX_EVENT_IDA_HOST_RENAME_QUEUE_2026-06-05.md), parses the 8-byte field-string table, and
// patches a string in place. In-RAM patch is LENGTH-BOUNDED (new <= old) so it never overruns the next string.
//
// !!! UNTESTED ON A LIVE GAME (built offline). The proof is running it with FFX open + ffx-probe.dll attached.
//     Running the probe is a SHARED resource (Codex) -> only with Halyson's OK. This tool only WRITES/READS via
//     the already-attached probe; it does not inject anything.
//
// Usage (game must be running with ffx-probe.dll + sitting in an event/cutscene):
//   eventtextrt2 ptr                         -> show moduleBase + the live chunk1 pointer
//   eventtextrt2 list [absChunkHex]          -> list field-string entries (index, offset, raw hex, ascii-ish)
//   eventtextrt2 swap <dstIdx> <srcIdx> [absChunkHex]   -> overwrite entry dst's string with entry src's bytes
//   eventtextrt2 patch <idx> <hexbytes> [absChunkHex]   -> raw-write bytes (+terminator) into entry idx (len<=old)
// absChunkHex (optional) overrides the resolved chunk pointer (e.g. the live English chunk addr found by other means).

using System.Globalization;
using System.IO.MemoryMappedFiles;

const string MMF = "Local\\FFXProbeBlock_v1";
const int SIZE = 580;
// Command Block field offsets (match ffx_probe_block.h / ctl/Program.cs).
const int O_MAGIC = 0, O_BASE = 8, O_HOOKED = 16, O_SEQ = 20, O_ACK = 24, O_OP = 28,
          O_STATUS = 32, O_ADDR = 36, O_LEN = 40, O_ABI = 44, O_A0 = 48, O_A1 = 52, O_A2 = 56,
          O_RET = 60, O_ERR = 64, O_BUF = 68;
const uint MAGIC = 0x46585042;
const uint IMAGE_BASE = 0x400000;
const uint VA_CHUNK1_PTR = 0x1135DF0; // g_FFX_Event_Chunk1Ptr (IDA: ev01_base + *(ev01_base+8) = chunk1 = JP text)

if (args.Length == 0)
{
    Console.WriteLine("uso: ptr | list [absChunkHex] | swap <dstIdx> <srcIdx> [absChunkHex] | patch <idx> <hexbytes> [absChunkHex]");
    Console.WriteLine("  (FFX deve estar rodando com ffx-probe.dll anexado, parado num evento/cutscene)");
    return 1;
}

MemoryMappedViewAccessor acc;
try
{
    var mmf = MemoryMappedFile.OpenExisting(MMF, MemoryMappedFileRights.ReadWrite);
    acc = mmf.CreateViewAccessor(0, SIZE, MemoryMappedFileAccess.ReadWrite);
}
catch (Exception ex)
{
    Console.WriteLine($"MMF '{MMF}' nao encontrado — o jogo esta rodando com ffx-probe.dll carregado? ({ex.GetType().Name})");
    return 2;
}

uint U(int o) => acc.ReadUInt32(o);
void W(int o, uint v) => acc.Write(o, v);
if (U(O_MAGIC) != MAGIC) Console.WriteLine($"[aviso] magic=0x{U(O_MAGIC):X8} (esperado 0x{MAGIC:X8}) — modulo anexou?");

uint baseAddr = U(O_BASE);
uint AbsOf(uint va) => baseAddr + (va - IMAGE_BASE);
uint ParseHex(string s) => uint.Parse(s.Replace("0x", ""), NumberStyles.HexNumber);

// Arm an op on the main thread and wait for ack. Returns STATUS (1 = OK).
uint Arm(uint op, uint addr, uint len)
{
    W(O_OP, op); W(O_ADDR, addr); W(O_LEN, len); W(O_ABI, 0);
    W(O_A0, 0); W(O_A1, 0); W(O_A2, 0); W(O_STATUS, 0); W(O_ERR, 0);
    uint seq = U(O_SEQ) + 1; W(O_SEQ, seq);
    for (int i = 0; i < 400; i++) { if (U(O_ACK) == seq) return U(O_STATUS); Thread.Sleep(5); }
    Console.WriteLine("[timeout] sem ack — heartbeat vivo? hook instalado?");
    return 0xFFFFFFFF;
}

byte[]? ReadAbs(uint absAddr, int len)
{
    if (len > 512) len = 512;
    if (Arm(1, absAddr, (uint)len) != 1) return null;
    var b = new byte[len];
    for (int i = 0; i < len; i++) b[i] = acc.ReadByte(O_BUF + i);
    return b;
}
uint WriteAbs(uint absAddr, byte[] bytes)
{
    int len = Math.Min(bytes.Length, 512);
    for (int i = 0; i < len; i++) acc.Write(O_BUF + i, bytes[i]);
    return Arm(2, absAddr, (uint)len);
}
uint? Ru32(uint absAddr) { var b = ReadAbs(absAddr, 4); return b == null ? null : BitConverter.ToUInt32(b, 0); }
bool PtrOk(uint p) => p >= 0x10000 && p < 0x7FFF0000;

// Resolve the live chunk1 (text) pointer (or use an explicit override).
uint ResolveChunk(string? overrideHex)
{
    if (!string.IsNullOrEmpty(overrideHex)) return ParseHex(overrideHex);
    var p = Ru32(AbsOf(VA_CHUNK1_PTR));
    if (p == null || !PtrOk(p.Value))
    {
        Console.WriteLine($"chunk1 ptr invalido (0x{p?.ToString("X8") ?? "?"}) — o jogo esta num evento com texto carregado?");
        return 0;
    }
    return p.Value;
}

// Read a NUL-terminated field-string at chunk+offset (cap 256).
byte[] ReadString(uint chunkPtr, ushort off)
{
    var raw = ReadAbs(chunkPtr + off, 256);
    if (raw == null) return Array.Empty<byte>();
    int n = Array.IndexOf(raw, (byte)0);
    if (n < 0) n = raw.Length;
    var s = new byte[n];
    Array.Copy(raw, s, n);
    return s;
}
string Ascii(byte[] b)
{
    var sb = new System.Text.StringBuilder();
    foreach (byte c in b) sb.Append(c is >= 0x20 and < 0x7F ? (char)c : '.');
    return sb.ToString();
}

string mode = args[0].ToLowerInvariant();

if (mode == "ptr")
{
    Console.WriteLine($"moduleBase=0x{baseAddr:X8} hooked={U(O_HOOKED)}");
    var p = Ru32(AbsOf(VA_CHUNK1_PTR));
    Console.WriteLine($"g_FFX_Event_Chunk1Ptr @0x{AbsOf(VA_CHUNK1_PTR):X8} -> 0x{p?.ToString("X8") ?? "?"} (chunk1 = JP text)");
    return 0;
}

if (mode == "list")
{
    uint chunk = ResolveChunk(args.Length > 1 ? args[1] : null);
    if (chunk == 0) return 3;
    var hdr = ReadAbs(chunk, 2);
    if (hdr == null) { Console.WriteLine("nao consegui ler o header do chunk"); return 3; }
    int headerLen = BitConverter.ToUInt16(hdr, 0);
    if (headerLen <= 0 || headerLen % 8 != 0) { Console.WriteLine($"headerLen invalido (0x{headerLen:X}) — chunk errado?"); return 3; }
    int count = headerLen / 8;
    Console.WriteLine($"chunk @0x{chunk:X8} headerLen={headerLen} entries={count}");
    for (int i = 0; i < count; i++)
    {
        var e = ReadAbs(chunk + (uint)(i * 8), 8);
        if (e == null) continue;
        ushort off = BitConverter.ToUInt16(e, 0);
        byte[] s = off == 0 ? Array.Empty<byte>() : ReadString(chunk, off);
        Console.WriteLine($"  [{i,3}] off=0x{off:X4} len={s.Length,3}  \"{Ascii(s)}\"  ({Convert.ToHexString(s)})");
    }
    return 0;
}

if (mode == "swap")
{
    if (args.Length < 3) { Console.WriteLine("uso: swap <dstIdx> <srcIdx> [absChunkHex]"); return 1; }
    int dst = int.Parse(args[1]), src = int.Parse(args[2]);
    uint chunk = ResolveChunk(args.Length > 3 ? args[3] : null);
    if (chunk == 0) return 3;
    ushort dstOff = EntryOff(chunk, dst), srcOff = EntryOff(chunk, src);
    if (dstOff == 0 || srcOff == 0) { Console.WriteLine("entrada com offset 0 (vazia) — escolha outra"); return 3; }
    byte[] dstS = ReadString(chunk, dstOff), srcS = ReadString(chunk, srcOff);
    if (srcS.Length > dstS.Length) { Console.WriteLine($"src ({srcS.Length}b) maior que dst ({dstS.Length}b) — escolha src <= dst (RAM in-place)"); return 4; }
    byte[] payload = new byte[srcS.Length + 1]; // src + NUL terminator
    Array.Copy(srcS, payload, srcS.Length);
    uint st = WriteAbs(chunk + dstOff, payload);
    Console.WriteLine($"swap [{dst}]<-[{src}] @0x{chunk + dstOff:X8} wrote {payload.Length}b status={st} err=0x{U(O_ERR):X8}");
    Console.WriteLine($"  dst era \"{Ascii(dstS)}\" -> agora \"{Ascii(srcS)}\"  (re-exiba a fala no jogo p/ ver)");
    return st == 1 ? 0 : 5;
}

if (mode == "patch")
{
    if (args.Length < 3) { Console.WriteLine("uso: patch <idx> <hexbytes> [absChunkHex]"); return 1; }
    int idx = int.Parse(args[1]);
    string hx = args[2].Replace(" ", "").Replace("0x", "");
    byte[] bytes = new byte[hx.Length / 2];
    for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hx.Substring(i * 2, 2), 16);
    uint chunk = ResolveChunk(args.Length > 3 ? args[3] : null);
    if (chunk == 0) return 3;
    ushort off = EntryOff(chunk, idx);
    if (off == 0) { Console.WriteLine("entrada vazia (offset 0)"); return 3; }
    byte[] cur = ReadString(chunk, off);
    if (bytes.Length > cur.Length) { Console.WriteLine($"novo ({bytes.Length}b) maior que atual ({cur.Length}b) — RAM in-place exige <= atual"); return 4; }
    byte[] payload = new byte[bytes.Length + 1];
    Array.Copy(bytes, payload, bytes.Length);
    uint st = WriteAbs(chunk + off, payload);
    Console.WriteLine($"patch [{idx}] @0x{chunk + off:X8} wrote {payload.Length}b status={st} err=0x{U(O_ERR):X8}  (re-exiba a fala no jogo)");
    return st == 1 ? 0 : 5;
}

Console.WriteLine($"modo desconhecido: {mode}");
return 1;

// helper: read entry i's regular string offset (u16 @ chunk + i*8)
ushort EntryOff(uint chunk, int i)
{
    var e = ReadAbs(chunk + (uint)(i * 8), 2);
    return e == null ? (ushort)0 : BitConverter.ToUInt16(e, 0);
}

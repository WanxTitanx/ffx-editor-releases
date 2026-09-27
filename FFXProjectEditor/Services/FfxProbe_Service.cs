using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;

namespace FFXProjectEditor.Services
{
    // Editor-side bridge to the FFX DINPUT8 in-process probe (RuntimeTools/FfxDinput8Probe).
    // The probe (ffx-probe.dll, loaded by the FFX Module Loader) executes READ/WRITE/CALL on the
    // game's MAIN THREAD via the GetDeviceState hook; this service arms commands through the shared
    // Command Block (memory-mapped file) and reads results back. This is the HONEST replacement for
    // the CreateRemoteThread path (which runs off the main thread and crashes).
    //
    // STANDBY by design: nothing executes in the game until a command is armed here. All addresses are
    // RVAs (VA - 0x400000); the probe reports the live moduleBase so callers stay ASLR-safe.
    public sealed class FfxProbe_Service
    {
        public static FfxProbe_Service Instance { get; } = new FfxProbe_Service();

        const string MmfName = "Local\\FFXProbeBlock_v1";
        const int BlockSize = 580;
        const uint Magic = 0x46585042; // 'FXPB'

        // Command Block field offsets (match RuntimeTools/FfxDinput8Probe/ffx_probe_block.h, pack(1)).
        const int O_MAGIC = 0, O_VERSION = 4, O_BASE = 8, O_HEARTBEAT = 12, O_HOOKED = 16, O_SEQ = 20,
                  O_ACK = 24, O_OPCODE = 28, O_STATUS = 32, O_ADDR = 36, O_LEN = 40, O_ABI = 44,
                  O_A0 = 48, O_A1 = 52, O_A2 = 56, O_RET = 60, O_ERR = 64, O_BUF = 68;
        const int BufCapacity = 512;

        // opcodes
        const uint OP_READ = 1, OP_WRITE = 2, OP_CALL = 3, OP_FORCEBATTLE = 4;
        const uint OP_KETHRES_START = 10, OP_KETHRES_STOP = 11, OP_KETHRES_DRAIN = 12, OP_KETHRES_SNAP = 13;
        const uint OP_PPPDRAW_START = 14, OP_PPPDRAW_STOP = 15, OP_PPPDRAW_DRAIN = 16;
        const uint ERR_PPPDRAW_RETIRED = 0x50505044u; /* match FFXPROBE_ERR_PPPDRAW_RETIRED */

        /// <summary>Inline EXE hook on <c>sub_71B980</c> retired 2026-06-15 (crash + zero RT2 value).</summary>
        public const bool PppDrawTintRetired = true;

        // calling conventions (CALL)
        public const uint AbiCdeclI = 1, AbiCdeclII = 2, AbiCdeclIII = 3, AbiCdeclIIF = 4;

        const uint StatusOk = 1;

        readonly object gate = new object();
        MemoryMappedFile? mmf;
        MemoryMappedViewAccessor? view;

        FfxProbe_Service() { }

        bool TryAttach()
        {
            if (view != null)
                return true;
            try
            {
                mmf = MemoryMappedFile.OpenExisting(MmfName, MemoryMappedFileRights.ReadWrite);
                view = mmf.CreateViewAccessor(0, BlockSize, MemoryMappedFileAccess.ReadWrite);
                return true;
            }
            catch
            {
                Detach();
                return false;
            }
        }

        void Detach()
        {
            view?.Dispose();
            mmf?.Dispose();
            view = null;
            mmf = null;
        }

        /// <summary>True when the probe module is loaded in the running game (Command Block present + magic ok).</summary>
        public bool IsAttached
        {
            get
            {
                lock (gate)
                {
                    if (!TryAttach())
                        return false;
                    try { return view!.ReadUInt32(O_MAGIC) == Magic; }
                    catch { Detach(); return false; }
                }
            }
        }

        /// <summary>1 once the GetDeviceState hook is live (the probe runs on the main thread per frame).</summary>
        public bool IsHooked => ReadField(O_HOOKED) == 1;

        /// <summary>Per-frame counter incremented on the game's main thread (proof of life).</summary>
        public uint Heartbeat => ReadField(O_HEARTBEAT);

        /// <summary>The FFX.exe runtime base address (RVA + this = absolute).</summary>
        public uint ModuleBase => ReadField(O_BASE);

        uint ReadField(int offset)
        {
            lock (gate)
            {
                if (!TryAttach())
                    return 0;
                try { return view!.ReadUInt32(offset); }
                catch { Detach(); return 0; }
            }
        }

        public sealed class ProbeResult
        {
            public required bool Ok { get; init; }
            public required uint Status { get; init; }
            public required int Ret { get; init; }
            public required uint Error { get; init; }
            public byte[]? Data { get; init; }
        }

        /// <summary>READ len bytes at base+rva on the main thread.</summary>
        public ProbeResult Read(uint rva, int len)
        {
            if (len < 0 || len > BufCapacity)
                throw new ArgumentOutOfRangeException(nameof(len));
            return Arm(OP_READ, ModuleBaseChecked() + rva, (uint)len, 0, 0, 0, 0, readBackLen: len);
        }

        /// <summary>READ len bytes from an absolute process address on the main thread. Use this for heap
        /// pointers such as MemoryChr.Ptr_script_chunks / Ptr_script_data, which are not module-relative RVAs.</summary>
        public ProbeResult ReadAbsolute(uint absoluteAddress, int len)
        {
            if (len < 0 || len > BufCapacity)
                throw new ArgumentOutOfRangeException(nameof(len));
            return Arm(OP_READ, absoluteAddress, (uint)len, 0, 0, 0, 0, readBackLen: len);
        }

        /// <summary>READ an arbitrary-length buffer via repeated 512-byte main-thread reads.</summary>
        public ProbeResult ReadAbsoluteBuffered(uint absoluteAddress, int totalLen)
        {
            if (totalLen < 0)
                throw new ArgumentOutOfRangeException(nameof(totalLen));
            if (totalLen == 0)
                return new ProbeResult { Ok = true, Status = StatusOk, Ret = 0, Error = 0, Data = Array.Empty<byte>() };

            var buf = new byte[totalLen];
            int offset = 0;
            while (offset < totalLen)
            {
                int chunk = Math.Min(BufCapacity, totalLen - offset);
                ProbeResult r = ReadAbsolute(absoluteAddress + (uint)offset, chunk);
                if (!r.Ok || r.Data == null)
                    return r;
                Buffer.BlockCopy(r.Data, 0, buf, offset, chunk);
                offset += chunk;
            }

            return new ProbeResult { Ok = true, Status = StatusOk, Ret = totalLen, Error = 0, Data = buf };
        }

        /// <summary>WRITE bytes at base+rva on the main thread.</summary>
        public ProbeResult Write(uint rva, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length > BufCapacity)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            return Arm(OP_WRITE, ModuleBaseChecked() + rva, (uint)bytes.Length, 0, 0, 0, 0, writeData: bytes);
        }

        /// <summary>WRITE bytes to an absolute process address on the main thread. This is the honest path
        /// for live ATEL edits because the script chunks sit on heap allocations, not inside FFX.exe image space.</summary>
        public ProbeResult WriteAbsolute(uint absoluteAddress, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length > BufCapacity)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            return Arm(OP_WRITE, absoluteAddress, (uint)bytes.Length, 0, 0, 0, 0, writeData: bytes);
        }

        /// <summary>CALL an engine function at base+rva on the main thread (see Abi* constants).</summary>
        public ProbeResult Call(uint rva, uint abi, uint a0 = 0, uint a1 = 0, uint a2 = 0)
        {
            return Arm(OP_CALL, ModuleBaseChecked() + rva, 0, abi, a0, a1, a2);
        }

        /// <summary>CALL with a float third arg (CDECL int(int,int,float)).</summary>
        public ProbeResult CallIIF(uint rva, int a0, int a1, float a2)
        {
            return Call(rva, AbiCdeclIIF, (uint)a0, (uint)a1, BitConverter.ToUInt32(BitConverter.GetBytes(a2)));
        }

        /// <summary>Atomic scripted-encounter force on the main thread (sets flags + calls MsBattleEncountExe in one frame).</summary>
        public ProbeResult ForceBattle(int field, int group, int formation = 0)
        {
            return Arm(OP_FORCEBATTLE, 0, 0, 0, (uint)field, (uint)group, (uint)formation);
        }

        /// <summary>Install + arm KeThRes buffer-link hook (<c>sub_72C570</c> / host+2840 path).</summary>
        public ProbeResult KeThResAttachLogStart() => Arm(OP_KETHRES_START, 0, 0, 0, 0, 0, 0);

        /// <summary>Disarm KeThRes hook; <see cref="ProbeResult.Ret"/> = record count.</summary>
        public ProbeResult KeThResAttachLogStop() => Arm(OP_KETHRES_STOP, 0, 0, 0, 0, 0, 0);

        public sealed record KeThResAttachRecord(uint Frame, uint BufferPtr, uint Size, uint HandlePtr);

        /// <summary>Drain KeThRes attach ring records starting at <paramref name="startIndex"/>.</summary>
        public (IReadOnlyList<KeThResAttachRecord> Records, uint TotalCount) KeThResAttachLogDrain(uint startIndex = 0)
        {
            var list = new List<KeThResAttachRecord>();
            uint start = startIndex;
            for (int safety = 0; safety < 8192; safety++)
            {
                ProbeResult r = Arm(OP_KETHRES_DRAIN, 0, 0, 0, start, 0, 0, readBackLen: 128);
                if (!r.Ok)
                    break;
                uint copied = (uint)Math.Max(0, r.Ret);
                uint total = ReadField(O_LEN);
                if (copied == 0)
                    break;
                if (r.Data == null)
                    break;
                for (int i = 0; i < copied; i++)
                {
                    int bo = i * 16;
                    list.Add(new KeThResAttachRecord(
                        BitConverter.ToUInt32(r.Data, bo),
                        BitConverter.ToUInt32(r.Data, bo + 4),
                        BitConverter.ToUInt32(r.Data, bo + 8),
                        BitConverter.ToUInt32(r.Data, bo + 12)));
                }
                start += copied;
                if (start >= total)
                    return (list, total);
            }
            return (list, (uint)list.Count);
        }

        /// <summary>In-hook 4 KiB snap of magic DLL KeThRes blob (main thread, on attach).</summary>
        public (byte[] Data, uint BufferPtr, uint SnapCount, int NonZero) KeThResSnapRead(uint offset = 0)
        {
            ProbeResult r = Arm(OP_KETHRES_SNAP, 0, 0, 0, offset, 0, 0, readBackLen: BufCapacity);
            return (
                r.Data ?? Array.Empty<byte>(),
                ReadField(O_ADDR),
                ReadField(O_LEN),
                r.Ok ? r.Ret : 0);
        }

        public byte[]? KeThResSnapReadFull(out uint bufferPtr, out uint snapCount, out int nonZero)
        {
            bufferPtr = 0;
            snapCount = 0;
            nonZero = 0;
            var full = new byte[4096];
            int offset = 0;
            while (offset < full.Length)
            {
                (byte[] chunk, uint ptr, uint count, int nz) = KeThResSnapRead((uint)offset);
                if (count == 0 && offset == 0)
                    return null;
                bufferPtr = ptr;
                snapCount = count;
                nonZero = nz;
                if (chunk.Length == 0)
                    break;
                Buffer.BlockCopy(chunk, 0, full, offset, chunk.Length);
                offset += chunk.Length;
            }

            return full;
        }

        /// <summary>Install hook on <c>FFX_MagicHost_ApplyPppDrawableColors</c> (RVA <c>0x31B980</c>).</summary>
        /// <param name="forceTint">When true, overwrite vec4 tint at struct+4 on each hit.</param>
        public ProbeResult PppDrawTintLogStart(bool forceTint = false, float r = 1f, float g = 0.35f, float b = 0.05f)
        {
            if (PppDrawTintRetired)
                return PppDrawRetiredResult();
            byte[]? buf = forceTint ? BitConverter.GetBytes(b) : null;
            return Arm(OP_PPPDRAW_START, 0, buf != null ? 4u : 0u, 0,
                forceTint ? 1u : 0u,
                BitConverter.ToUInt32(BitConverter.GetBytes(r)),
                BitConverter.ToUInt32(BitConverter.GetBytes(g)),
                writeData: buf);
        }

        public uint PppDrawHookAddress => PppDrawTintRetired ? 0u : ReadField(O_ADDR);

        public ProbeResult PppDrawTintLogStop() =>
            PppDrawTintRetired ? PppDrawRetiredResult() : Arm(OP_PPPDRAW_STOP, 0, 0, 0, 0, 0, 0);

        public sealed record PppDrawTintRecord(
            uint Frame,
            uint A0,
            uint A1,
            uint A2,
            uint A3,
            float TintR,
            float TintG,
            float TintB,
            float TintA);

        public (IReadOnlyList<PppDrawTintRecord> Records, uint TotalCount) PppDrawTintLogDrain(uint startIndex = 0)
        {
            if (PppDrawTintRetired)
                return (Array.Empty<PppDrawTintRecord>(), 0);
            var list = new List<PppDrawTintRecord>();
            uint start = startIndex;
            for (int safety = 0; safety < 8192; safety++)
            {
                ProbeResult r = Arm(OP_PPPDRAW_DRAIN, 0, 0, 0, start, 0, 0, readBackLen: 512);
                if (!r.Ok)
                    break;
                uint copied = (uint)Math.Max(0, r.Ret);
                uint total = ReadField(O_LEN);
                if (copied == 0)
                    break;
                if (r.Data == null)
                    break;
                for (int i = 0; i < copied; i++)
                {
                    int bo = i * 32;
                    list.Add(new PppDrawTintRecord(
                        BitConverter.ToUInt32(r.Data, bo),
                        BitConverter.ToUInt32(r.Data, bo + 4),
                        BitConverter.ToUInt32(r.Data, bo + 8),
                        BitConverter.ToUInt32(r.Data, bo + 12),
                        BitConverter.ToUInt32(r.Data, bo + 16),
                        BitConverter.ToSingle(r.Data, bo + 20),
                        BitConverter.ToSingle(r.Data, bo + 24),
                        BitConverter.ToSingle(r.Data, bo + 28),
                        1f));
                }
                start += copied;
                if (start >= total)
                    return (list, total);
            }
            return (list, (uint)list.Count);
        }

        static ProbeResult PppDrawRetiredResult() =>
            new() { Ok = false, Status = 0, Ret = 0, Error = ERR_PPPDRAW_RETIRED };

        uint ModuleBaseChecked()
        {
            uint b = ModuleBase;
            if (b == 0)
                throw new InvalidOperationException("FFX probe not attached (game running with ffx-probe.dll?).");
            return b;
        }

        ProbeResult Arm(uint opcode, uint addr, uint len, uint abi, uint a0, uint a1, uint a2,
                        byte[]? writeData = null, int readBackLen = 0)
        {
            lock (gate)
            {
                if (!TryAttach() || view!.ReadUInt32(O_MAGIC) != Magic)
                    return new ProbeResult { Ok = false, Status = 0, Ret = 0, Error = 0 };

                if (writeData != null)
                    for (int i = 0; i < writeData.Length; i++)
                        view.Write(O_BUF + i, writeData[i]);

                view.Write(O_OPCODE, opcode);
                view.Write(O_ADDR, addr);
                view.Write(O_LEN, len);
                view.Write(O_ABI, abi);
                view.Write(O_A0, a0);
                view.Write(O_A1, a1);
                view.Write(O_A2, a2);
                view.Write(O_STATUS, 0u);
                view.Write(O_ERR, 0u);

                uint seq = view.ReadUInt32(O_SEQ) + 1;
                view.Write(O_SEQ, seq); // arm last

                // wait for the main-thread hook to ack (it runs ~per frame)
                bool acked = false;
                for (int i = 0; i < 400; i++)
                {
                    if (view.ReadUInt32(O_ACK) == seq) { acked = true; break; }
                    System.Threading.Thread.Sleep(5);
                }
                if (!acked)
                    return new ProbeResult { Ok = false, Status = 0, Ret = 0, Error = view.ReadUInt32(O_ERR) };

                uint status = view.ReadUInt32(O_STATUS);
                byte[]? data = null;
                if (readBackLen > 0 && status == StatusOk)
                {
                    data = new byte[readBackLen];
                    for (int i = 0; i < readBackLen; i++)
                        data[i] = view.ReadByte(O_BUF + i);
                }

                return new ProbeResult
                {
                    Ok = status == StatusOk,
                    Status = status,
                    Ret = view.ReadInt32(O_RET),
                    Error = view.ReadUInt32(O_ERR),
                    Data = data
                };
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>
/// Compresses a reviewed PPP snapshot's finite timelines. Q12 counters advance by
/// 0x1000 at FFX.exe VA 0x717282; +20 is program lifetime, +24/+28 are loop counters.
/// This is a dedicated writer: it does not relax the ordinary field writer's prefix guard.
/// </summary>
internal static class MagicPppTimingRewriter
{
    internal const uint FrameUnit = 0x1000;
    internal const uint Infinite = 0x70000000;
    internal const uint EndRecord = 0xFFFFF000;
    internal sealed record Edit(int DataOffset, string Kind, double Before, double After, string Bytes);
    internal sealed record Result(byte[] Bytes, IReadOnlyList<Edit> Edits, int[] BeforeFrames, int[] AfterFrames);

    internal static Result Compress(MagicDllFile file, int maximumFrames, string expectedSha256)
    {
        if (maximumFrames is < 1 or > 6000) throw new ArgumentOutOfRangeException(nameof(maximumFrames));
        byte[] original = file.FileBytes;
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(original)), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PPP timing input SHA-256 changed.");
        if (file.DataSectionRawPtr < 0 || file.DataSectionSize != file.Data.Length ||
            file.DataSectionRawPtr > original.Length - file.Data.Length ||
            !original.AsSpan(file.DataSectionRawPtr, file.Data.Length).SequenceEqual(file.Data))
            throw new InvalidDataException("PPP data snapshot is inconsistent.");
        byte[] data = file.Data;
        var changes = new Dictionary<int,Edit>();
        var before = new List<int>(); var after = new List<int>();
        var programs = file.Roots.SelectMany(r=>r.Programs).Where(p=>p.SlotCount>0).ToArray();
        if (programs.Length == 0) throw new InvalidDataException("No PPP timelines found.");

        void Bounds(int at, int size)
        {
            if (at < 0 || size < 0 || at > data.Length - size) throw new InvalidDataException("PPP timing range is outside .data.");
        }
        uint Read(int at) { Bounds(at,4); return BitConverter.ToUInt32(data,at); }
        void Plan(int at, string kind, double old, double value, byte[] bytes)
        {
            Bounds(at,bytes.Length);
            if (data.AsSpan(at,bytes.Length).SequenceEqual(bytes)) return;
            string hex = Convert.ToHexString(bytes);
            if (changes.TryGetValue(at,out var prior))
            {
                if (prior.Bytes != hex) throw new InvalidDataException("Shared PPP operands require incompatible edits.");
                return;
            }
            if (changes.Values.Any(other=>at<other.DataOffset+other.Bytes.Length/2 && other.DataOffset<at+bytes.Length))
                throw new InvalidDataException("PPP timing writes overlap.");
            changes.Add(at,new(at,kind,old,value,hex));
        }
        void Tick(int at,uint value,string kind) => Plan(at,kind,Read(at),value,BitConverter.GetBytes(value));

        foreach (var group in programs.GroupBy(p=>p.SectionAbs))
        {
            int section=group.Key;
            uint rawLife=Read(section);
            if (rawLife==0 || rawLife>=Infinite || rawLife%FrameUnit!=0 || Read(section+4)!=0)
                throw new InvalidDataException("Only finite, non-looping PPP sections are supported.");
            int oldFrames=checked((int)(rawLife/FrameUnit)), newFrames=Math.Min(oldFrames,maximumFrames);
            before.Add(oldFrames); after.Add(newFrames);
            if (oldFrames==newFrames) continue;
            Tick(section,checked((uint)newFrames*FrameUnit),"section-duration");
            double speed=(double)oldFrames/newFrames;

            foreach (var program in group)
            {
                int head=program.ProgramAbs;
                Bounds(head,40);
                if (BitConverter.ToUInt16(data,head+38)!=program.Slots.Count || program.SlotCount!=program.Slots.Count)
                    throw new InvalidDataException("PPP program metadata changed.");
                uint oldLife=Read(head+20), newLife=MapTick(oldLife,oldFrames,newFrames,true);
                Tick(head+16,MapTick(Read(head+16),oldFrames,newFrames),"program-start");
                uint loopStart=MapTick(Read(head+24),oldFrames,newFrames);
                uint loopEnd=MapTick(Read(head+28),oldFrames,newFrames);
                if (Read(head+28)<Infinite && Read(head+28)>Read(head+24) && loopEnd<=loopStart)
                    loopEnd=checked(loopStart+FrameUnit);
                Tick(head+24,loopStart,"program-loop-start"); Tick(head+28,loopEnd,"program-loop-end");
                long activeLast=-FrameUnit;

                foreach (var slot in program.Slots)
                {
                    int width=slot.NativeRecordWidth, count=slot.Flags, start=slot.RecordOffset;
                    Bounds(slot.SlotAbs,16);
                    if (slot.SlotAbs!=head+40+16*slot.SlotIndex ||
                        BitConverter.ToUInt16(data,slot.SlotAbs+4)!=width || BitConverter.ToUInt16(data,slot.SlotAbs+6)!=count ||
                        Read(slot.SlotAbs+8)!=(uint)(start-section))
                        throw new InvalidDataException("PPP slot metadata changed.");
                    if (width<4 || width%4!=0 || count<1 || count>4096)
                        throw new InvalidDataException("Unsupported PPP subrecord layout.");
                    Bounds(start,checked(width*count+4));
                    if (Read(start+width*count)!=EndRecord) throw new InvalidDataException("PPP keyframe terminator mismatch.");
                    long previous=-FrameUnit, previousSource=-1;
                    for (int index=0;index<count;index++)
                    {
                        int at=checked(start+index*width);
                        uint sourceTick=Read(at), mapped=MapTick(sourceTick,oldFrames,newFrames);
                        if (sourceTick<Infinite)
                        {
                            if (sourceTick<=previousSource) throw new InvalidDataException("PPP keyframes are not strictly ordered.");
                            // Native dispatch compares exact Q12 ticks and advances one record.
                            // Two distinct keys must not collapse onto the same tick.
                            mapped=checked((uint)Math.Max(mapped,previous+FrameUnit));
                            previous=mapped; previousSource=sourceTick;
                            if (sourceTick<oldLife) activeLast=Math.Max(activeLast,mapped);
                        }
                        Tick(at,mapped,"record-time");
                        string? opcode=slot.OpcodeName;
                        if (opcode is "pppColMove" or "pppColAccele")
                        {
                            if (width!=16) throw new InvalidDataException("Unexpected color-rate operand width.");
                            double multiplier=opcode=="pppColAccele" ? speed*speed : speed;
                            for (int component=8;component<=14;component+=2)
                            {
                                short old=BitConverter.ToInt16(data,at+component);
                                short value=(short)Math.Clamp(Math.Round(old*multiplier),short.MinValue,short.MaxValue);
                                Plan(at+component,opcode,old,value,BitConverter.GetBytes(value));
                            }
                        }
                        if (opcode is "pppMove" or "pppSclMove" or "pppAccele" or "pppSclAccele")
                        {
                            if (width!=32) throw new InvalidDataException("Unexpected PVA operand width.");
                            double multiplier=opcode is "pppAccele" or "pppSclAccele" ? speed*speed : speed;
                            for (int component=16;component<=28;component+=4)
                            {
                                float old=BitConverter.ToSingle(data,at+component), value=(float)(old*multiplier);
                                if (!float.IsFinite(old) || !float.IsFinite(value)) throw new InvalidDataException("Non-finite PVA value.");
                                Plan(at+component,opcode,old,value,BitConverter.GetBytes(value));
                            }
                        }
                    }
                }
                if (newLife<Infinite) newLife=checked((uint)Math.Max(newLife,activeLast+FrameUnit));
                Tick(head+20,newLife,"program-lifetime");
            }
        }
        // Plan/validate first; no partial mutation escapes on rejection.
        byte[] output=(byte[])original.Clone();
        foreach (var change in changes.Values)
            Convert.FromHexString(change.Bytes).CopyTo(output,file.DataSectionRawPtr+change.DataOffset);
        return new(output,changes.Values.OrderBy(x=>x.DataOffset).ToArray(),before.ToArray(),after.ToArray());
    }

    internal static uint MapTick(uint tick,int oldFrames,int newFrames,bool duration=false)
    {
        if (oldFrames<=0 || newFrames<=0 || newFrames>oldFrames) throw new ArgumentOutOfRangeException(nameof(newFrames));
        if (tick>=Infinite) return tick; // infinite, externally spawned, and end-of-list sentinels
        if (tick%FrameUnit!=0) throw new InvalidDataException("Fractional source tick requires a separate native contract.");
        long mapped=((long)(tick/FrameUnit)*newFrames+oldFrames/2)/oldFrames;
        if (duration && tick>0) mapped=Math.Max(1,mapped);
        return checked((uint)mapped*FrameUnit);
    }
}

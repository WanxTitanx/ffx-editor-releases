using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Mods;
internal sealed record ElementalPreviewPart(int Value, int Weight);
internal sealed record ElementalPreviewRequest
{
    public int Damage { get; init; } = 1000;
    public int BaseBp { get; init; } = 10000;
    public int EquipmentBp { get; init; }
    public int Imperil { get; init; }
    public int Ward { get; init; }
    public bool Locked { get; init; }
    public int LockedValue { get; init; } = 10000;
    public string Policy { get; init; } = "split_weighted";
    public ElementalPreviewPart[] Parts { get; init; } = Array.Empty<ElementalPreviewPart>();
    public byte NativeMask { get; init; }
    public byte Weak { get; init; }
    public byte Resist { get; init; }
    public byte Nulls { get; init; }
    public byte Absorb { get; init; }
    public bool UseGravity { get; init; }
    public int MaximumHp { get; init; } = 1600;
    public int CurrentHp { get; init; } = 1600;
    public static ElementalPreviewRequest Parse(byte[] bytes)
    { HookJson.RejectDuplicates(bytes);return JsonSerializer.Deserialize<ElementalPreviewRequest>(bytes,HookProjectProfile.JsonOptions)??throw new InvalidDataException("Missing affinity preview."); }
    public string Calculate()
    {
        if(Parts==null)throw new InvalidDataException("Invalid affinity preview sources.");
        int effective=ElementalPreviewService.Effective(BaseBp,EquipmentBp,Imperil,Ward,Locked,LockedValue);
        int basis=UseGravity?ElementalPreviewService.Gravity(MaximumHp,CurrentHp,16,true):Damage;
        var parts=Parts.Length==0?new[]{(effective,1)}:Parts.Select(p=>(p.Value,p.Weight)).ToArray();
        int result=Policy=="native_exact"?ElementalPreviewService.NativeExact(basis,NativeMask,Weak,Resist,Nulls,Absorb):ElementalPreviewService.Resolve(basis,parts,Policy);
        return $"Base: {BaseBp} bp; equipment: {EquipmentBp} bp; Imperil: {Imperil}; Ward: {Ward}\nEffective: {effective} bp; locked: {Locked}\n{Policy}: {basis} → {result}\nRT0 arithmetic preview; negative values represent absorption. No game state was changed.";
    }
}

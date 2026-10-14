using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record WorkshopPolicy
{
    public uint Mode { get; init; } = 2;
    public uint BaseItem { get; init; } = 70;
    public uint BaseAmount { get; init; } = 1;
    public uint RefinementDivisor { get; init; } = 10;
    public uint FusionDivisor { get; init; } = 3;
    public uint FusionGilPerAbility { get; init; } = 10000;
    public uint ModRecipeQuantity { get; init; } = 30;
    public uint DevFreeMaterials { get; init; }
    public uint DevFreeGil { get; init; }
    public uint DevIgnoreProgression { get; init; }
    public uint[] Values() => new[] { Mode, BaseItem, BaseAmount, RefinementDivisor, FusionDivisor, FusionGilPerAbility,
        ModRecipeQuantity, DevFreeMaterials, DevFreeGil, DevIgnoreProgression };
    public void Validate()
    {
        if (Mode is < 1 or > 2 || BaseItem is < 70 or > 73 || BaseAmount is < 1 or > 99 || RefinementDivisor is < 1 or > 100 ||
            FusionDivisor is < 1 or > 100 || FusionGilPerAbility is < 1 or > 100000000 || ModRecipeQuantity is < 1 or > 255 ||
            DevFreeMaterials > 1 || DevFreeGil > 1 || DevIgnoreProgression > 1) throw new InvalidDataException("Workshop policy exceeds the canonical v5 limits.");
    }
}
internal sealed record WorkshopPlanAuthoring
{
    public uint Operation { get; init; } = 1;
    public ulong Revision { get; init; }
    public ulong PieceId { get; init; }
    public ulong OtherId { get; init; }
    public ushort Slot { get; init; }
    public ushort Other { get; init; }
    public ushort Value { get; init; }
    public byte[] From { get; init; } = new byte[2];
    public byte[] To { get; init; } = new byte[2];
    public byte Count { get; init; }
    public byte FusionPolicy { get; init; }
    public string TemplateHex { get; init; } = new('0', 44);
    public WorkshopPolicy Policy { get; init; } = new();
    public uint Gil { get; init; }
    public uint CustomizeUnlocked { get; init; }
    public uint ObtainedAeons { get; init; }
    public uint Crests { get; init; }
    public byte[] AeonGear { get; init; } = Enumerable.Repeat((byte)255,20).ToArray();
    public string CatalogHex { get; init; } = new('0',224);

    public static WorkshopPlanAuthoring Parse(byte[] bytes)
    { HookJson.RejectDuplicates(bytes); return JsonSerializer.Deserialize<WorkshopPlanAuthoring>(bytes,HookProjectProfile.JsonOptions) ?? throw new InvalidDataException("Missing Workshop request."); }
    public (byte[] Request, byte[] Economy) Encode()
    {
        Policy.Validate();
        if (Operation is < 1 or > 12 || Slot >= 200 || Other >= 200 || From?.Length != 2 || To?.Length != 2 || Count > 2 ||
            From.Any(x => x > 4) || To.Any(x => x > 4) || CustomizeUnlocked > 1 || AeonGear?.Length != 20)
            throw new InvalidDataException("Invalid Workshop request field.");
        byte[] template = Convert.FromHexString(TemplateHex), catalog = Convert.FromHexString(CatalogHex);
        if (template.Length != 22 || catalog.Length != 112) throw new InvalidDataException("Invalid Workshop template/catalog size.");
        // Create/Reforge templates must come from a verified host, not an arbitrary UI blob.
        if (Operation is 3 or 4) throw new InvalidDataException("Create/Reforge require a host-verified template snapshot.");
        byte[] request = new byte[62];
        BinaryPrimitives.WriteUInt32LittleEndian(request,Operation); BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(4),Revision);
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(12),PieceId); BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(20),OtherId);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(28),Slot); BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(30),Other);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(32),Value); From.CopyTo(request,34); To.CopyTo(request,36); request[38]=Count; request[39]=FusionPolicy; template.CopyTo(request,40);
        byte[] economy = new byte[188]; uint[] policy = Policy.Values();
        for(int i=0;i<policy.Length;i++) BinaryPrimitives.WriteUInt32LittleEndian(economy.AsSpan(i*4),policy[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(economy.AsSpan(40),Gil); BinaryPrimitives.WriteUInt32LittleEndian(economy.AsSpan(44),CustomizeUnlocked);
        BinaryPrimitives.WriteUInt32LittleEndian(economy.AsSpan(48),ObtainedAeons); BinaryPrimitives.WriteUInt32LittleEndian(economy.AsSpan(52),Crests);
        AeonGear.CopyTo(economy,56); catalog.CopyTo(economy,76);
        return(request,economy);
    }
    public static uint RefinementGil(int total)
    {
        if(total is < 1 or > 50) throw new InvalidDataException("Refinement total must be 1..50.");
        uint value=0;for(int n=1;n<=total;n++) value+=1000u+500u*(uint)((n-1)/10);return value;
    }
}

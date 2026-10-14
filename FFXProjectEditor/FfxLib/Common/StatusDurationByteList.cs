using CommunityToolkit.Mvvm.ComponentModel;
using Xe.BinaryMapper;

namespace FFXProjectEditor.FfxLib.Common
{
    public class StatusDurationByteList : ObservableObject
    {
        byte sleep;
        byte silence;
        byte darkness;
        byte shell;
        byte protect;
        byte reflect;
        byte nulTide;
        byte nulBlaze;
        byte nulShock;
        byte nulFrost;
        byte regen;
        byte haste;
        byte slow;

        [Data] public byte Sleep { get => sleep; set => SetProperty(ref sleep, value); }
        [Data] public byte Silence { get => silence; set => SetProperty(ref silence, value); }
        [Data] public byte Darkness { get => darkness; set => SetProperty(ref darkness, value); }
        [Data] public byte Shell { get => shell; set => SetProperty(ref shell, value); }
        [Data] public byte Protect { get => protect; set => SetProperty(ref protect, value); }
        [Data] public byte Reflect { get => reflect; set => SetProperty(ref reflect, value); }
        [Data] public byte NulTide { get => nulTide; set => SetProperty(ref nulTide, value); }
        [Data] public byte NulBlaze { get => nulBlaze; set => SetProperty(ref nulBlaze, value); }
        [Data] public byte NulShock { get => nulShock; set => SetProperty(ref nulShock, value); }
        [Data] public byte NulFrost { get => nulFrost; set => SetProperty(ref nulFrost, value); }
        [Data] public byte Regen { get => regen; set => SetProperty(ref regen, value); }
        [Data] public byte Haste { get => haste; set => SetProperty(ref haste, value); }
        [Data] public byte Slow { get => slow; set => SetProperty(ref slow, value); }
    }
}

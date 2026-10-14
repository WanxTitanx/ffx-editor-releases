using CommunityToolkit.Mvvm.ComponentModel;
using Xe.BinaryMapper;

namespace FFXProjectEditor.FfxLib.Common
{
    public class StatusByteList : ObservableObject
    {
        byte death;
        byte zombie;
        byte petrify;
        byte poison;
        byte breakPower;
        byte breakMagic;
        byte breakArmor;
        byte breakMental;
        byte confuse;
        byte berserk;
        byte provoke;
        byte threaten;
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

        [Data] public byte Death { get => death; set => SetProperty(ref death, value); }
        [Data] public byte Zombie { get => zombie; set => SetProperty(ref zombie, value); }
        [Data] public byte Petrify { get => petrify; set => SetProperty(ref petrify, value); }
        [Data] public byte Poison { get => poison; set => SetProperty(ref poison, value); }
        [Data] public byte BreakPower { get => breakPower; set => SetProperty(ref breakPower, value); }
        [Data] public byte BreakMagic { get => breakMagic; set => SetProperty(ref breakMagic, value); }
        [Data] public byte BreakArmor { get => breakArmor; set => SetProperty(ref breakArmor, value); }
        [Data] public byte BreakMental { get => breakMental; set => SetProperty(ref breakMental, value); }
        [Data] public byte Confuse { get => confuse; set => SetProperty(ref confuse, value); }
        [Data] public byte Berserk { get => berserk; set => SetProperty(ref berserk, value); }
        [Data] public byte Provoke { get => provoke; set => SetProperty(ref provoke, value); }
        [Data] public byte Threaten { get => threaten; set => SetProperty(ref threaten, value); }
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

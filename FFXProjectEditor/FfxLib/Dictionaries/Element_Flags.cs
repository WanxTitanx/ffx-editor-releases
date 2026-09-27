using System;

namespace FFXProjectEditor.FfxLib.Dictionaries
{
    [Flags]
    public enum Element_Flags : byte
    {
        Fire = 0x01,
        Blizzard = 0x02,
        Thunder = 0x04,
        Water = 0x08,
        Holy = 0x10,
        /// <summary>Reserved slot — engine applies weak/resist @ <c>FFX_Battle_ApplyElementResist</c> but no vanilla UI.</summary>
        Earth = 0x20,
        /// <summary>Reserved slot — same as <see cref="Earth"/>.</summary>
        Wind = 0x40,
        Dark = 0x80,
    }
}

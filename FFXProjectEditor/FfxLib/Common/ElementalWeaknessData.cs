using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils;
using Xe.BinaryMapper;

namespace FFXProjectEditor.FfxLib.Common
{
    public class ElementalWeaknessData : ObservableObject
    {
        Element_Flags absorb;
        Element_Flags immune;
        Element_Flags resist;
        Element_Flags weak;

        [Data] public Element_Flags Absorb { get => absorb; set => SetProperty(ref absorb, value); }
        [Data] public Element_Flags Immune { get => immune; set => SetProperty(ref immune, value); }
        [Data] public Element_Flags Resist { get => resist; set => SetProperty(ref resist, value); }
        [Data] public Element_Flags Weak { get => weak; set => SetProperty(ref weak, value); }

        public bool AbsorbFire { get => BitFlag_Util.IsFlagSet(Absorb, Element_Flags.Fire); set => SetFlag(ref absorb, Element_Flags.Fire, value, nameof(AbsorbFire), nameof(Absorb)); }
        public bool AbsorbBlizzard { get => BitFlag_Util.IsFlagSet(Absorb, Element_Flags.Blizzard); set => SetFlag(ref absorb, Element_Flags.Blizzard, value, nameof(AbsorbBlizzard), nameof(Absorb)); }
        public bool AbsorbThunder { get => BitFlag_Util.IsFlagSet(Absorb, Element_Flags.Thunder); set => SetFlag(ref absorb, Element_Flags.Thunder, value, nameof(AbsorbThunder), nameof(Absorb)); }
        public bool AbsorbWater { get => BitFlag_Util.IsFlagSet(Absorb, Element_Flags.Water); set => SetFlag(ref absorb, Element_Flags.Water, value, nameof(AbsorbWater), nameof(Absorb)); }
        public bool AbsorbHoly { get => BitFlag_Util.IsFlagSet(Absorb, Element_Flags.Holy); set => SetFlag(ref absorb, Element_Flags.Holy, value, nameof(AbsorbHoly), nameof(Absorb)); }
        public bool AbsorbDark { get => BitFlag_Util.IsFlagSet(Absorb, Element_Flags.Dark); set => SetFlag(ref absorb, Element_Flags.Dark, value, nameof(AbsorbDark), nameof(Absorb)); }

        public bool ImmuneFire { get => BitFlag_Util.IsFlagSet(Immune, Element_Flags.Fire); set => SetFlag(ref immune, Element_Flags.Fire, value, nameof(ImmuneFire), nameof(Immune)); }
        public bool ImmuneBlizzard { get => BitFlag_Util.IsFlagSet(Immune, Element_Flags.Blizzard); set => SetFlag(ref immune, Element_Flags.Blizzard, value, nameof(ImmuneBlizzard), nameof(Immune)); }
        public bool ImmuneThunder { get => BitFlag_Util.IsFlagSet(Immune, Element_Flags.Thunder); set => SetFlag(ref immune, Element_Flags.Thunder, value, nameof(ImmuneThunder), nameof(Immune)); }
        public bool ImmuneWater { get => BitFlag_Util.IsFlagSet(Immune, Element_Flags.Water); set => SetFlag(ref immune, Element_Flags.Water, value, nameof(ImmuneWater), nameof(Immune)); }
        public bool ImmuneHoly { get => BitFlag_Util.IsFlagSet(Immune, Element_Flags.Holy); set => SetFlag(ref immune, Element_Flags.Holy, value, nameof(ImmuneHoly), nameof(Immune)); }
        public bool ImmuneDark { get => BitFlag_Util.IsFlagSet(Immune, Element_Flags.Dark); set => SetFlag(ref immune, Element_Flags.Dark, value, nameof(ImmuneDark), nameof(Immune)); }

        public bool ResistFire { get => BitFlag_Util.IsFlagSet(Resist, Element_Flags.Fire); set => SetFlag(ref resist, Element_Flags.Fire, value, nameof(ResistFire), nameof(Resist)); }
        public bool ResistBlizzard { get => BitFlag_Util.IsFlagSet(Resist, Element_Flags.Blizzard); set => SetFlag(ref resist, Element_Flags.Blizzard, value, nameof(ResistBlizzard), nameof(Resist)); }
        public bool ResistThunder { get => BitFlag_Util.IsFlagSet(Resist, Element_Flags.Thunder); set => SetFlag(ref resist, Element_Flags.Thunder, value, nameof(ResistThunder), nameof(Resist)); }
        public bool ResistWater { get => BitFlag_Util.IsFlagSet(Resist, Element_Flags.Water); set => SetFlag(ref resist, Element_Flags.Water, value, nameof(ResistWater), nameof(Resist)); }
        public bool ResistHoly { get => BitFlag_Util.IsFlagSet(Resist, Element_Flags.Holy); set => SetFlag(ref resist, Element_Flags.Holy, value, nameof(ResistHoly), nameof(Resist)); }
        public bool ResistDark { get => BitFlag_Util.IsFlagSet(Resist, Element_Flags.Dark); set => SetFlag(ref resist, Element_Flags.Dark, value, nameof(ResistDark), nameof(Resist)); }

        public bool WeakFire { get => BitFlag_Util.IsFlagSet(Weak, Element_Flags.Fire); set => SetFlag(ref weak, Element_Flags.Fire, value, nameof(WeakFire), nameof(Weak)); }
        public bool WeakBlizzard { get => BitFlag_Util.IsFlagSet(Weak, Element_Flags.Blizzard); set => SetFlag(ref weak, Element_Flags.Blizzard, value, nameof(WeakBlizzard), nameof(Weak)); }
        public bool WeakThunder { get => BitFlag_Util.IsFlagSet(Weak, Element_Flags.Thunder); set => SetFlag(ref weak, Element_Flags.Thunder, value, nameof(WeakThunder), nameof(Weak)); }
        public bool WeakWater { get => BitFlag_Util.IsFlagSet(Weak, Element_Flags.Water); set => SetFlag(ref weak, Element_Flags.Water, value, nameof(WeakWater), nameof(Weak)); }
        public bool WeakHoly { get => BitFlag_Util.IsFlagSet(Weak, Element_Flags.Holy); set => SetFlag(ref weak, Element_Flags.Holy, value, nameof(WeakHoly), nameof(Weak)); }
        public bool WeakDark { get => BitFlag_Util.IsFlagSet(Weak, Element_Flags.Dark); set => SetFlag(ref weak, Element_Flags.Dark, value, nameof(WeakDark), nameof(Weak)); }

        void SetFlag(ref Element_Flags currentFlags, Element_Flags flag, bool enabled, string propertyName, string aggregatePropertyName)
        {
            Element_Flags updated = BitFlag_Util.SetFlag(currentFlags, flag, enabled);
            if (updated == currentFlags)
                return;

            currentFlags = updated;
            OnPropertyChanged(propertyName);
            OnPropertyChanged(aggregatePropertyName);
        }
    }
}

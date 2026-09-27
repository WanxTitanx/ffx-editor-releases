using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Windows.Input;

namespace FFXProjectEditor.Modules.AutoAbilityEditor
{
    public partial class AbilityTogglePill_Control : UserControl
    {
        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<AbilityTogglePill_Control, bool>(nameof(IsActive));

        public bool IsActive
        {
            get => GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public static readonly StyledProperty<string> LabelProperty =
            AvaloniaProperty.Register<AbilityTogglePill_Control, string>(nameof(Label));

        public string Label
        {
            get => GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly StyledProperty<string> FamilyColorProperty =
            AvaloniaProperty.Register<AbilityTogglePill_Control, string>(nameof(FamilyColor), "AbilitySpecial");

        public string FamilyColor
        {
            get => GetValue(FamilyColorProperty);
            set => SetValue(FamilyColorProperty, value);
        }

        public AbilityTogglePill_Control()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

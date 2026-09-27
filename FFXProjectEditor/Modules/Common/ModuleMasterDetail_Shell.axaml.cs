using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace FFXProjectEditor.Modules.Common;

/// <summary>
/// Shell reutilizável de master-detail (Camada 4 do overhaul de UI).
/// Substitui o Expander nativo por um Grid + Button toggle custom para controle
/// pixel-perfect: colapsado = rail de 26px com chevron; expandido = painel denso
/// de 180px + rail. O módulo fornece MasterHeader/MasterList/Detail.
///
/// Os módulos continuam donos do seu conteúdo (ListBox, DataModel, handlers). O shell só
/// provê o layout externo. Sem mudança em SetModule / NavigationSnapshot.
/// </summary>
public partial class ModuleMasterDetail_Shell : UserControl
{
    public static readonly StyledProperty<Control?> MasterHeaderProperty =
        AvaloniaProperty.Register<ModuleMasterDetail_Shell, Control?>(nameof(MasterHeader));

    public static readonly StyledProperty<Control?> MasterListProperty =
        AvaloniaProperty.Register<ModuleMasterDetail_Shell, Control?>(nameof(MasterList));

    public static readonly StyledProperty<Control?> DetailProperty =
        AvaloniaProperty.Register<ModuleMasterDetail_Shell, Control?>(nameof(Detail));

    public static readonly StyledProperty<string?> MasterHeaderLabelProperty =
        AvaloniaProperty.Register<ModuleMasterDetail_Shell, string?>(nameof(MasterHeaderLabel));

    public static readonly StyledProperty<bool> IsMasterExpandedProperty =
        AvaloniaProperty.Register<ModuleMasterDetail_Shell, bool>(nameof(IsMasterExpanded), defaultValue: true);

    public Control? MasterHeader
    {
        get => GetValue(MasterHeaderProperty);
        set => SetValue(MasterHeaderProperty, value);
    }

    public Control? MasterList
    {
        get => GetValue(MasterListProperty);
        set => SetValue(MasterListProperty, value);
    }

    public Control? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string? MasterHeaderLabel
    {
        get => GetValue(MasterHeaderLabelProperty);
        set => SetValue(MasterHeaderLabelProperty, value);
    }

    public bool IsMasterExpanded
    {
        get => GetValue(IsMasterExpandedProperty);
        set => SetValue(IsMasterExpandedProperty, value);
    }

    public void ToggleMaster_Click(object? sender, RoutedEventArgs e)
    {
        IsMasterExpanded = !IsMasterExpanded;
    }

    public ModuleMasterDetail_Shell()
    {
        InitializeComponent();
    }
}

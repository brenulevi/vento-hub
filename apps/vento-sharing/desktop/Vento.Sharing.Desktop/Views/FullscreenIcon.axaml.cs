using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Vento.Sharing.Desktop.Views;

public enum FullscreenIconKind
{
    Expand,
    Exit
}

public partial class FullscreenIcon : UserControl
{
    public static readonly StyledProperty<FullscreenIconKind> KindProperty =
        AvaloniaProperty.Register<FullscreenIcon, FullscreenIconKind>(nameof(Kind));

    private static readonly Geometry ExpandGeometry = Geometry.Parse(
        "M4,4 H9 M4,4 V9 M20,4 H15 M20,4 V9 M4,20 H9 M4,20 V15 M20,20 H15 M20,20 V15");

    private static readonly Geometry ExitGeometry = Geometry.Parse(
        "M9,9 H4 M9,9 V4 M15,9 H20 M15,9 V4 M9,15 H4 M9,15 V20 M15,15 H20 M15,15 V20");

    public FullscreenIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public FullscreenIcon()
    {
        InitializeComponent();
        KindProperty.Changed.AddClassHandler<FullscreenIcon>((icon, _) => icon.UpdatePath());
        UpdatePath();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty)
            IconPath.Stroke = Foreground;
    }

    private void UpdatePath() => IconPath.Data = Kind == FullscreenIconKind.Expand ? ExpandGeometry : ExitGeometry;
}

using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Vento.Sharing.Desktop;

public sealed record ThemePalette(string Id, string Name, string Background, string Surface,
    string Panel, string Border, string Accent, string AccentSoft, string Text, string Muted);

public static class AppTheme
{
    public static readonly ThemePalette[] Palettes =
    [
        new("aurora", "Aurora", "#12101D", "#201B30", "#2A233D", "#504366", "#B59AFF", "#684F96", "#F7F2FF", "#C5B8D5"),
        new("ocean", "Oceano", "#0B1220", "#121D30", "#172A3B", "#345464", "#83DDE0", "#2D7281", "#F1F5F9", "#AFC0D2"),
        new("forest", "Floresta", "#0E1915", "#16281F", "#1D3428", "#3C6852", "#83D9A7", "#327453", "#F1FAF3", "#B1D1BC"),
        new("sunset", "Pôr do sol", "#1D1216", "#301D25", "#3D2630", "#714959", "#FFA882", "#945C64", "#FFF5F1", "#D9BEB9")
    ];

    public static ThemePalette Find(string? id) =>
        Palettes.FirstOrDefault(palette => palette.Id == id) ?? Palettes[0];

    public static void Apply(string? id)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        var palette = Find(id);
        resources["AppBackground"] = Brush.Parse(palette.Background);
        resources["AppSurface"] = Brush.Parse(palette.Surface);
        resources["AppPanel"] = Brush.Parse(palette.Panel);
        resources["AppBorder"] = Brush.Parse(palette.Border);
        resources["AppAccent"] = Brush.Parse(palette.Accent);
        resources["AppAccentSoft"] = Brush.Parse(palette.AccentSoft);
        resources["AppText"] = Brush.Parse(palette.Text);
        resources["AppMuted"] = Brush.Parse(palette.Muted);
        resources["SystemAccentColor"] = Color.Parse(palette.Accent);
    }
}

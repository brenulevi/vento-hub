using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Vento.Sharing.Desktop;

public sealed record ThemePalette(string Id, string Name, string Rail, string Header, string Background,
    string Surface, string Panel, string Border, string Accent, string AccentSoft, string Text, string Muted);

public static class AppTheme
{
    public static readonly ThemePalette[] Palettes =
    [
        new("graphite", "Discord", "#1E1F22", "#232428", "#313338", "#2B2D31", "#383A40", "#3F4147",
            "#5865F2", "#404249", "#F2F3F5", "#949BA4"),
        new("aurora", "Aurora", "#16141F", "#1F1B2C", "#2A2638", "#221E30", "#322C45", "#443C5C",
            "#9B84F6", "#4A3F68", "#F5F3FF", "#A8A3B8"),
        new("ocean", "Oceano", "#0E141C", "#121A26", "#1A2433", "#141D2A", "#243447", "#2E4558",
            "#3BA4D9", "#1F4F62", "#EFF6FB", "#94A9BC"),
        new("forest", "Floresta", "#101814", "#152019", "#1C2822", "#152019", "#243529", "#355545",
            "#3BA55D", "#264D3A", "#EDF8F1", "#9BBAA8"),
        new("sunset", "Pôr do sol", "#1A1216", "#241820", "#30242A", "#241820", "#3A2C34", "#5C4450",
            "#EB6F92", "#6E4048", "#FFF6F0", "#C4A8A0")
    ];

    public static ThemePalette Find(string? id) =>
        Palettes.FirstOrDefault(palette => palette.Id == id) ?? Palettes[0];

    public static void Apply(string? id)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        var palette = Find(id);
        resources["AppRail"] = Brush.Parse(palette.Rail);
        resources["AppHeader"] = Brush.Parse(palette.Header);
        resources["AppBackground"] = Brush.Parse(palette.Background);
        resources["AppSurface"] = Brush.Parse(palette.Surface);
        resources["AppPanel"] = Brush.Parse(palette.Panel);
        resources["AppBorder"] = Brush.Parse(palette.Border);
        resources["AppAccent"] = Brush.Parse(palette.Accent);
        resources["AppAccentSoft"] = Brush.Parse(palette.AccentSoft);
        resources["AppText"] = Brush.Parse(palette.Text);
        resources["AppMuted"] = Brush.Parse(palette.Muted);
        resources["AppWarning"] = Brush.Parse("#F0B232");
        resources["AppDanger"] = Brush.Parse("#DA373C");
        resources["AppDangerSoft"] = Brush.Parse("#3A2326");
        resources["AppOnline"] = Brush.Parse("#23A559");
        resources["AppLink"] = Brush.Parse(palette.Accent);
        resources["SystemAccentColor"] = Color.Parse(palette.Accent);
    }
}

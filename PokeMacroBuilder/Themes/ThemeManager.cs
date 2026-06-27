using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PokeMacroBuilder.Themes;

/// <summary>
/// 配色ブラシ(VsTheme.xaml の SolidColorBrush)の Color を実行時に書き換えてテーマを切り替える。
/// ブラシ実体は共有参照なので、StaticResource 利用箇所も即座に反映される。
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["EditorBgBrush"] = "#FF1E1E1E",
        ["ShellBgBrush"] = "#FF2D2D30",
        ["PanelBgBrush"] = "#FF252526",
        ["ControlBgBrush"] = "#FF3F3F46",
        ["ControlHoverBrush"] = "#FF505057",
        ["MenuHoverBrush"] = "#FF3E3E42",
        ["BorderBrush"] = "#FF3F3F46",
        ["BorderStrongBrush"] = "#FF54545A",
        ["AccentBrush"] = "#FF007ACC",
        ["PrimaryBrush"] = "#FF0E639C",
        ["PrimaryHoverBrush"] = "#FF1177BB",
        ["TextBrush"] = "#FFF1F1F1",
        ["TextDimBrush"] = "#FF9D9D9D",
    };

    private static readonly Dictionary<string, string> Light = new()
    {
        ["EditorBgBrush"] = "#FFFFFFFF",
        ["ShellBgBrush"] = "#FFEDEDED",
        ["PanelBgBrush"] = "#FFF3F3F3",
        ["ControlBgBrush"] = "#FFFAFAFA",
        ["ControlHoverBrush"] = "#FFE2E6EA",
        ["MenuHoverBrush"] = "#FFD6E4F5",
        ["BorderBrush"] = "#FFCFCFCF",
        ["BorderStrongBrush"] = "#FFB0B0B0",
        ["AccentBrush"] = "#FF007ACC",
        ["PrimaryBrush"] = "#FF0E639C",
        ["PrimaryHoverBrush"] = "#FF1177BB",
        ["TextBrush"] = "#FF1E1E1E",
        ["TextDimBrush"] = "#FF6E6E6E",
    };

    public static void Apply(string? theme)
    {
        var map = theme == "Light" ? Light : Dark;
        var res = Application.Current?.Resources;
        if (res is null) return;

        foreach (var (key, hex) in map)
        {
            if (res[key] is SolidColorBrush b && !b.IsFrozen)
                b.Color = (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}

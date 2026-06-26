using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PokeMacroBuilder.Converters;

/// <summary>
/// 8方向パッドの選択中ボタンを強調する。
/// value = 選択中の方向index, ConverterParameter = そのボタンのindex(文字列)。
/// </summary>
public sealed class DirectionSelectedConverter : IValueConverter
{
    private static readonly Brush Selected = new SolidColorBrush(Color.FromRgb(0x11, 0x77, 0xBB)); // VS blue
    private static readonly Brush Normal = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    static DirectionSelectedConverter()
    {
        Selected.Freeze();
        Normal.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int selected = value is int i ? i : -1;
        int self = parameter is string s && int.TryParse(s, out var p) ? p : -2;
        return selected == self ? Selected : Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

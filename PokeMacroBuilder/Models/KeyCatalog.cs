using System.Collections.Generic;

namespace PokeMacroBuilder.Models;

/// <summary>
/// 1つのボタンを表す。Display=画面表示名, Code=生成するPython式。
/// </summary>
public sealed class KeyItem
{
    public string Display { get; }
    public string Code { get; }

    public KeyItem(string display, string code)
    {
        Display = display;
        Code = code;
    }

    public override string ToString() => Display;
}

/// <summary>
/// Poke-Controller の Button に対応する選択肢一覧(ボタン専用。方向はスティックブロックで扱う)。
/// </summary>
public static class KeyCatalog
{
    public static IReadOnlyList<KeyItem> All { get; } = new List<KeyItem>
    {
        new("A", "Button.A"),
        new("B", "Button.B"),
        new("X", "Button.X"),
        new("Y", "Button.Y"),
        new("L", "Button.L"),
        new("R", "Button.R"),
        new("ZL", "Button.ZL"),
        new("ZR", "Button.ZR"),
        new("－ (MINUS)", "Button.MINUS"),
        new("＋ (PLUS)", "Button.PLUS"),
        new("Lスティック押込", "Button.LCLICK"),
        new("Rスティック押込", "Button.RCLICK"),
        new("HOME", "Button.HOME"),
        new("CAPTURE", "Button.CAPTURE"),
    };

    public static KeyItem Default => All[0];

    public static KeyItem FromCode(string? code)
    {
        if (code is null) return Default;
        foreach (var k in All)
            if (k.Code == code) return k;
        return Default;
    }
}

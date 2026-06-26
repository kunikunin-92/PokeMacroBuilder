using System.Collections.Generic;

namespace PokeMacroBuilder.Models;

/// <summary>
/// 1つの入力キー(ボタン/方向)を表す。Display=画面表示名, Code=生成するPython式。
/// </summary>
public sealed class KeyItem
{
    public string Display { get; }
    public string Code { get; }
    public string Group { get; }

    public KeyItem(string display, string code, string group)
    {
        Display = display;
        Code = code;
        Group = group;
    }

    public override string ToString() => Display;
}

/// <summary>
/// Poke-Controller の Button / Hat に対応する選択肢一覧。
/// </summary>
public static class KeyCatalog
{
    public static IReadOnlyList<KeyItem> All { get; } = new List<KeyItem>
    {
        // --- ボタン ---
        new("A", "Button.A", "ボタン"),
        new("B", "Button.B", "ボタン"),
        new("X", "Button.X", "ボタン"),
        new("Y", "Button.Y", "ボタン"),
        new("L", "Button.L", "ボタン"),
        new("R", "Button.R", "ボタン"),
        new("ZL", "Button.ZL", "ボタン"),
        new("ZR", "Button.ZR", "ボタン"),
        new("－ (MINUS)", "Button.MINUS", "ボタン"),
        new("＋ (PLUS)", "Button.PLUS", "ボタン"),
        new("Lスティック押込", "Button.LCLICK", "ボタン"),
        new("Rスティック押込", "Button.RCLICK", "ボタン"),
        new("HOME", "Button.HOME", "ボタン"),
        new("CAPTURE", "Button.CAPTURE", "ボタン"),
        // --- 十字キー(方向) ---
        new("↑ 上", "Hat.TOP", "方向(十字キー)"),
        new("↗ 右上", "Hat.TOP_RIGHT", "方向(十字キー)"),
        new("→ 右", "Hat.RIGHT", "方向(十字キー)"),
        new("↘ 右下", "Hat.BTM_RIGHT", "方向(十字キー)"),
        new("↓ 下", "Hat.BTM", "方向(十字キー)"),
        new("↙ 左下", "Hat.BTM_LEFT", "方向(十字キー)"),
        new("← 左", "Hat.LEFT", "方向(十字キー)"),
        new("↖ 左上", "Hat.TOP_LEFT", "方向(十字キー)"),
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

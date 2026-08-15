using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PokeMacroBuilder.Services;

/// <summary>
/// 生成済み Python コードを VSCode 風に色付けした FlowDocument に変換する簡易ハイライタ。
/// テーマに合わせて配色を切り替える(暗い配色のままだと Light テーマで文字が読めないため)。
/// </summary>
public static class PythonHighlighter
{
    /// <summary>1テーマ分の配色。</summary>
    private sealed class Palette
    {
        public required Brush Comment { get; init; }
        public required Brush String { get; init; }
        public required Brush Number { get; init; }
        public required Brush Keyword { get; init; }  // 制御系 from/import/while...
        public required Brush Decl { get; init; }     // class/def/self/True...
        public required Brush Type { get; init; }     // Button/Direction...
        public required Brush Func { get; init; }     // 関数呼び出し
        public required Brush Default { get; init; }
    }

    // VSCode Dark+ 配色
    private static readonly Palette DarkPalette = new()
    {
        Comment = Frozen("#6A9955"),
        String = Frozen("#CE9178"),
        Number = Frozen("#B5CEA8"),
        Keyword = Frozen("#C586C0"),
        Decl = Frozen("#569CD6"),
        Type = Frozen("#4EC9B0"),
        Func = Frozen("#DCDCAA"),
        Default = Frozen("#D4D4D4"),
    };

    // VSCode Light+ 配色
    private static readonly Palette LightPalette = new()
    {
        Comment = Frozen("#008000"),
        String = Frozen("#A31515"),
        Number = Frozen("#098658"),
        Keyword = Frozen("#AF00DB"),
        Decl = Frozen("#0000FF"),
        Type = Frozen("#267F99"),
        Func = Frozen("#795E26"),
        Default = Frozen("#1E1E1E"),
    };

    /// <summary>Light テーマ用の配色を使うか(ThemeManager から設定される)。</summary>
    public static bool IsLightTheme { get; set; }

    private static Palette Current => IsLightTheme ? LightPalette : DarkPalette;

    private static readonly HashSet<string> ControlKeywords = new()
    {
        "from", "import", "while", "for", "in", "return", "if", "elif", "else",
        "pass", "with", "as", "lambda", "and", "or", "not", "break", "continue", "try", "except"
    };
    private static readonly HashSet<string> DeclKeywords = new()
    {
        "class", "def", "self", "True", "False", "None", "super"
    };
    private static readonly HashSet<string> TypeNames = new()
    {
        "Button", "Direction", "Hat", "Stick", "Touchscreen",
        "PythonCommand", "ImageProcPythonCommand", "range", "print"
    };

    private static SolidColorBrush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public static FlowDocument BuildDocument(string code)
    {
        var pal = Current;
        var para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 18,
        };

        var lines = (code ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            AppendLine(para, lines[li], pal);
            if (li < lines.Length - 1) para.Inlines.Add(new LineBreak());
        }

        var doc = new FlowDocument(para)
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12.5,
            Foreground = pal.Default,
            PagePadding = new Thickness(2),
            PageWidth = 2000, // 折り返し防止
        };
        return doc;
    }

    private static void AppendLine(Paragraph para, string line, Palette pal)
    {
        int i = 0;
        int n = line.Length;
        while (i < n)
        {
            char c = line[i];

            // コメント
            if (c == '#')
            {
                Add(para, line.Substring(i), pal.Comment);
                return;
            }

            // 文字列
            if (c == '\'' || c == '"')
            {
                int start = i;
                char q = c;
                i++;
                while (i < n)
                {
                    if (line[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (line[i] == q) { i++; break; }
                    i++;
                }
                Add(para, line.Substring(start, i - start), pal.String);
                continue;
            }

            // 数値
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < n && (char.IsDigit(line[i]) || line[i] == '.')) i++;
                Add(para, line.Substring(start, i - start), pal.Number);
                continue;
            }

            // 識別子
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                string word = line.Substring(start, i - start);

                bool followedByParen = i < n && SkipSpaces(line, i) is int j && j < n && line[j] == '(';

                Brush brush;
                if (ControlKeywords.Contains(word)) brush = pal.Keyword;
                else if (DeclKeywords.Contains(word)) brush = pal.Decl;
                else if (TypeNames.Contains(word)) brush = pal.Type;
                else if (followedByParen) brush = pal.Func;
                else brush = pal.Default;

                Add(para, word, brush);
                continue;
            }

            // それ以外(記号・空白)
            {
                int start = i;
                while (i < n)
                {
                    char d = line[i];
                    if (d == '#' || d == '\'' || d == '"' || char.IsLetterOrDigit(d) || d == '_') break;
                    i++;
                }
                Add(para, line.Substring(start, i - start), pal.Default);
            }
        }
    }

    private static int SkipSpaces(string s, int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
        return i;
    }

    private static void Add(Paragraph para, string text, Brush brush)
    {
        if (text.Length == 0) return;
        para.Inlines.Add(new Run(text) { Foreground = brush });
    }
}

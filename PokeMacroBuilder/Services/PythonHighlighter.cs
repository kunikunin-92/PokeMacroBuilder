using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PokeMacroBuilder.Services;

/// <summary>
/// 生成済み Python コードを VSCode(Dark+)風に色付けした FlowDocument に変換する簡易ハイライタ。
/// </summary>
public static class PythonHighlighter
{
    // VSCode Dark+ 配色
    private static readonly Brush ColComment = Frozen("#6A9955");
    private static readonly Brush ColString = Frozen("#CE9178");
    private static readonly Brush ColNumber = Frozen("#B5CEA8");
    private static readonly Brush ColKeyword = Frozen("#C586C0"); // 制御系 from/import/while...
    private static readonly Brush ColDecl = Frozen("#569CD6");    // class/def/self/True...
    private static readonly Brush ColType = Frozen("#4EC9B0");    // Button/Direction...
    private static readonly Brush ColFunc = Frozen("#DCDCAA");    // 関数呼び出し
    private static readonly Brush ColDefault = Frozen("#D4D4D4");

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
        var para = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 18,
        };

        var lines = (code ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            AppendLine(para, lines[li]);
            if (li < lines.Length - 1) para.Inlines.Add(new LineBreak());
        }

        var doc = new FlowDocument(para)
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12.5,
            Foreground = ColDefault,
            PagePadding = new Thickness(2),
            PageWidth = 2000, // 折り返し防止
        };
        return doc;
    }

    private static void AppendLine(Paragraph para, string line)
    {
        int i = 0;
        int n = line.Length;
        while (i < n)
        {
            char c = line[i];

            // コメント
            if (c == '#')
            {
                Add(para, line.Substring(i), ColComment);
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
                Add(para, line.Substring(start, i - start), ColString);
                continue;
            }

            // 数値
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < n && (char.IsDigit(line[i]) || line[i] == '.')) i++;
                Add(para, line.Substring(start, i - start), ColNumber);
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
                if (ControlKeywords.Contains(word)) brush = ColKeyword;
                else if (DeclKeywords.Contains(word)) brush = ColDecl;
                else if (TypeNames.Contains(word)) brush = ColType;
                else if (followedByParen) brush = ColFunc;
                else brush = ColDefault;

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
                Add(para, line.Substring(start, i - start), ColDefault);
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

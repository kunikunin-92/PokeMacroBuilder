using System.Globalization;
using System.Text;
using PokeMacroBuilder.Models;

namespace PokeMacroBuilder.Services;

/// <summary>
/// MacroDocument から Poke-Controller 用の PythonCommand(.py)を生成する。
/// </summary>
public static class PythonGenerator
{
    private static string Num(double v) =>
        v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>例: "macro1.py" -> "Macro1"。Python のクラス名として有効化する。</summary>
    public static string ClassNameFromFile(string fileName)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var sb = new StringBuilder();
        bool upperNext = true;
        foreach (var ch in stem)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }
        var name = sb.ToString();
        if (name.Length == 0 || char.IsDigit(name[0])) name = "Macro" + name;
        return name;
    }

    public static string Generate(MacroDocument doc, string fileName)
    {
        var className = ClassNameFromFile(fileName);
        var sb = new StringBuilder();

        sb.AppendLine("#!/usr/bin/env python3");
        sb.AppendLine("# -*- coding: utf-8 -*-");
        sb.AppendLine("#");
        sb.AppendLine(MacroSerializer.SignatureComment);
        sb.AppendLine("# このファイルは PokeMacroBuilder で作成・編集できます。");
        sb.AppendLine("# 下記の行は編集情報です。手動で書き換えないでください。");
        sb.AppendLine(MacroSerializer.Marker + MacroSerializer.ToBase64(doc));
        sb.AppendLine();
        sb.AppendLine("from Commands.PythonCommandBase import PythonCommand");
        sb.AppendLine("from Commands.Keys import Button, Direction, Hat");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"class {className}(PythonCommand):");
        sb.AppendLine($"    NAME = '{EscapeSingleQuote(doc.DisplayName)}'");
        sb.AppendLine();
        sb.AppendLine("    def __init__(self):");
        sb.AppendLine("        super().__init__()");
        sb.AppendLine();
        sb.AppendLine("    def do(self):");

        // ループ設定に応じたインデント
        string bodyIndent;
        switch (doc.Loop)
        {
            case LoopMode.Infinite:
                sb.AppendLine("        while True:");
                bodyIndent = "            ";
                break;
            case LoopMode.Count:
                var n = doc.LoopCount < 1 ? 1 : doc.LoopCount;
                sb.AppendLine($"        for _ in range({n}):");
                bodyIndent = "            ";
                break;
            default:
                bodyIndent = "        ";
                break;
        }

        var bodyLines = new System.Collections.Generic.List<string>();
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case PressBlock p:
                    bodyLines.Add(EmitPress(p));
                    break;
                case WaitBlock w:
                    bodyLines.Add($"self.wait({Num(w.Seconds)})");
                    break;
            }
        }

        if (bodyLines.Count == 0)
        {
            sb.AppendLine(bodyIndent + "pass");
        }
        else
        {
            foreach (var line in bodyLines)
                sb.AppendLine(bodyIndent + line);
        }

        return sb.ToString();
    }

    private static string EmitPress(PressBlock p)
    {
        var codes = new System.Collections.Generic.List<string>();
        foreach (var slot in p.Keys) codes.Add(slot.SelectedKey.Code);

        string target = codes.Count == 1
            ? codes[0]
            : "[" + string.Join(", ", codes) + "]";

        return $"self.press({target}, duration={Num(p.Duration)}, wait={Num(p.Wait)})";
    }

    private static string EscapeSingleQuote(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'");
}

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PokeMacroBuilder.Models;

namespace PokeMacroBuilder.Services;

/// <summary>
/// MacroDocument から Poke-Controller 用の PythonCommand(.py)を生成する。
/// </summary>
public static class PythonGenerator
{
    private static readonly string[] CmpOps = { "==", "!=", "<", "<=", ">", ">=" };
    private static readonly string[] AssignOps = { "=", "+=", "-=", "*=", "/=" };

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string ClassNameFromFile(string fileName)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var sb = new StringBuilder();
        bool upperNext = true;
        foreach (var ch in stem)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch); upperNext = false; }
            else upperNext = true;
        }
        var name = sb.ToString();
        if (name.Length == 0 || char.IsDigit(name[0])) name = "Macro" + name;
        return name;
    }

    public static string Generate(MacroDocument doc, string fileName)
    {
        var className = ClassNameFromFile(fileName);
        bool requiresCam = AllBlocks(doc.Blocks).Any(b =>
            b is ScreenshotBlock
            || (b is NotifyBlock nb && nb.AttachScreenshot)
            || (b is IfBlock ib && (ib.Condition.Kind == 1 || ib.ElseIfs.Any(br => br.Condition.Kind == 1)))
            || (b is LoopBlock lb && lb.LoopKind == 2 && lb.Condition.Kind == 1));
        string baseClass = requiresCam ? "ImageProcPythonCommand" : "PythonCommand";

        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env python3");
        sb.AppendLine("# -*- coding: utf-8 -*-");
        sb.AppendLine("#");
        sb.AppendLine(MacroSerializer.SignatureComment);
        sb.AppendLine("# このファイルは PokeMacroBuilder で作成・編集できます。");
        sb.AppendLine("# 下記の行は編集情報です。手動で書き換えないでください。");
        sb.AppendLine(MacroSerializer.Marker + MacroSerializer.ToBase64(doc));
        sb.AppendLine();
        sb.AppendLine($"from Commands.PythonCommandBase import {baseClass}");
        sb.AppendLine("from Commands.Keys import Button, Direction, Hat, Stick");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"class {className}({baseClass}):");
        sb.AppendLine($"    NAME = '{EscapeQuote(doc.DisplayName)}'");
        sb.AppendLine();

        // __init__ (画像系はカメラ引数が必要)
        if (requiresCam)
        {
            sb.AppendLine("    def __init__(self, cam, gui=None):");
            sb.AppendLine("        super().__init__(cam, gui)");
        }
        else
        {
            sb.AppendLine("    def __init__(self):");
            sb.AppendLine("        super().__init__()");
        }
        // 使用している変数を初期化
        var vars = CollectVariables(doc.Blocks);
        foreach (var v in vars) sb.AppendLine($"        self.{v} = 0");
        sb.AppendLine();

        sb.AppendLine("    def do(self):");

        string baseIndent;
        switch (doc.Loop)
        {
            case LoopMode.Infinite:
                sb.AppendLine("        while True:");
                baseIndent = "            ";
                break;
            case LoopMode.Count:
                var n = doc.LoopCount < 1 ? 1 : doc.LoopCount;
                sb.AppendLine($"        for _ in range({n}):");
                baseIndent = "            ";
                break;
            default:
                baseIndent = "        ";
                break;
        }

        var lines = new List<(int rel, string code)>();
        EmitBlocks(doc.Blocks, 0, lines);
        if (lines.Count == 0)
        {
            sb.AppendLine(baseIndent + "pass");
        }
        else
        {
            foreach (var (rel, code) in lines)
                sb.AppendLine(baseIndent + new string(' ', 4 * rel) + code);
        }

        return sb.ToString();
    }

    // ---- ブロック列を再帰的に出力 ----
    private static void EmitBlocks(IEnumerable<MacroBlock> blocks, int rel, List<(int, string)> outp)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case PressBlock p:
                    outp.Add((rel, EmitPress(p)));
                    break;
                case StickBlock s:
                    outp.Add((rel, EmitStick(s)));
                    break;
                case WaitBlock w:
                    outp.Add((rel, $"self.wait({Num(w.Seconds)})"));
                    break;
                case VariableBlock v:
                    outp.Add((rel, EmitVariable(v)));
                    break;
                case LogBlock l:
                    outp.Add((rel, $"print('{EscapeQuote(l.Text)}')"));
                    break;
                case NotifyBlock no:
                    outp.Add((rel, EmitNotify(no)));
                    break;
                case ScreenshotBlock sc:
                    outp.Add((rel, string.IsNullOrWhiteSpace(sc.Name)
                        ? "self.saveCapture()"
                        : $"self.saveCapture('{EscapeQuote(sc.Name)}')"));
                    break;
                case LoopBlock lp:
                    outp.Add((rel, lp.LoopKind switch
                    {
                        0 => "while True:",
                        2 => $"while {Cond(lp.Condition)}:",
                        _ => $"for _ in range({(lp.Count < 0 ? 0 : lp.Count)}):"
                    }));
                    EmitBody(lp.Children, rel + 1, outp);
                    break;
                case IfBlock ib:
                    outp.Add((rel, $"if {Cond(ib.Condition)}:"));
                    EmitBody(ib.Children, rel + 1, outp);
                    foreach (var br in ib.ElseIfs)
                    {
                        outp.Add((rel, $"elif {Cond(br.Condition)}:"));
                        EmitBody(br.Children, rel + 1, outp);
                    }
                    if (ib.HasElse)
                    {
                        outp.Add((rel, "else:"));
                        EmitBody(ib.ElseChildren, rel + 1, outp);
                    }
                    break;
            }
        }
    }

    private static void EmitBody(IEnumerable<MacroBlock> children, int rel, List<(int, string)> outp)
    {
        int before = outp.Count;
        EmitBlocks(children, rel, outp);
        if (outp.Count == before) outp.Add((rel, "pass"));
    }

    private static string EmitPress(PressBlock p)
    {
        var codes = p.Keys.Select(k => k.SelectedKey.Code).ToList();
        string target = codes.Count == 1 ? codes[0] : "[" + string.Join(", ", codes) + "]";
        return p.Mode switch
        {
            1 => $"self.hold({target}, wait={Num(p.Wait)})",   // 押し続ける
            2 => $"self.holdEnd({target})",                     // 離す
            _ => p.Repeat > 1
                ? $"self.pressRep({target}, {p.Repeat}, duration={Num(p.Duration)}, interval={Num(p.Wait)})"
                : $"self.press({target}, duration={Num(p.Duration)}, wait={Num(p.Wait)})",
        };
    }

    private static string EmitVariable(VariableBlock v)
    {
        var name = SanitizeVar(v.Name);
        return v.Op switch
        {
            5 => $"self.{name} += 1",   // ++
            6 => $"self.{name} -= 1",   // --
            _ => $"self.{name} {AssignOps[v.Op is >= 0 and < 5 ? v.Op : 0]} {ValueLiteral(v.Value)}",
        };
    }

    private static string EmitStick(StickBlock s)
    {
        int idx = s.Direction is >= 0 and < 8 ? s.Direction : 0;
        string target;
        if (s.Device == 0)
        {
            target = StickMaps.HatCodes[idx];
        }
        else
        {
            bool right = s.Device == 2;
            if (s.Magnitude >= 100)
                target = right ? StickMaps.RStickConst[idx] : StickMaps.LStickConst[idx];
            else
            {
                var stick = right ? "Stick.RIGHT" : "Stick.LEFT";
                var mag = (s.Magnitude < 1 ? 1 : s.Magnitude) / 100.0;
                target = $"Direction({stick}, {StickMaps.Angles[idx]}, {Num(mag)})";
            }
        }
        return $"self.press({target}, duration={Num(s.Duration)}, wait={Num(s.Wait)})";
    }

    private static readonly System.Text.RegularExpressions.Regex Placeholder =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}");

    /// <summary>本文中の {name} を {self.name} に変換し、必要なら f文字列にする。</summary>
    private static string MessageLiteral(string text)
    {
        text ??= "";
        bool hasVar = Placeholder.IsMatch(text);
        var body = Placeholder.Replace(text, m => "{self." + SanitizeVar(m.Groups[1].Value) + "}");
        body = body.Replace("\\", "\\\\").Replace("'", "\\'");
        return hasVar ? $"f'{body}'" : $"'{body}'";
    }

    private static string EmitNotify(NotifyBlock n)
    {
        var content = MessageLiteral(n.Text);
        return n.AttachScreenshot
            ? $"self.discord_image(content={content})"
            : $"self.discord_text({content})";
    }

    private static string Cond(Condition c)
    {
        if (c.Kind == 1)
        {
            var th = c.Threshold is > 0 and <= 1 ? c.Threshold : 0.8;
            return $"self.isContainTemplate('{EscapeQuote(c.ImageRef)}', threshold={Num(th)})";
        }
        int op = c.Op is >= 0 and < 6 ? c.Op : 0;
        return $"self.{SanitizeVar(c.Var)} {CmpOps[op]} {ValueLiteral(c.Value)}";
    }

    /// <summary>数値ならそのまま、そうでなければ文字列リテラルにする。</summary>
    private static string ValueLiteral(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return "0";
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return raw;
        if (raw is "True" or "False" or "None") return raw;
        return $"'{EscapeQuote(raw)}'";
    }

    private static string SanitizeVar(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "flag";
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
        var s = sb.ToString();
        if (s.Length == 0) return "flag";
        if (char.IsDigit(s[0])) s = "v" + s;
        return s;
    }

    private static IEnumerable<string> CollectVariables(IEnumerable<MacroBlock> blocks)
    {
        var set = new List<string>();
        void Add(string s) { if (!set.Contains(s)) set.Add(s); }
        foreach (var b in AllBlocks(blocks))
        {
            switch (b)
            {
                case VariableBlock v: Add(SanitizeVar(v.Name)); break;
                case LoopBlock l when l.LoopKind == 2 && l.Condition.Kind == 0: Add(SanitizeVar(l.Condition.Var)); break;
                case IfBlock i:
                    if (i.Condition.Kind == 0) Add(SanitizeVar(i.Condition.Var));
                    foreach (var br in i.ElseIfs) if (br.Condition.Kind == 0) Add(SanitizeVar(br.Condition.Var));
                    break;
                case NotifyBlock n:
                    foreach (System.Text.RegularExpressions.Match m in Placeholder.Matches(n.Text ?? ""))
                        Add(SanitizeVar(m.Groups[1].Value));
                    break;
            }
        }
        return set;
    }

    /// <summary>入れ子も含む全ブロックを列挙する。</summary>
    public static IEnumerable<MacroBlock> AllBlocks(IEnumerable<MacroBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            if (b is IfBlock ib)
            {
                foreach (var c in AllBlocks(ib.Children)) yield return c;
                foreach (var br in ib.ElseIfs)
                    foreach (var c in AllBlocks(br.Children)) yield return c;
                foreach (var c in AllBlocks(ib.ElseChildren)) yield return c;
            }
            else if (b is ContainerBlock cb)
            {
                foreach (var c in AllBlocks(cb.Children)) yield return c;
            }
        }
    }

    private static string EscapeQuote(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
}

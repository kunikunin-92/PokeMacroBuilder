using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PokeMacroBuilder.Models;

namespace PokeMacroBuilder.Services;

/// <summary>
/// ワークスペース(pokeconディレクトリ)内の PythonCommands を解決し、
/// 本ツールが作成したマクロの一覧取得・保存・削除を行う。
/// </summary>
public sealed class MacroStore
{
    /// <summary>本ツールが生成物を保存するサブフォルダ名。</summary>
    public const string GeneratedFolderName = "MacroBuilder";

    public string Workspace { get; }
    public string PythonCommandsDir { get; }
    public string GeneratedDir { get; }

    private MacroStore(string workspace, string pythonCommandsDir)
    {
        Workspace = workspace;
        PythonCommandsDir = pythonCommandsDir;
        GeneratedDir = Path.Combine(pythonCommandsDir, GeneratedFolderName);
    }

    /// <summary>
    /// 選択フォルダから PythonCommands ディレクトリを探して MacroStore を生成する。
    /// 見つからなければ null。
    /// </summary>
    public static MacroStore? TryCreate(string workspace)
    {
        var dir = ResolvePythonCommandsDir(workspace);
        return dir is null ? null : new MacroStore(workspace, dir);
    }

    public static string? ResolvePythonCommandsDir(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            return null;

        // 1) 代表的な相対パスを優先的にチェック
        string[] candidates =
        {
            Path.Combine(workspace, "SerialController", "Commands", "PythonCommands"),
            Path.Combine(workspace, "Commands", "PythonCommands"),
            Path.Combine(workspace, "PythonCommands"),
        };
        foreach (var c in candidates)
            if (Directory.Exists(c)) return c;

        // 2) 選択フォルダ自身が PythonCommands
        if (string.Equals(new DirectoryInfo(workspace).Name, "PythonCommands", StringComparison.OrdinalIgnoreCase))
            return workspace;

        // 3) 配下を再帰探索(深すぎる探索は避け、最初の1件)
        try
        {
            var found = Directory.EnumerateDirectories(workspace, "PythonCommands", SearchOption.AllDirectories)
                                 .FirstOrDefault();
            if (found != null) return found;
        }
        catch
        {
            // アクセス不可などは無視
        }
        return null;
    }

    public void EnsureGeneratedDir() => Directory.CreateDirectory(GeneratedDir);

    /// <summary>本ツールで作成したマクロを一覧取得する。</summary>
    public List<MacroEntry> LoadAll()
    {
        var list = new List<MacroEntry>();
        if (!Directory.Exists(GeneratedDir)) return list;

        foreach (var path in Directory.EnumerateFiles(GeneratedDir, "*.py"))
        {
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch { continue; }

            var doc = MacroSerializer.TryParse(text);
            if (doc is null) continue; // 本ツール製でないものはスキップ

            doc.FileName = Path.GetFileName(path);
            doc.FilePath = path;
            list.Add(new MacroEntry(doc.DisplayName, doc.FileName!, path, doc));
        }

        return list.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>次に使う未使用のファイル名 (macro1.py, macro2.py, ...) を返す。</summary>
    public string NextFileName()
    {
        EnsureGeneratedDir();
        int max = 0;
        var rx = new Regex(@"^macro(\d+)\.py$", RegexOptions.IgnoreCase);
        foreach (var path in Directory.EnumerateFiles(GeneratedDir, "*.py"))
        {
            var m = rx.Match(Path.GetFileName(path));
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
                max = Math.Max(max, n);
        }
        return $"macro{max + 1}.py";
    }

    /// <summary>マクロを保存し、保存先パスを doc に反映する。</summary>
    public void Save(MacroDocument doc)
    {
        EnsureGeneratedDir();
        if (string.IsNullOrEmpty(doc.FileName))
            doc.FileName = NextFileName();

        var path = Path.Combine(GeneratedDir, doc.FileName);
        var code = PythonGenerator.Generate(doc, doc.FileName!);
        File.WriteAllText(path, code, new UTF8Encoding(false));
        doc.FilePath = path;
    }

    public void Delete(MacroEntry entry)
    {
        if (File.Exists(entry.FilePath))
            File.Delete(entry.FilePath);
    }
}

public sealed class MacroEntry
{
    public string DisplayName { get; }
    public string FileName { get; }
    public string FilePath { get; }
    public MacroDocument Document { get; }

    public MacroEntry(string displayName, string fileName, string filePath, MacroDocument document)
    {
        DisplayName = displayName;
        FileName = fileName;
        FilePath = filePath;
        Document = document;
    }

    // 一覧表示用
    public string SubText => FileName;
}

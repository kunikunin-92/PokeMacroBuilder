using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
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
            doc.SavedSnapshot = MacroSerializer.ToBase64(doc);
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
        doc.SavedSnapshot = MacroSerializer.ToBase64(doc);
    }

    public void Delete(MacroEntry entry)
    {
        if (File.Exists(entry.FilePath))
            File.Delete(entry.FilePath);
    }

    // ============================================================
    //  テンプレ画像 (Template/<macroStem>/ に配置)
    // ============================================================
    /// <summary>SerialController/Template フォルダ。</summary>
    public string TemplateDir =>
        Path.GetFullPath(Path.Combine(PythonCommandsDir, "..", "..", "Template"));

    /// <summary>SerialController/Captures フォルダ(スクショ保存先)。</summary>
    public string CapturesDir =>
        Path.GetFullPath(Path.Combine(PythonCommandsDir, "..", "..", "Captures"));

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp" };

    /// <summary>マクロの画像フォルダ名(ファイル名の拡張子なし)。未保存なら null。</summary>
    public string? MacroStem(MacroDocument doc) =>
        doc.FileName is null ? null : Path.GetFileNameWithoutExtension(doc.FileName);

    public string? MacroTemplateDir(MacroDocument doc)
    {
        var stem = MacroStem(doc);
        return stem is null ? null : Path.Combine(TemplateDir, stem);
    }

    /// <summary>マクロのテンプレ画像一覧。</summary>
    public List<TemplateImage> ListImages(MacroDocument doc)
    {
        var list = new List<TemplateImage>();
        var stem = MacroStem(doc);
        var dir = MacroTemplateDir(doc);
        if (stem is null || dir is null || !Directory.Exists(dir)) return list;

        foreach (var f in Directory.EnumerateFiles(dir)
                     .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(f);
            list.Add(new TemplateImage(name, f, $"{stem}/{name}"));
        }
        return list;
    }

    /// <summary>クロップ済み画像を Template/&lt;stem&gt;/imgN.png として保存する。</summary>
    public TemplateImage AddImage(MacroDocument doc, BitmapSource image)
    {
        var stem = MacroStem(doc) ?? throw new InvalidOperationException("先にマクロを保存してください。");
        var dir = MacroTemplateDir(doc)!;
        Directory.CreateDirectory(dir);

        int max = 0;
        var rx = new Regex(@"^img(\d+)\.png$", RegexOptions.IgnoreCase);
        foreach (var f in Directory.EnumerateFiles(dir, "*.png"))
        {
            var m = rx.Match(Path.GetFileName(f));
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) max = Math.Max(max, n);
        }
        var name = $"img{max + 1}.png";
        var path = Path.Combine(dir, name);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var fs = File.Create(path)) encoder.Save(fs);

        return new TemplateImage(name, path, $"{stem}/{name}");
    }

    public void DeleteImage(MacroDocument doc, TemplateImage img)
    {
        if (File.Exists(img.FullPath)) File.Delete(img.FullPath);
    }

    /// <summary>
    /// テンプレ画像フォルダを別のマクロへ複製する(コピー保存用)。
    /// 複製先の参照は "&lt;新stem&gt;/&lt;同じファイル名&gt;" になる。
    /// </summary>
    public void CopyImages(MacroDocument from, MacroDocument to)
    {
        var src = MacroTemplateDir(from);
        var dst = MacroTemplateDir(to);
        if (src is null || dst is null || !Directory.Exists(src)) return;

        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)
                     .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }
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

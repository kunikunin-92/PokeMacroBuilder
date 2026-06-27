using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PokeMacroBuilder.Services;

/// <summary>前回のワークスペースなどを %AppData%\PokeMacroBuilder\settings.json に保存。</summary>
public sealed class AppSettings
{
    public string? LastWorkspace { get; set; }

    /// <summary>tesseract.exe の絶対パス(OCR用)。</summary>
    public string? TesseractPath { get; set; }

    /// <summary>テーマ名 ("Dark" / "Light")。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>最近開いたマクロのファイルパス(新しい順)。</summary>
    public List<string> RecentFiles { get; set; } = new();

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8) RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
    }

    private static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PokeMacroBuilder");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // 破損時は既定値
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 保存失敗は致命的でないため無視
        }
    }
}

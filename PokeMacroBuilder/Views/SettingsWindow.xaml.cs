using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PokeMacroBuilder.Views;

public partial class SettingsWindow : Window
{
    public string? TesseractPath { get; private set; }
    public string ThemeName { get; private set; } = "Dark";

    public SettingsWindow(string? tesseractPath, string theme)
    {
        InitializeComponent();
        PathBox.Text = tesseractPath ?? "";
        ThemeBox.SelectedIndex = theme == "Light" ? 1 : 0;
        UpdateStatus();
    }

    /// <summary>パスが有効(tesseract.exe が存在)か。</summary>
    public static bool IsValid(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    private void PathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        if (PathStatus is null) return;
        var p = PathBox.Text;
        if (string.IsNullOrWhiteSpace(p))
        {
            PathStatus.Text = "未設定です（文字認識(OCR)は使用できません）";
            PathStatus.Foreground = (Brush)FindResource("TextDimBrush");
        }
        else if (IsValid(p))
        {
            PathStatus.Text = "✓ OK : tesseract.exe を確認しました";
            PathStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        }
        else
        {
            PathStatus.Text = "✕ tesseract.exe が見つかりません（パスを確認してください）";
            PathStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x4B));
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "tesseract.exe を選択",
            Filter = "tesseract.exe|tesseract.exe|実行ファイル|*.exe|すべて|*.*",
        };
        try
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var guess = Path.Combine(pf, "Tesseract-OCR");
            if (Directory.Exists(guess)) dlg.InitialDirectory = guess;
        }
        catch { }

        if (dlg.ShowDialog(this) == true)
        {
            PathBox.Text = dlg.FileName;
            UpdateStatus();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        TesseractPath = string.IsNullOrWhiteSpace(PathBox.Text) ? null : PathBox.Text.Trim();
        ThemeName = ThemeBox.SelectedIndex == 1 ? "Light" : "Dark";
        DialogResult = true;
    }
}

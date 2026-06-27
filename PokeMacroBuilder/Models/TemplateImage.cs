using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PokeMacroBuilder.Models;

/// <summary>テンプレ画像フィールドの1項目。</summary>
public sealed class TemplateImage
{
    public string FileName { get; }   // 例: img1.png
    public string FullPath { get; }   // 絶対パス
    public string RelRef { get; }     // 例: macro1/img1.png (isContainTemplate 用)

    /// <summary>サムネイル(ファイルをロックしないよう OnLoad で全読み込みして凍結)。</summary>
    public ImageSource? Thumbnail { get; }

    public TemplateImage(string fileName, string fullPath, string relRef)
    {
        FileName = fileName;
        FullPath = fullPath;
        RelRef = relRef;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;          // 読み込み後にファイルを離す
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 256;                          // サムネ用に縮小デコード
            bmp.UriSource = new Uri(fullPath);
            bmp.EndInit();
            bmp.Freeze();
            Thumbnail = bmp;
        }
        catch
        {
            Thumbnail = null;
        }
    }

    public override string ToString() => RelRef;
}

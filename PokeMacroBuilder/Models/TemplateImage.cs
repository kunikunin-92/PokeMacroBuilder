namespace PokeMacroBuilder.Models;

/// <summary>テンプレ画像フィールドの1項目。</summary>
public sealed class TemplateImage
{
    public string FileName { get; }   // 例: img1.png
    public string FullPath { get; }   // 絶対パス(サムネ表示用)
    public string RelRef { get; }     // 例: macro1/img1.png (isContainTemplate 用)

    public TemplateImage(string fileName, string fullPath, string relRef)
    {
        FileName = fileName;
        FullPath = fullPath;
        RelRef = relRef;
    }

    public override string ToString() => RelRef;
}

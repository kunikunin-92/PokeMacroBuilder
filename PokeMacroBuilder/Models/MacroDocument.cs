using System.Collections.ObjectModel;

namespace PokeMacroBuilder.Models;

public enum LoopMode
{
    None,      // ループなし(1回だけ)
    Infinite,  // while True で無限ループ
    Count      // 指定回数だけ繰り返す
}

/// <summary>
/// 1つのマクロ全体。表示名・ファイル名・ブロック列・ループ設定を保持する。
/// </summary>
public sealed class MacroDocument
{
    /// <summary>作成時に入力する「表示名」。Python の NAME 属性になる。</summary>
    public string DisplayName { get; set; } = "新しいマクロ";

    /// <summary>実ファイル名(例: macro1.py)。新規の場合は保存時に決定。</summary>
    public string? FileName { get; set; }

    /// <summary>保存済みファイルの絶対パス(未保存なら null)。</summary>
    public string? FilePath { get; set; }

    public LoopMode Loop { get; set; } = LoopMode.None;

    public int LoopCount { get; set; } = 1;

    public ObservableCollection<MacroBlock> Blocks { get; } = new();

    public bool IsSaved => FilePath != null;
}

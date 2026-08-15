using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PokeMacroBuilder.Models;

public enum LoopMode
{
    None,      // ループなし(1回だけ)
    Infinite,  // while True で無限ループ
    Count      // 指定回数だけ繰り返す
}

/// <summary>
/// 1つのマクロ全体。表示名・ファイル名・ブロック列・ループ設定を保持する。
/// タブ表示のため、表示名・ファイル名・アクティブ状態は監視可能。
/// </summary>
public sealed class MacroDocument : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private string _displayName = "新しいマクロ";
    /// <summary>作成時に入力する「表示名」。Python の NAME 属性になる。</summary>
    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnChanged(); } }
    }

    private string? _fileName;
    /// <summary>実ファイル名(例: macro1.py)。新規の場合は保存時に決定。</summary>
    public string? FileName
    {
        get => _fileName;
        set { if (_fileName != value) { _fileName = value; OnChanged(); OnChanged(nameof(IsSaved)); } }
    }

    /// <summary>保存済みファイルの絶対パス(未保存なら null)。</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 最後に保存/読み込みした時点の内容(MacroSerializer のBase64)。
    /// 現在の内容と比較して「未保存の変更があるか」を判定する。未保存の新規なら null。
    /// </summary>
    public string? SavedSnapshot { get; set; }

    public LoopMode Loop { get; set; } = LoopMode.None;

    public int LoopCount { get; set; } = 1;

    public ObservableCollection<MacroBlock> Blocks { get; } = new();

    public bool IsSaved => FilePath != null;

    private bool _isActive;
    /// <summary>タブとしてアクティブかどうか。</summary>
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnChanged(); } }
    }
}

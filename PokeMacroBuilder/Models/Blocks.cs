using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PokeMacroBuilder.Models;

public abstract class MacroBlock : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    /// <summary>パレット/一覧で表示する種別名。</summary>
    public abstract string Kind { get; }
}

/// <summary>子ブロックを抱える制御フロー系ブロックの基底。</summary>
public abstract class ContainerBlock : MacroBlock
{
    public ObservableCollection<MacroBlock> Children { get; } = new();
}

/// <summary>条件分岐の「でなければもし(elif)」1分岐。</summary>
public sealed class ElseIfBranch
{
    public Condition Condition { get; } = new();
    public ObservableCollection<MacroBlock> Children { get; } = new();
}

// ============================================================
//  条件(if / while で使用)
// ============================================================
public sealed class Condition : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void On([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));

    private string _var = "cnt";
    public string Var { get => _var; set { if (_var != value) { _var = value; On(); } } }

    /// <summary>0:== 1:!= 2:&lt; 3:&lt;= 4:&gt; 5:&gt;=</summary>
    private int _op;
    public int Op { get => _op; set { if (_op != value) { _op = value; On(); } } }

    private string _value = "0";
    public string Value { get => _value; set { if (_value != value) { _value = value; On(); } } }
}

// ============================================================
//  入力系
// ============================================================
public sealed class KeySlot : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private KeyItem _selectedKey = KeyCatalog.Default;
    public KeyItem SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (Equals(_selectedKey, value) || value is null) return;
            _selectedKey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedKey)));
        }
    }
}

/// <summary>
/// ボタン入力。動作モードで「押す(連打可)」「押し続ける」「離す」を切り替える。
/// キーが複数なら同時押し。
/// </summary>
public sealed class PressBlock : MacroBlock
{
    public override string Kind => "ボタン";

    /// <summary>0:押す 1:押し続ける(hold) 2:離す(holdEnd)</summary>
    private int _mode;
    public int Mode { get => _mode; set => Set(ref _mode, value); }

    public ObservableCollection<KeySlot> Keys { get; } = new();

    private double _duration = 0.1;
    public double Duration { get => _duration; set => Set(ref _duration, value); }

    private double _wait = 0.1;
    public double Wait { get => _wait; set => Set(ref _wait, value); }

    private int _repeat = 1;
    public int Repeat { get => _repeat; set => Set(ref _repeat, value); }

    public PressBlock() => Keys.Add(new KeySlot());
    public void AddKey() => Keys.Add(new KeySlot());
    public void RemoveKey(KeySlot slot) { if (Keys.Count > 1) Keys.Remove(slot); }
}

/// <summary>スティック / 方向キーを倒す(8方向)。</summary>
public sealed class StickBlock : MacroBlock
{
    public override string Kind => "スティック";

    private int _device = 1;     // 0=Hat,1=L,2=R
    public int Device { get => _device; set => Set(ref _device, value); }

    private int _direction;      // 0..7
    public int Direction { get => _direction; set => Set(ref _direction, value); }

    private double _magnitude = 100;
    public double Magnitude { get => _magnitude; set => Set(ref _magnitude, value); }

    private double _duration = 0.5;
    public double Duration { get => _duration; set => Set(ref _duration, value); }

    private double _wait = 0.1;
    public double Wait { get => _wait; set => Set(ref _wait, value); }
}

/// <summary>指定秒数だけ待機する。</summary>
public sealed class WaitBlock : MacroBlock
{
    public override string Kind => "待機";

    private double _seconds = 1.0;
    public double Seconds { get => _seconds; set => Set(ref _seconds, value); }
}

// ============================================================
//  制御フロー系(コンテナ)
// ============================================================
/// <summary>ループ。種類で「ずっと/回数/条件の間」を切り替える。</summary>
public sealed class LoopBlock : ContainerBlock
{
    public override string Kind => "ループ";

    /// <summary>0:ずっと 1:回数 2:条件の間</summary>
    private int _loopKind = 1;
    public int LoopKind { get => _loopKind; set => Set(ref _loopKind, value); }

    private int _count = 10;
    public int Count { get => _count; set => Set(ref _count, value); }

    public Condition Condition { get; } = new();
}

public sealed class IfBlock : ContainerBlock
{
    public override string Kind => "条件分岐";

    /// <summary>最初の「もし」の条件。Children が その本文(then)。</summary>
    public Condition Condition { get; } = new();

    /// <summary>「でなければもし(elif)」分岐の列。</summary>
    public ObservableCollection<ElseIfBranch> ElseIfs { get; } = new();

    /// <summary>「でなければ(else)」の本文。</summary>
    public ObservableCollection<MacroBlock> ElseChildren { get; } = new();

    private bool _hasElse;
    public bool HasElse { get => _hasElse; set => Set(ref _hasElse, value); }

    public void AddElseIf() => ElseIfs.Add(new ElseIfBranch());
    public void RemoveElseIf(ElseIfBranch b) => ElseIfs.Remove(b);
}

// ============================================================
//  変数・通知・画面
// ============================================================
public sealed class VariableBlock : MacroBlock
{
    public override string Kind => "変数";

    private string _name = "cnt";
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>0:= 1:+=</summary>
    private int _op;
    public int Op { get => _op; set => Set(ref _op, value); }

    private string _value = "0";
    public string Value { get => _value; set => Set(ref _value, value); }
}

public sealed class LogBlock : MacroBlock
{
    public override string Kind => "ログ出力";

    private string _text = "メッセージ";
    public string Text { get => _text; set => Set(ref _text, value); }
}

/// <summary>Discord 通知。本文に {変数名} を埋め込め、スクショ添付も可能。</summary>
public sealed class NotifyBlock : MacroBlock
{
    public override string Kind => "Discord通知";

    private string _text = "メッセージ {cnt}";
    public string Text { get => _text; set => Set(ref _text, value); }

    /// <summary>true ならスクショを添付して送る(ImageProcPythonCommand が必要)。</summary>
    private bool _attachScreenshot;
    public bool AttachScreenshot { get => _attachScreenshot; set => Set(ref _attachScreenshot, value); }
}

/// <summary>画面をキャプチャして保存する(ImageProcPythonCommand が必要)。</summary>
public sealed class ScreenshotBlock : MacroBlock
{
    public override string Kind => "スクショ保存";

    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }
}

/// <summary>スティック方向のコード/角度マッピング。index: 0上,1右上,2右,3右下,4下,5左下,6左,7左上</summary>
public static class StickMaps
{
    public static readonly string[] HatCodes =
    {
        "Hat.TOP", "Hat.TOP_RIGHT", "Hat.RIGHT", "Hat.BTM_RIGHT",
        "Hat.BTM", "Hat.BTM_LEFT", "Hat.LEFT", "Hat.TOP_LEFT"
    };

    public static readonly string[] LStickConst =
    {
        "Direction.UP", "Direction.UP_RIGHT", "Direction.RIGHT", "Direction.DOWN_RIGHT",
        "Direction.DOWN", "Direction.DOWN_LEFT", "Direction.LEFT", "Direction.UP_LEFT"
    };

    public static readonly string[] RStickConst =
    {
        "Direction.R_UP", "Direction.R_UP_RIGHT", "Direction.R_RIGHT", "Direction.R_DOWN_RIGHT",
        "Direction.R_DOWN", "Direction.R_DOWN_LEFT", "Direction.R_LEFT", "Direction.R_UP_LEFT"
    };

    public static readonly int[] Angles = { 90, 45, 0, -45, -90, -135, 180, 135 };

    public static readonly string[] Glyphs = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
}

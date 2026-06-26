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

/// <summary>同時押し用の1スロット(プルダウン1つ分)。</summary>
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

/// <summary>ボタンを押す(キーが複数なら同時押し)。</summary>
public sealed class PressBlock : MacroBlock
{
    public override string Kind => "ボタンを押す";

    public ObservableCollection<KeySlot> Keys { get; } = new();

    private double _duration = 0.1;
    public double Duration { get => _duration; set => Set(ref _duration, value); }

    private double _wait = 0.1;
    public double Wait { get => _wait; set => Set(ref _wait, value); }

    public PressBlock()
    {
        Keys.Add(new KeySlot());
    }

    public void AddKey() => Keys.Add(new KeySlot());

    public void RemoveKey(KeySlot slot)
    {
        if (Keys.Count > 1) Keys.Remove(slot);
    }
}

/// <summary>スティック / 方向キーを倒す(8方向)。</summary>
public sealed class StickBlock : MacroBlock
{
    public override string Kind => "スティック / 方向";

    /// <summary>0=十字キー(Hat), 1=左スティック, 2=右スティック</summary>
    private int _device = 1;
    public int Device { get => _device; set => Set(ref _device, value); }

    /// <summary>方向 0..7 (上,右上,右,右下,下,左下,左,左上)</summary>
    private int _direction = 0;
    public int Direction { get => _direction; set => Set(ref _direction, value); }

    /// <summary>傾き(%) 1..100。十字キーでは無視される。</summary>
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
    public override string Kind => "待機する";

    private double _seconds = 1.0;
    public double Seconds { get => _seconds; set => Set(ref _seconds, value); }
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

    /// <summary>方向グリフ(3x3表示用)。</summary>
    public static readonly string[] Glyphs = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
}

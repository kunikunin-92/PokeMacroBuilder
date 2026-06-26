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

/// <summary>指定秒数だけ待機する。</summary>
public sealed class WaitBlock : MacroBlock
{
    public override string Kind => "待機する";

    private double _seconds = 1.0;
    public double Seconds { get => _seconds; set => Set(ref _seconds, value); }
}

using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PokeMacroBuilder.Models;
using PokeMacroBuilder.Services;

namespace PokeMacroBuilder.Views;

public partial class EditorWindow : Window
{
    private readonly MacroStore _store;
    private readonly MacroDocument _doc;

    private Point _dragStart;
    private MacroBlock? _dragBlock;
    private bool _loaded;

    public EditorWindow(MacroStore store, MacroDocument doc)
    {
        InitializeComponent();
        _store = store;
        _doc = doc;

        DisplayNameBox.Text = doc.DisplayName;
        LoopBox.SelectedIndex = doc.Loop switch
        {
            LoopMode.Infinite => 1,
            LoopMode.Count => 2,
            _ => 0
        };
        LoopCountBox.Text = doc.LoopCount.ToString(CultureInfo.InvariantCulture);

        BlocksHost.ItemsSource = doc.Blocks;
        doc.Blocks.CollectionChanged += Blocks_CollectionChanged;

        Title = doc.IsSaved ? $"マクロ編集 - {doc.FileName}" : "マクロ編集 - (新規)";

        Loaded += (_, _) =>
        {
            _loaded = true;
            UpdateLoopCountVisibility();
            UpdateHints();
            UpdatePreview();
        };
    }

    private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHints();
        UpdatePreview();
    }

    private void UpdateHints()
    {
        EmptyScriptHint.Visibility = _doc.Blocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- パレット ----------
    private void AddPress_Click(object sender, RoutedEventArgs e)
        => _doc.Blocks.Add(new PressBlock());

    private void AddCombo_Click(object sender, RoutedEventArgs e)
    {
        var p = new PressBlock();
        p.AddKey(); // 2キー = 同時押し
        _doc.Blocks.Add(p);
    }

    private void AddWait_Click(object sender, RoutedEventArgs e)
        => _doc.Blocks.Add(new WaitBlock());

    // ---------- キースロット ----------
    private void AddKeySlot_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PressBlock p)
        {
            p.AddKey();
            UpdatePreview();
        }
    }

    private void RemoveKeySlot_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not KeySlot slot) return;
        foreach (var block in _doc.Blocks)
        {
            if (block is PressBlock p && p.Keys.Contains(slot))
            {
                p.RemoveKey(slot);
                break;
            }
        }
        UpdatePreview();
    }

    // ---------- 並べ替え/削除 ----------
    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        int i = _doc.Blocks.IndexOf(b);
        if (i > 0) _doc.Blocks.Move(i, i - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        int i = _doc.Blocks.IndexOf(b);
        if (i >= 0 && i < _doc.Blocks.Count - 1) _doc.Blocks.Move(i, i + 1);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MacroBlock b)
            _doc.Blocks.Remove(b);
    }

    // ---------- ドラッグで並べ替え ----------
    private void BlocksHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragBlock = IsOverInteractive(e.OriginalSource as DependencyObject)
            ? null
            : FindBlock(e.OriginalSource as DependencyObject);
    }

    private void BlocksHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragBlock is null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject("macroblock", _dragBlock);
        DragDrop.DoDragDrop(BlocksHost, data, DragDropEffects.Move);
        _dragBlock = null;
    }

    private void BlocksHost_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("macroblock")) return;
        if (e.Data.GetData("macroblock") is not MacroBlock dragged) return;

        var target = FindBlock(e.OriginalSource as DependencyObject);
        int from = _doc.Blocks.IndexOf(dragged);
        if (from < 0) return;

        int to;
        if (target is null || ReferenceEquals(target, dragged))
            to = _doc.Blocks.Count - 1;
        else
            to = _doc.Blocks.IndexOf(target);

        if (to < 0) to = _doc.Blocks.Count - 1;
        if (from != to) _doc.Blocks.Move(from, to);
    }

    private static MacroBlock? FindBlock(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is FrameworkElement fe && fe.DataContext is MacroBlock b)
                return b;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private static bool IsOverInteractive(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is System.Windows.Controls.Primitives.TextBoxBase || src is ComboBox || src is ComboBoxItem || src is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    // ---------- ループ ----------
    private void LoopBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) { return; }
        UpdateLoopCountVisibility();
        UpdatePreview();
    }

    private void UpdateLoopCountVisibility()
    {
        bool isCount = LoopBox.SelectedIndex == 2;
        LoopCountBox.Visibility = isCount ? Visibility.Visible : Visibility.Collapsed;
        LoopCountUnit.Visibility = isCount ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 保存/プレビュー/戻る ----------
    private void SyncToDoc()
    {
        _doc.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? "新しいマクロ" : DisplayNameBox.Text.Trim();
        _doc.Loop = LoopBox.SelectedIndex switch
        {
            1 => LoopMode.Infinite,
            2 => LoopMode.Count,
            _ => LoopMode.None
        };
        _doc.LoopCount = int.TryParse(LoopCountBox.Text, out var n) && n > 0 ? n : 1;
    }

    private void UpdatePreview()
    {
        if (!_loaded) return;
        SyncToDoc();
        var fileName = _doc.FileName ?? "macro1.py";
        PreviewBox.Text = PythonGenerator.Generate(_doc, fileName);
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => UpdatePreview();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
        {
            MessageBox.Show(this, "表示名を入力してください。", "保存エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SyncToDoc();
        try
        {
            _store.Save(_doc);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存に失敗しました:\n" + ex.Message, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Title = $"マクロ編集 - {_doc.FileName}";
        UpdatePreview();
        MessageBox.Show(this,
            $"保存しました。\n\n表示名: {_doc.DisplayName}\nファイル: {_doc.FileName}\n場所: {_store.GeneratedDir}",
            "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}

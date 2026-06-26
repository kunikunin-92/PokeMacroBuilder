using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PokeMacroBuilder.Models;
using PokeMacroBuilder.Services;

namespace PokeMacroBuilder.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private MacroStore? _store;
    private MacroDocument? _doc;          // 編集中のマクロ
    private bool _editorLoaded;

    private Point _dragStart;
    private MacroBlock? _dragBlock;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_settings.LastWorkspace))
                TrySetWorkspace(_settings.LastWorkspace!, silent: true);
            ShowHome();
        };
    }

    // ============================================================
    //  画面切り替え(単一ウィンドウ)
    // ============================================================
    private void ShowHome()
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
        EditorCommands.Visibility = Visibility.Collapsed;
        Breadcrumb.Text = "ホーム";
        RefreshList();
    }

    private void ShowEditor(MacroDocument doc)
    {
        if (_doc != null)
            _doc.Blocks.CollectionChanged -= Blocks_CollectionChanged;

        _doc = doc;
        _editorLoaded = false;

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

        HomePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        EditorCommands.Visibility = Visibility.Visible;
        Breadcrumb.Text = doc.IsSaved ? $"編集 - {doc.FileName}" : "編集 - (新規マクロ)";

        _editorLoaded = true;
        UpdateLoopCountVisibility();
        UpdateEditorHints();
        UpdatePreview();
    }

    private bool IsEditorOpen => EditorPanel.Visibility == Visibility.Visible;

    // ============================================================
    //  メニュー
    // ============================================================
    private void MenuNew_Click(object sender, RoutedEventArgs e) => New_Click(sender, e);
    private void MenuOpenWorkspace_Click(object sender, RoutedEventArgs e) => ChooseWorkspace_Click(sender, e);
    private void MenuSave_Click(object sender, RoutedEventArgs e)
    {
        if (IsEditorOpen) Save_Click(sender, e);
    }
    private void MenuHome_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditorOpen) RefreshList();
    }
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();
    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "PokeMacro Builder\nブロックで pokecon マクロを作成するツール\n\n© 2026",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.S when IsEditorOpen: Save_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.N: New_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.O: ChooseWorkspace_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            }
        }
    }

    // ============================================================
    //  ワークスペース / 一覧
    // ============================================================
    private void ChooseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "pokecon(Poke-Controller)のフォルダを選択してください",
        };
        if (!string.IsNullOrEmpty(_settings.LastWorkspace))
            dlg.InitialDirectory = _settings.LastWorkspace;

        if (dlg.ShowDialog(this) == true)
        {
            TrySetWorkspace(dlg.FolderName, silent: false);
            if (IsEditorOpen) ShowHome();
        }
    }

    private void TrySetWorkspace(string path, bool silent)
    {
        var store = MacroStore.TryCreate(path);
        if (store is null)
        {
            if (!silent)
                MessageBox.Show(this,
                    "選択したフォルダ内に PythonCommands フォルダが見つかりませんでした。\n" +
                    "Poke-Controller のルート、または SerialController フォルダを選んでください。",
                    "ワークスペースエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _store = store;
        _settings.LastWorkspace = path;
        _settings.Save();
        WorkspacePathText.Text = path;
        StatusText.Text = $"ワークスペース: {path}";
        RefreshList();
    }

    private void RefreshList()
    {
        if (_store is null) { return; }
        List<MacroEntry> entries = _store.LoadAll();
        MacroList.ItemsSource = entries;
        EmptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"マクロ {entries.Count} 件  (保存先: {_store.GeneratedDir})";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureWorkspace()) return;
        var doc = new MacroDocument { DisplayName = "新しいマクロ" };
        ShowEditor(doc);
        DisplayNameBox.Focus();
        DisplayNameBox.SelectAll();
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();
    private void MacroList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (MacroList.SelectedItem is not MacroEntry entry)
        {
            MessageBox.Show(this, "編集するマクロを選択してください。", "情報",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowEditor(entry.Document);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        if (MacroList.SelectedItem is not MacroEntry entry)
        {
            MessageBox.Show(this, "削除するマクロを選択してください。", "情報",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var res = MessageBox.Show(this,
            $"「{entry.DisplayName}」({entry.FileName}) を削除しますか?",
            "削除の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        _store.Delete(entry);
        RefreshList();
    }

    private bool EnsureWorkspace()
    {
        if (_store != null) return true;
        MessageBox.Show(this, "先にワークスペース(pokeconフォルダ)を選択してください。", "情報",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    // ============================================================
    //  エディタ: ブロック操作
    // ============================================================
    private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEditorHints();
        UpdatePreview();
    }

    private void UpdateEditorHints()
    {
        if (_doc is null) return;
        EmptyScriptHint.Visibility = _doc.Blocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddPress_Click(object sender, RoutedEventArgs e) => _doc?.Blocks.Add(new PressBlock());
    private void AddWait_Click(object sender, RoutedEventArgs e) => _doc?.Blocks.Add(new WaitBlock());

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
        if (_doc is null || ((FrameworkElement)sender).DataContext is not KeySlot slot) return;
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

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        int i = _doc.Blocks.IndexOf(b);
        if (i > 0) _doc.Blocks.Move(i, i - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_doc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        int i = _doc.Blocks.IndexOf(b);
        if (i >= 0 && i < _doc.Blocks.Count - 1) _doc.Blocks.Move(i, i + 1);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_doc != null && ((FrameworkElement)sender).DataContext is MacroBlock b)
            _doc.Blocks.Remove(b);
    }

    // ---- ドラッグ並べ替え ----
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

        DragDrop.DoDragDrop(BlocksHost, new DataObject("macroblock", _dragBlock), DragDropEffects.Move);
        _dragBlock = null;
    }

    private void BlocksHost_Drop(object sender, DragEventArgs e)
    {
        if (_doc is null || !e.Data.GetDataPresent("macroblock")) return;
        if (e.Data.GetData("macroblock") is not MacroBlock dragged) return;

        var target = FindBlock(e.OriginalSource as DependencyObject);
        int from = _doc.Blocks.IndexOf(dragged);
        if (from < 0) return;

        int to = (target is null || ReferenceEquals(target, dragged))
            ? _doc.Blocks.Count - 1
            : _doc.Blocks.IndexOf(target);
        if (to < 0) to = _doc.Blocks.Count - 1;
        if (from != to) _doc.Blocks.Move(from, to);
    }

    private static MacroBlock? FindBlock(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is FrameworkElement fe && fe.DataContext is MacroBlock b) return b;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private static bool IsOverInteractive(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is System.Windows.Controls.Primitives.TextBoxBase ||
                src is ComboBox || src is ComboBoxItem ||
                src is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    // ============================================================
    //  ループ / 保存 / プレビュー
    // ============================================================
    private void LoopBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_editorLoaded) return;
        UpdateLoopCountVisibility();
        UpdatePreview();
    }

    private void UpdateLoopCountVisibility()
    {
        bool isCount = LoopBox.SelectedIndex == 2;
        LoopCountBox.Visibility = isCount ? Visibility.Visible : Visibility.Collapsed;
        LoopCountUnit.Visibility = isCount ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncToDoc()
    {
        if (_doc is null) return;
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
        if (!_editorLoaded || _doc is null) return;
        SyncToDoc();
        PreviewBox.Text = PythonGenerator.Generate(_doc, _doc.FileName ?? "macro1.py");
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => UpdatePreview();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _doc is null) return;
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

        Breadcrumb.Text = $"編集 - {_doc.FileName}";
        StatusText.Text = $"保存しました: {_doc.FileName}  ({_doc.DisplayName})";
        UpdatePreview();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowHome();
}

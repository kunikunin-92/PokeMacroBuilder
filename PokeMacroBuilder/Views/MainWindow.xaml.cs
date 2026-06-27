using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PokeMacroBuilder.Models;
using PokeMacroBuilder.Services;

namespace PokeMacroBuilder.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private MacroStore? _store;

    private readonly ObservableCollection<MacroDocument> _openDocs = new();
    private MacroDocument? _activeDoc;          // null = ホーム
    private MacroDocument? _subscribedDoc;
    private bool _editorLoaded;

    // ステータス
    private string _idleStatus = "準備完了";
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    // ドラッグ配置/並べ替え状態
    private enum DragKind { None, Move, NewBlock }
    private DragKind _dragKind;
    private bool _dragArmed;
    private bool _dragging;
    private bool _paletteDragged;
    private Point _dragStart;
    private Point _grab;
    private MacroBlock? _moveBlock;           // 既存ブロックの移動
    private Func<MacroBlock?>? _newFactory;    // パレットからの新規
    private FrameworkElement? _srcElement;     // ゴースト元
    private FrameworkElement? _dimmed;         // 移動中に薄くする元コンテナ
    private DragGhostAdorner? _ghost;
    private InsertionAdorner? _insAdorner;
    private AdornerLayer? _adornerLayer;
    private ObservableCollection<MacroBlock>? _dropColl;
    private int _dropIndex;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        TabBar.ItemsSource = _openDocs;
        BlocksHost.LostMouseCapture += (_, _) => EndDrag();
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusText.Text = _idleStatus; };

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_settings.LastWorkspace))
                TrySetWorkspace(_settings.LastWorkspace!, silent: true);
            BuildRecentMenu();
            ActivateHome();
        };
    }

    // ============================================================
    //  タブ / 画面切り替え
    // ============================================================
    private void ActivateHome()
    {
        _activeDoc = null;
        foreach (var d in _openDocs) d.IsActive = false;
        SetBlocksSubscription(null);
        _editorLoaded = false;

        EditorPanel.Visibility = Visibility.Collapsed;
        HomePanel.Visibility = Visibility.Visible;
        SetHomeTabActive(true);
        RefreshList();
    }

    private void ActivateDoc(MacroDocument doc)
    {
        _activeDoc = doc;
        foreach (var d in _openDocs) d.IsActive = ReferenceEquals(d, doc);
        SetHomeTabActive(false);

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
        SetBlocksSubscription(doc);

        HomePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;

        _editorLoaded = true;
        UpdateLoopCountVisibility();
        UpdateEditorHints();
        UpdatePreview();
    }

    /// <summary>ドキュメントをタブとして開く(同一ファイルが既にあれば再利用)。</summary>
    private void OpenDoc(MacroDocument doc)
    {
        if (doc.FilePath != null)
        {
            var existing = _openDocs.FirstOrDefault(d =>
                d.FilePath != null && string.Equals(d.FilePath, doc.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { ActivateDoc(existing); return; }
        }
        _openDocs.Add(doc);
        ActivateDoc(doc);
    }

    private void CloseDoc(MacroDocument doc)
    {
        if (!doc.IsSaved && doc.Blocks.Count > 0)
        {
            var ans = MessageBox.Show(this,
                $"「{doc.DisplayName}」は保存されていません。閉じますか?",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ans != MessageBoxResult.Yes) return;
        }

        int idx = _openDocs.IndexOf(doc);
        bool wasActive = ReferenceEquals(_activeDoc, doc);
        if (_subscribedDoc == doc) SetBlocksSubscription(null);
        _openDocs.Remove(doc);

        if (wasActive)
        {
            if (_openDocs.Count == 0) ActivateHome();
            else ActivateDoc(_openDocs[Math.Min(idx, _openDocs.Count - 1)]);
        }
    }

    private void SetBlocksSubscription(MacroDocument? doc)
    {
        if (ReferenceEquals(_subscribedDoc, doc)) return;
        if (_subscribedDoc != null) _subscribedDoc.Blocks.CollectionChanged -= Blocks_CollectionChanged;
        _subscribedDoc = doc;
        if (_subscribedDoc != null) _subscribedDoc.Blocks.CollectionChanged += Blocks_CollectionChanged;
    }

    private void SetHomeTabActive(bool active)
    {
        HomeTab.Background = (Brush)FindResource(active ? "EditorBgBrush" : "ShellBgBrush");
        HomeTab.BorderBrush = active ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
        HomeTabText.Foreground = (Brush)FindResource(active ? "TextBrush" : "TextDimBrush");
        HomeTabText.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void HomeTab_Click(object sender, MouseButtonEventArgs e) => ActivateHome();

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MacroDocument doc)
            ActivateDoc(doc);
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MacroDocument doc)
            CloseDoc(doc);
    }

    // ============================================================
    //  メニュー
    // ============================================================
    private void MenuNew_Click(object sender, RoutedEventArgs e) => New_Click(sender, e);
    private void MenuOpenWorkspace_Click(object sender, RoutedEventArgs e) => ChooseWorkspace_Click(sender, e);
    private void MenuSave_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc != null) Save_Click(sender, e);
    }
    private void MenuSaveAsCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _activeDoc is null) return;
        SyncToDoc();
        var copy = MacroSerializer.Clone(_activeDoc);
        copy.DisplayName = _activeDoc.DisplayName + " のコピー";
        OpenDoc(copy);
        // 直ちにファイル化
        try { _store.Save(copy); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "コピーの保存に失敗しました:\n" + ex.Message, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AfterSaved(copy);
    }
    private void MenuHome_Click(object sender, RoutedEventArgs e) => ActivateHome();
    private void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null) RefreshList();
    }
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();
    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "PokeMacro Builder\nブロックで pokecon マクロを作成するツール\n\n© 2026",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        if (_settings.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(なし)", IsEnabled = false });
            return;
        }
        foreach (var path in _settings.RecentFiles)
        {
            var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path, Tag = path };
            item.Click += Recent_Click;
            RecentMenu.Items.Add(item);
        }
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string path) return;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "ファイルが見つかりません:\n" + path, "情報",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _settings.RecentFiles.Remove(path);
            _settings.Save();
            BuildRecentMenu();
            return;
        }
        var doc = LoadDocFromPath(path);
        if (doc != null) OpenDoc(doc);
    }

    private MacroDocument? LoadDocFromPath(string path)
    {
        try
        {
            var text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            var doc = MacroSerializer.TryParse(text);
            if (doc is null)
            {
                MessageBox.Show(this, "このファイルは本ツールで作成されたマクロではありません。", "情報",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            doc.FileName = Path.GetFileName(path);
            doc.FilePath = path;
            return doc;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "読み込みに失敗しました:\n" + ex.Message, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.S when _activeDoc != null: Save_Click(this, new RoutedEventArgs()); e.Handled = true; break;
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
            TrySetWorkspace(dlg.FolderName, silent: false);
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
        if (_activeDoc is null) RefreshList();
    }

    private void RefreshList()
    {
        if (_store is null) { return; }
        List<MacroEntry> entries = _store.LoadAll();
        MacroList.ItemsSource = entries;
        EmptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetIdleStatus($"マクロ {entries.Count} 件  (保存先: {_store.GeneratedDir})");
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureWorkspace()) return;
        var doc = new MacroDocument { DisplayName = "新しいマクロ" };
        OpenDoc(doc);
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
        OpenDoc(entry.Document);
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
    //  ブロック操作
    // ============================================================
    private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEditorHints();
        UpdatePreview();
    }

    private void UpdateEditorHints()
    {
        if (_activeDoc is null) return;
        EmptyScriptHint.Visibility = _activeDoc.Blocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- ブロック追加(クリック=末尾へ、ドラッグ=任意位置へ) ----
    private static MacroBlock? CreateByKind(string? kind) => kind switch
    {
        "press" => new PressBlock(),
        "stick" => new StickBlock(),
        "wait" => new WaitBlock(),
        "loop" => new LoopBlock(),
        "if" => new IfBlock(),
        "var" => new VariableBlock(),
        "log" => new LogBlock(),
        "notify" => new NotifyBlock(),
        "screenshot" => new ScreenshotBlock(),
        _ => null,
    };

    private void PaletteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_paletteDragged) { _paletteDragged = false; return; } // ドラッグ後のClickは無視
        if (_activeDoc is null) return;
        if (sender is FrameworkElement fe && CreateByKind(fe.Tag as string) is { } nb)
        {
            _activeDoc.Blocks.Add(nb);
            UpdateEditorHints();
            UpdatePreview();
        }
    }

    // ---- 条件分岐: elif / else ----
    private void IfElseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is IfBlock ib)
        {
            ib.HasElse = !ib.HasElse;
            UpdatePreview();
        }
    }

    private void AddElseIf_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is IfBlock ib)
        {
            ib.AddElseIf();
            UpdatePreview();
        }
    }

    private void RemoveElseIf_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not ElseIfBranch br) return;
        foreach (var b in PythonGenerator.AllBlocks(_activeDoc.Blocks))
            if (b is IfBlock ib && ib.ElseIfs.Contains(br)) { ib.RemoveElseIf(br); break; }
        UpdatePreview();
    }

    // 条件・テキスト等の編集 → プレビュー更新
    private void Field_LostFocus(object sender, RoutedEventArgs e) => UpdatePreview();
    private void Field_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void PadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is StickBlock sb &&
            fe.Tag is string t && int.TryParse(t, out var idx))
        {
            sb.Direction = idx;
            UpdatePreview();
        }
    }

    private void StickField_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void NumberField_LostFocus(object sender, RoutedEventArgs e) => UpdatePreview();

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
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not KeySlot slot) return;
        foreach (var block in PythonGenerator.AllBlocks(_activeDoc.Blocks))
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
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        var coll = FindParentCollection(b);
        int i = coll?.IndexOf(b) ?? -1;
        if (coll != null && i > 0) { coll.Move(i, i - 1); UpdatePreview(); }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        var coll = FindParentCollection(b);
        int i = coll?.IndexOf(b) ?? -1;
        if (coll != null && i >= 0 && i < coll.Count - 1) { coll.Move(i, i + 1); UpdatePreview(); }
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        FindParentCollection(b)?.Remove(b);
        UpdateEditorHints();
        UpdatePreview();
    }

    /// <summary>ブロックの親コレクション(ルート/コンテナのChildren/Else/elif)を返す。</summary>
    private ObservableCollection<MacroBlock>? FindParentCollection(MacroBlock target)
    {
        if (_activeDoc is null) return null;
        return Search(_activeDoc.Blocks);

        ObservableCollection<MacroBlock>? Search(ObservableCollection<MacroBlock> coll)
        {
            if (coll.Contains(target)) return coll;
            foreach (var b in coll)
            {
                if (b is IfBlock ib)
                {
                    var r = Search(ib.Children);
                    if (r != null) return r;
                    foreach (var br in ib.ElseIfs) { r = Search(br.Children); if (r != null) return r; }
                    r = Search(ib.ElseChildren);
                    if (r != null) return r;
                }
                else if (b is ContainerBlock cb)
                {
                    var r = Search(cb.Children);
                    if (r != null) return r;
                }
            }
            return null;
        }
    }

    // ============================================================
    //  ドラッグ配置(パレット→任意位置 / 既存ブロックの移動)
    // ============================================================
    private void PaletteBlock_Down(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = false;
        _paletteDragged = false;
        if (_activeDoc is null || sender is not FrameworkElement fe) return;
        if (CreateByKind(fe.Tag as string) is null) return;

        var kind = fe.Tag as string;
        _dragKind = DragKind.NewBlock;
        _newFactory = () => CreateByKind(kind);
        _srcElement = fe;
        _dragStart = e.GetPosition(BlocksHost);
        _dragArmed = true;
    }

    private void PaletteBlock_Move(object sender, MouseEventArgs e)
    {
        if (_dragging || !_dragArmed || _dragKind != DragKind.NewBlock) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!MovedEnough(e)) return;
        _paletteDragged = true;
        BeginDrag(e);
        if (_dragging) DragUpdate(e);
    }

    private void BlocksHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = false;
        if (IsOverInteractive(e.OriginalSource as DependencyObject)) return;
        var block = FindBlock(e.OriginalSource as DependencyObject);
        if (block is null) return;

        _dragKind = DragKind.Move;
        _moveBlock = block;
        _srcElement = FindBlockContainer(e.OriginalSource as DependencyObject, block);
        _dragStart = e.GetPosition(BlocksHost);
        _dragArmed = _srcElement != null;
    }

    private void BlocksHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) { DragUpdate(e); return; }
        if (!_dragArmed || _dragKind != DragKind.Move) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!MovedEnough(e)) return;
        BeginDrag(e);
        if (_dragging) DragUpdate(e);
    }

    private void BlocksHost_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) PerformDrop();
        EndDrag();
    }

    private bool MovedEnough(MouseEventArgs e)
    {
        var p = e.GetPosition(BlocksHost);
        return Math.Abs(p.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(p.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void BeginDrag(MouseEventArgs e)
    {
        if (_srcElement is null || _srcElement.ActualWidth < 1 || _srcElement.ActualHeight < 1)
        {
            _dragArmed = false;
            return;
        }
        _grab = e.GetPosition(_srcElement);

        var size = new Size(_srcElement.ActualWidth, _srcElement.ActualHeight);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(_srcElement);
        bmp.Freeze();

        _adornerLayer = AdornerLayer.GetAdornerLayer(BlocksHost);
        if (_adornerLayer is null) { _dragArmed = false; return; }

        _ghost = new DragGhostAdorner(BlocksHost, bmp, size);
        _adornerLayer.Add(_ghost);
        _insAdorner = new InsertionAdorner(BlocksHost);
        _adornerLayer.Add(_insAdorner);

        if (_dragKind == DragKind.Move) { _dimmed = _srcElement; _srcElement.Opacity = 0.35; }

        _dragging = true;
        BlocksHost.CaptureMouse();
    }

    private void DragUpdate(MouseEventArgs e)
    {
        var posRoot = e.GetPosition(BlocksHost);
        _ghost?.UpdatePosition(new Point(posRoot.X - _grab.X, posRoot.Y - _grab.Y));
        ComputeDropTarget(posRoot);
        AutoScroll(e);
    }

    private void ComputeDropTarget(Point posRoot)
    {
        if (_activeDoc is null) return;
        var hit = HitTestTopmost(posRoot);
        var host = FindBlockHost(hit);
        ItemsControl hostIc = host ?? BlocksHost;
        var coll = hostIc.ItemsSource as ObservableCollection<MacroBlock> ?? _activeDoc.Blocks;

        int index = coll.Count;
        double? y = null;
        for (int i = 0; i < coll.Count; i++)
        {
            if (hostIc.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var top = c.TranslatePoint(new Point(0, 0), BlocksHost).Y;
            if (posRoot.Y < top + c.ActualHeight / 2) { index = i; y = top; break; }
        }

        var htl = hostIc.TranslatePoint(new Point(0, 0), BlocksHost);
        if (y is null)
        {
            if (coll.Count > 0 && hostIc.ItemContainerGenerator.ContainerFromIndex(coll.Count - 1) is FrameworkElement last)
                y = last.TranslatePoint(new Point(0, 0), BlocksHost).Y + last.ActualHeight;
            else
                y = htl.Y + 4;
        }

        _dropColl = coll;
        _dropIndex = index;
        double x1 = htl.X + 4;
        double x2 = htl.X + Math.Max(40, hostIc.ActualWidth) - 4;
        _insAdorner?.Update(x1, x2, y.Value);
    }

    private void PerformDrop()
    {
        if (_activeDoc is null || _dropColl is null) return;

        if (_dragKind == DragKind.NewBlock)
        {
            var nb = _newFactory?.Invoke();
            if (nb is null) return;
            _dropColl.Insert(Math.Clamp(_dropIndex, 0, _dropColl.Count), nb);
        }
        else if (_dragKind == DragKind.Move && _moveBlock != null)
        {
            if (IsInsideSubtree(_moveBlock, _dropColl)) return;   // 自分の中へは入れない
            var oldColl = FindParentCollection(_moveBlock);
            if (oldColl is null) return;
            int oldIndex = oldColl.IndexOf(_moveBlock);
            int idx = _dropIndex;
            oldColl.RemoveAt(oldIndex);
            if (ReferenceEquals(oldColl, _dropColl) && idx > oldIndex) idx--;
            _dropColl.Insert(Math.Clamp(idx, 0, _dropColl.Count), _moveBlock);
        }
        UpdateEditorHints();
    }

    private void AutoScroll(MouseEventArgs e)
    {
        if (ScriptScroll is null) return;
        var p = e.GetPosition(ScriptScroll);
        const double edge = 40;
        if (p.Y < edge)
            ScriptScroll.ScrollToVerticalOffset(ScriptScroll.VerticalOffset - 14);
        else if (p.Y > ScriptScroll.ActualHeight - edge)
            ScriptScroll.ScrollToVerticalOffset(ScriptScroll.VerticalOffset + 14);
    }

    private void EndDrag()
    {
        bool was = _dragging;
        if (_adornerLayer != null)
        {
            if (_ghost != null) _adornerLayer.Remove(_ghost);
            if (_insAdorner != null) _adornerLayer.Remove(_insAdorner);
        }
        _ghost = null; _insAdorner = null; _adornerLayer = null;
        if (_dimmed != null) { _dimmed.Opacity = 1.0; _dimmed = null; }
        if (BlocksHost.IsMouseCaptured) BlocksHost.ReleaseMouseCapture();

        _dragging = false;
        _dragArmed = false;
        _dragKind = DragKind.None;
        _moveBlock = null;
        _newFactory = null;
        _srcElement = null;
        _dropColl = null;

        if (was) UpdatePreview();
    }

    // ---- ヘルパ ----
    private DependencyObject? HitTestTopmost(Point p)
    {
        DependencyObject? result = null;
        VisualTreeHelper.HitTest(BlocksHost, null,
            r => { result = r.VisualHit; return HitTestResultBehavior.Stop; },
            new PointHitTestParameters(p));
        return result;
    }

    private static ItemsControl? FindBlockHost(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is ItemsControl ic && ic.ItemsSource is ObservableCollection<MacroBlock>) return ic;
            src = VisualTreeHelper.GetParent(src);
        }
        return null;
    }

    private static FrameworkElement? FindBlockContainer(DependencyObject? src, MacroBlock block)
    {
        FrameworkElement? cand = null;
        var d = src;
        while (d != null)
        {
            if (d is FrameworkElement fe && ReferenceEquals(fe.DataContext, block)) cand = fe;
            else if (cand != null) break;
            d = VisualTreeHelper.GetParent(d);
        }
        return cand;
    }

    /// <summary>coll が block の子孫コレクションかどうか(循環防止)。</summary>
    private static bool IsInsideSubtree(MacroBlock block, ObservableCollection<MacroBlock> coll)
    {
        bool Check(MacroBlock b)
        {
            if (b is IfBlock ib)
            {
                if (ReferenceEquals(ib.Children, coll) || ReferenceEquals(ib.ElseChildren, coll)) return true;
                foreach (var br in ib.ElseIfs)
                {
                    if (ReferenceEquals(br.Children, coll)) return true;
                    foreach (var c in br.Children) if (Check(c)) return true;
                }
                foreach (var c in ib.Children) if (Check(c)) return true;
                foreach (var c in ib.ElseChildren) if (Check(c)) return true;
            }
            else if (b is ContainerBlock cb)
            {
                if (ReferenceEquals(cb.Children, coll)) return true;
                foreach (var c in cb.Children) if (Check(c)) return true;
            }
            return false;
        }
        return Check(block);
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
    //  表示名 / ループ / 保存 / プレビュー
    // ============================================================
    private void DisplayNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_editorLoaded || _activeDoc is null) return;
        _activeDoc.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? "新しいマクロ" : DisplayNameBox.Text;
        UpdatePreview();
    }

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
        if (_activeDoc is null) return;
        _activeDoc.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? "新しいマクロ" : DisplayNameBox.Text.Trim();
        _activeDoc.Loop = LoopBox.SelectedIndex switch
        {
            1 => LoopMode.Infinite,
            2 => LoopMode.Count,
            _ => LoopMode.None
        };
        _activeDoc.LoopCount = int.TryParse(LoopCountBox.Text, out var n) && n > 0 ? n : 1;
    }

    private void UpdatePreview()
    {
        if (!_editorLoaded || _activeDoc is null) return;
        SyncToDoc();
        var code = PythonGenerator.Generate(_activeDoc, _activeDoc.FileName ?? "macro1.py");
        PreviewBox.Document = PythonHighlighter.BuildDocument(code);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _activeDoc is null) return;
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
        {
            MessageBox.Show(this, "表示名を入力してください。", "保存エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SyncToDoc();
        try
        {
            _store.Save(_activeDoc);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存に失敗しました:\n" + ex.Message, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AfterSaved(_activeDoc);
    }

    private void AfterSaved(MacroDocument doc)
    {
        UpdatePreview();
        if (doc.FilePath != null)
        {
            _settings.AddRecent(doc.FilePath);
            _settings.Save();
            BuildRecentMenu();
        }
        FlashStatus($"✓ 保存しました — {doc.DisplayName} ({doc.FileName})");
    }

    // ============================================================
    //  ステータス表示
    // ============================================================
    private void SetIdleStatus(string text)
    {
        _idleStatus = text;
        if (!_statusTimer.IsEnabled) StatusText.Text = text;
    }

    private void FlashStatus(string text)
    {
        StatusText.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();   // 3秒後に通常表示へ戻す
    }
}

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
    private Point _dragStart;
    private MacroBlock? _moveBlock;           // 既存ブロックの移動
    private Func<MacroBlock?>? _newFactory;    // パレットからの新規
    private string? _paletteKind;             // パレットからの種別(ラベル用)
    private FrameworkElement? _srcElement;     // 移動元(ドラッグ判定用)
    private FrameworkElement? _dimmed;         // 移動中に薄くする元コンテナ
    private ObservableCollection<MacroBlock>? _dropColl;
    private int _dropIndex;

    // Undo / Redo (スナップショット方式)
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private bool _suppressSnapshot;

    // テンプレ画像フィールド
    public ObservableCollection<TemplateImage> TemplateImages { get; } = new();
    public ObservableCollection<string> TemplateImageRefs { get; } = new();

    public static readonly DependencyProperty ImageColumnsProperty =
        DependencyProperty.Register(nameof(ImageColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(2));
    public int ImageColumns { get => (int)GetValue(ImageColumnsProperty); set => SetValue(ImageColumnsProperty, value); }

    public static readonly DependencyProperty ThumbWidthProperty =
        DependencyProperty.Register(nameof(ThumbWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(100.0));
    public double ThumbWidth { get => (double)GetValue(ThumbWidthProperty); set => SetValue(ThumbWidthProperty, value); }

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        TabBar.ItemsSource = _openDocs;
        ImageList.ItemsSource = TemplateImages;
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
        BlocksHost.ItemsSource = doc.Blocks;
        SetBlocksSubscription(doc);

        HomePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;

        _editorLoaded = true;
        UpdateEditorHints();
        LoadImages();
        UpdatePreview();
        ResetUndo();
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
            // テキスト編集中は TextBox 自身の Undo/Redo を優先
            bool inText = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
            switch (e.Key)
            {
                case Key.S when _activeDoc != null: Save_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.N: New_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.O: ChooseWorkspace_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case Key.Z when _activeDoc != null && !inText: Undo(); e.Handled = true; break;
                case Key.Y when _activeDoc != null && !inText: Redo(); e.Handled = true; break;
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

    // ---- 条件分岐: elif / else ----
    private void IfElseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is IfBlock ib)
        {
            ib.HasElse = !ib.HasElse;
            Edited();
        }
    }

    private void AddElseIf_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is IfBlock ib)
        {
            ib.AddElseIf();
            Edited();
        }
    }

    private void RemoveElseIf_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not ElseIfBranch br) return;
        foreach (var b in PythonGenerator.AllBlocks(_activeDoc.Blocks))
            if (b is IfBlock ib && ib.ElseIfs.Contains(br)) { ib.RemoveElseIf(br); break; }
        Edited();
    }

    // 条件・テキスト等の編集 → プレビュー更新 + Undo記録
    private void Field_LostFocus(object sender, RoutedEventArgs e) => Edited();
    private void Field_SelectionChanged(object sender, SelectionChangedEventArgs e) => Edited();

    // ============================================================
    //  テンプレ画像フィールド
    // ============================================================
    private void LoadImages()
    {
        TemplateImages.Clear();
        TemplateImageRefs.Clear();
        if (_store is null || _activeDoc is null) return;
        foreach (var img in _store.ListImages(_activeDoc))
        {
            TemplateImages.Add(img);
            TemplateImageRefs.Add(img.RelRef);
        }
        UpdateThumbWidth();
    }

    /// <summary>画像を追加できる状態にする(未保存マクロは先に保存)。</summary>
    private bool EnsureSavedForImages()
    {
        if (_store is null) { EnsureWorkspace(); return false; }
        if (_activeDoc is null) return false;
        if (_activeDoc.FileName is null)
        {
            SyncToDoc();
            try { _store.Save(_activeDoc); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "画像追加の前に保存が必要ですが、保存に失敗しました:\n" + ex.Message,
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            FlashStatus($"保存しました: {_activeDoc.FileName}");
        }
        return true;
    }

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSavedForImages()) return;
        var dlg = new OpenFileDialog
        {
            Title = "テンプレにする画像を選択",
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true)
            ImportImages(dlg.FileNames);
    }

    private void ImageField_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var imgs = ((string[])e.Data.GetData(DataFormats.FileDrop)).Where(f =>
        {
            var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp";
        }).ToArray();
        e.Handled = true;
        if (imgs.Length == 0) return;

        // ドロップ(OLE)操作を先に完了させてからトリミング窓などを開く
        // (ここで同期的にモーダルを開くとエクスプローラー側がフリーズする)
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!EnsureSavedForImages()) return;
            ImportImages(imgs);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ImageField_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ImportImages(IEnumerable<string> paths)
    {
        if (_store is null || _activeDoc is null) return;
        int added = 0;
        foreach (var path in paths)
        {
            try
            {
                var crop = new CropWindow(path) { Owner = this };
                if (crop.ShowDialog() == true && crop.Result != null)
                {
                    _store.AddImage(_activeDoc, crop.Result);
                    added++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"画像の追加に失敗しました ({System.IO.Path.GetFileName(path)}):\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (added > 0) { LoadImages(); FlashStatus($"テンプレ画像を {added} 件追加しました"); }
        Activate();   // 追加後はメインウィンドウにフォーカスを戻す
    }

    private void DeleteImage_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _activeDoc is null) return;
        if (((FrameworkElement)sender).DataContext is not TemplateImage img) return;
        var res = MessageBox.Show(this, $"「{img.FileName}」を削除しますか?", "削除の確認",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        try
        {
            _store.DeleteImage(_activeDoc, img);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "削除に失敗しました:\n" + ex.Message, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        LoadImages();
        UpdatePreview();
    }

    private void ImageColumns_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem it && int.TryParse(it.Content?.ToString(), out var n))
        {
            ImageColumns = n;
            UpdateThumbWidth();
        }
    }

    private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumbWidth();

    private void UpdateThumbWidth()
    {
        if (ImageScroll is null) return;
        double avail = ImageScroll.ViewportWidth;
        if (avail <= 0) avail = ImageScroll.ActualWidth;
        if (avail <= 0) return;
        int cols = ImageColumns < 1 ? 1 : ImageColumns;
        // 各サムネ Border は Margin 3*2、内部 Image Margin 3*2 を含む
        double w = (avail - 2) / cols - 6;
        ThumbWidth = Math.Max(36, w);
    }

    private void PadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is StickBlock sb &&
            fe.Tag is string t && int.TryParse(t, out var idx))
        {
            sb.Direction = idx;
            Edited();
        }
    }

    private void StickField_Changed(object sender, SelectionChangedEventArgs e) => Edited();
    private void NumberField_LostFocus(object sender, RoutedEventArgs e) => Edited();

    private void AddKeySlot_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PressBlock p)
        {
            p.AddKey();
            Edited();
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
        Edited();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        var coll = FindParentCollection(b);
        int i = coll?.IndexOf(b) ?? -1;
        if (coll != null && i > 0) { coll.Move(i, i - 1); Edited(); }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        var coll = FindParentCollection(b);
        int i = coll?.IndexOf(b) ?? -1;
        if (coll != null && i >= 0 && i < coll.Count - 1) { coll.Move(i, i + 1); Edited(); }
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDoc is null || ((FrameworkElement)sender).DataContext is not MacroBlock b) return;
        FindParentCollection(b)?.Remove(b);
        UpdateEditorHints();
        Edited();
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
        if (_activeDoc is null || sender is not FrameworkElement fe) return;
        if (CreateByKind(fe.Tag as string) is null) return;

        var kind = fe.Tag as string;
        _dragKind = DragKind.NewBlock;
        _paletteKind = kind;
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
        // ドラッグ中ラベル(常にカーソルに追従する小さなチップ。色はブロックに合わせる)
        var sample = _dragKind == DragKind.Move ? _moveBlock : CreateByKind(_paletteKind);
        var (bg, fg) = ChipColors(sample);
        GhostChip.Background = bg;
        GhostChipText.Foreground = fg;
        GhostChipIcon.Foreground = fg;
        GhostChipText.Text = sample?.Kind ?? "ブロック";
        GhostChipIcon.Text = _dragKind == DragKind.NewBlock ? "➕" : "☰";
        GhostChip.Visibility = Visibility.Visible;
        InsertLine.Visibility = Visibility.Visible;

        // 移動元を薄く表示
        if (_dragKind == DragKind.Move && _srcElement != null)
        {
            _dimmed = _srcElement;
            _srcElement.Opacity = 0.4;
        }

        _dragging = true;
        BlocksHost.CaptureMouse();
    }

    private void DragUpdate(MouseEventArgs e)
    {
        var posOverlay = e.GetPosition(DragOverlay);
        Canvas.SetLeft(GhostChip, posOverlay.X + 14);
        Canvas.SetTop(GhostChip, posOverlay.Y + 10);
        ComputeDropTarget(e.GetPosition(BlocksHost));
        AutoScroll(e);
    }

    private static (Brush bg, Brush fg) ChipColors(MacroBlock? b)
    {
        Brush Bg(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = 0.95 };
        Brush White = Brushes.White;
        Brush DarkBrown = new SolidColorBrush(Color.FromRgb(0x3B, 0x2A, 0x00));
        Brush DarkCyan = new SolidColorBrush(Color.FromRgb(0x06, 0x38, 0x4B));
        return b switch
        {
            PressBlock => (Bg("#4C97FF"), White),
            StickBlock => (Bg("#9966FF"), White),
            WaitBlock => (Bg("#FFAB19"), DarkBrown),
            LoopBlock => (Bg("#E6A817"), DarkBrown),
            IfBlock => (Bg("#E6A817"), DarkBrown),
            VariableBlock => (Bg("#FF8C1A"), White),
            LogBlock => (Bg("#5CB1D6"), DarkCyan),
            NotifyBlock => (Bg("#CF63B4"), White),
            ScreenshotBlock => (Bg("#12A89D"), White),
            _ => (Bg("#007ACC"), White),
        };
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

        // BlocksHost 座標 → オーバーレイ座標へ変換してライン表示
        var p1 = BlocksHost.TranslatePoint(new Point(htl.X + 4, y.Value), DragOverlay);
        double width = Math.Max(40, hostIc.ActualWidth) - 8;
        Canvas.SetLeft(InsertLine, p1.X);
        Canvas.SetTop(InsertLine, p1.Y - 1.5);
        InsertLine.Width = width;
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
        GhostChip.Visibility = Visibility.Collapsed;
        InsertLine.Visibility = Visibility.Collapsed;
        if (_dimmed != null) { _dimmed.Opacity = 1.0; _dimmed = null; }
        if (BlocksHost.IsMouseCaptured) BlocksHost.ReleaseMouseCapture();

        _dragging = false;
        _dragArmed = false;
        _dragKind = DragKind.None;
        _moveBlock = null;
        _newFactory = null;
        _srcElement = null;
        _dropColl = null;

        if (was) { UpdatePreview(); RecordSnapshot(); }
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
    //  Undo / Redo
    // ============================================================
    private void ResetUndo()
    {
        _undo.Clear();
        _redo.Clear();
        if (_activeDoc != null) _undo.Add(MacroSerializer.ToBase64(_activeDoc));
    }

    /// <summary>変更後に呼ぶ。現在状態をスナップショットとして積む。</summary>
    private void RecordSnapshot()
    {
        if (_suppressSnapshot || _activeDoc is null) return;
        SyncToDoc();
        var s = MacroSerializer.ToBase64(_activeDoc);
        if (_undo.Count > 0 && _undo[^1] == s) return;
        _undo.Add(s);
        if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_activeDoc is null || _undo.Count < 2) return;
        SyncToDoc();
        var cur = MacroSerializer.ToBase64(_activeDoc);
        if (_undo[^1] != cur) _undo.Add(cur);   // 未記録の変更があれば確定
        var top = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(top);
        ApplySnapshot(_undo[^1]);
    }

    private void Redo()
    {
        if (_activeDoc is null || _redo.Count == 0) return;
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(s);
        ApplySnapshot(s);
    }

    private void ApplySnapshot(string base64)
    {
        if (_activeDoc is null) return;
        var parsed = MacroSerializer.TryParse(MacroSerializer.Marker + base64);
        if (parsed is null) return;

        _suppressSnapshot = true;
        _editorLoaded = false;

        _activeDoc.Blocks.Clear();
        foreach (var b in parsed.Blocks) _activeDoc.Blocks.Add(b);
        _activeDoc.DisplayName = parsed.DisplayName;
        _activeDoc.Loop = parsed.Loop;
        _activeDoc.LoopCount = parsed.LoopCount;

        DisplayNameBox.Text = parsed.DisplayName;

        _editorLoaded = true;
        UpdateEditorHints();
        UpdatePreview();
        _suppressSnapshot = false;
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

    private void SyncToDoc()
    {
        if (_activeDoc is null) return;
        _activeDoc.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? "新しいマクロ" : DisplayNameBox.Text.Trim();
    }

    private void UpdatePreview()
    {
        if (!_editorLoaded || _activeDoc is null) return;
        SyncToDoc();
        var code = PythonGenerator.Generate(_activeDoc, _activeDoc.FileName ?? "macro1.py");
        PreviewBox.Document = PythonHighlighter.BuildDocument(code);
    }

    /// <summary>編集後の共通処理: プレビュー更新 + Undo スナップショット。</summary>
    private void Edited() { UpdatePreview(); RecordSnapshot(); }

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

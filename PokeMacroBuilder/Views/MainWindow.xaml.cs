using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;
using PokeMacroBuilder.Models;
using PokeMacroBuilder.Services;

namespace PokeMacroBuilder.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private MacroStore? _store;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_settings.LastWorkspace))
                TrySetWorkspace(_settings.LastWorkspace!, silent: true);
        };
    }

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
            {
                MessageBox.Show(this,
                    "選択したフォルダ内に PythonCommands フォルダが見つかりませんでした。\n" +
                    "Poke-Controller のルート、または SerialController フォルダを選んでください。",
                    "ワークスペースエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        _store = store;
        _settings.LastWorkspace = path;
        _settings.Save();
        WorkspacePathText.Text = path;
        RefreshList();
    }

    private void RefreshList()
    {
        if (_store is null) return;
        List<MacroEntry> entries = _store.LoadAll();
        MacroList.ItemsSource = entries;
        EmptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureWorkspace()) return;

        var dlg = new InputDialog("新しいマクロの表示名を入力してください:", "新しいマクロ") { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var doc = new MacroDocument { DisplayName = dlg.ResponseText };
        OpenEditor(doc);
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void MacroList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenSelected();

    private void OpenSelected()
    {
        if (MacroList.SelectedItem is not MacroEntry entry)
        {
            MessageBox.Show(this, "編集するマクロを選択してください。", "情報",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenEditor(entry.Document);
    }

    private void OpenEditor(MacroDocument doc)
    {
        if (_store is null) return;
        var editor = new EditorWindow(_store, doc) { Owner = this };
        editor.ShowDialog();
        RefreshList();
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
}

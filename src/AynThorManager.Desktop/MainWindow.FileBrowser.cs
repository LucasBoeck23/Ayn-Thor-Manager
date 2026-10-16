using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AynThorManager.Core.Models;

namespace AynThorManager.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<FileItem> _files = new();
    private bool _fileListBound;

    private void EnsureFileListBound()
    {
        if (_fileListBound) return;
        FileList.ItemsSource = _files;
        _fileListBound = true;
    }

    private async Task LoadDirectory()
    {
        EnsureFileListBound();
        PathDisplay.Text = _currentPath;
        _files.Clear();

        var result = await _fileService.ListDirectoryAsync(_currentPath, default);
        if (!result.IsSuccess) { ConnMessage.Text = result.Error!.Message; return; }

        foreach (var entry in result.Value!.Entries)
        {
            var isDir = entry.Type == FileEntryType.Directory;
            _files.Add(new FileItem
            {
                Icon = FileItem.IconFor(entry.Name, isDir),
                Name = entry.Name,
                Size = isDir ? "" : FormatSize(entry.SizeBytes),
                Modified = FormatDate(entry.ModifiedAt),
                IsDirectory = isDir,
                FullPath = _currentPath + entry.Name
            });
        }

        EmptyHint.IsVisible = _files.Count == 0;
    }

    private async void FileList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is FileItem item && item.IsDirectory)
        {
            _currentPath = item.FullPath + "/";
            await LoadDirectory();
        }
    }

    private async void BtnUp_Click(object? sender, RoutedEventArgs e)
    {
        var parts = _currentPath.TrimEnd('/').Split('/');
        if (parts.Length > 2)
        {
            _currentPath = string.Join("/", parts[..^1]) + "/";
            await LoadDirectory();
        }
    }

    private async void BtnNewFolder_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Nome da nova pasta:");
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || string.IsNullOrWhiteSpace(dialog.Result)) return;

        var result = await _fileService.CreateDirectoryAsync(_currentPath, dialog.Result, default);
        if (result.IsSuccess) await LoadDirectory();
        else ConnMessage.Text = result.Error!.Message;
    }

    private async void BtnRefresh_Click(object? sender, RoutedEventArgs e) => await LoadDirectory();

    private async void BtnRename_Click(object? sender, RoutedEventArgs e)
    {
        var item = SelectedFile(sender);
        if (item is null) return;

        var dialog = new InputDialog($"Novo nome para \"{item.Name}\":", item.Name);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || string.IsNullOrWhiteSpace(dialog.Result) || dialog.Result == item.Name) return;

        var result = await _fileService.RenameAsync(item.FullPath, dialog.Result, default);
        if (result.IsSuccess) await LoadDirectory();
        else ConnMessage.Text = result.Error!.Message;
    }

    private async void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        var item = SelectedFile(sender);
        if (item is null) return;

        var kind = item.IsDirectory ? "a pasta" : "o arquivo";
        var confirm = new ConfirmDialog(
            "Confirmar exclusão",
            $"Excluir {kind} \"{item.Name}\"?" + (item.IsDirectory ? "\nTodo o conteúdo será removido." : ""));
        if (!await confirm.ShowDialog<bool>(this)) return;

        var result = await _fileService.DeleteAsync(item.FullPath, default);
        if (result.IsSuccess) await LoadDirectory();
        else ConnMessage.Text = result.Error!.Message;
    }

    /// <summary>
    /// Resolves the file item a context-menu/action click targets: the item bound
    /// to the clicked control, falling back to the current list selection.
    /// </summary>
    private FileItem? SelectedFile(object? sender) =>
        (sender as Control)?.DataContext as FileItem
        ?? FileList.SelectedItem as FileItem;
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    private async void BtnNewFile_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Nome do novo documento:", "novo.txt");
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || string.IsNullOrWhiteSpace(dialog.Result)) return;

        var result = await _fileService.CreateFileAsync(_currentPath, dialog.Result, default);
        if (result.IsSuccess) await LoadDirectory();
        else ConnMessage.Text = result.Error!.Message;
    }

    private async void BtnUpload_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Enviar para o Thor",
            AllowMultiple = true,
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();

        await UploadFilesAsync(paths);
    }

    private async void BtnRefresh_Click(object? sender, RoutedEventArgs e) => await LoadDirectory();

    /// <summary>Pushes the given local files into the current directory, one by one.</summary>
    private async Task UploadFilesAsync(IReadOnlyList<string> localPaths)
    {
        if (localPaths.Count == 0) return;

        var done = 0;
        foreach (var local in localPaths)
        {
            ConnMessage.Text = $"Enviando {System.IO.Path.GetFileName(local)} ({++done}/{localPaths.Count})...";
            var result = await _fileService.UploadFileAsync(local, _currentPath, default);
            if (!result.IsSuccess)
            {
                ConnMessage.Text = $"Falha ao enviar {System.IO.Path.GetFileName(local)}: {result.Error!.Message}";
                break;
            }
        }

        ConnMessage.Text = done == localPaths.Count ? $"{done} arquivo(s) enviado(s)." : ConnMessage.Text;
        await LoadDirectory();
    }

    /// <summary>Wires drag-and-drop upload onto the file list. Called once at startup.</summary>
    private void SetupFileDragDrop()
    {
        FileList.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        });

        FileList.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (!e.Data.Contains(DataFormats.Files)) return;

            var paths = (e.Data.GetFiles() ?? [])
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                .Cast<string>()
                .ToList();

            await UploadFilesAsync(paths);
        });
    }

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

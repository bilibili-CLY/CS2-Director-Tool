using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CS2_Director_Tool.App.ViewModels;

namespace CS2_Director_Tool.App.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    private HomeViewModel? ViewModel => DataContext as HomeViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        viewModel.ExecutableFilePicker = PickExecutableFileAsync;
        viewModel.FolderPicker = PickFolderAsync;
        ObsPasswordBox.Text = viewModel.ObsPassword ?? string.Empty;
    }

    private void ObsPasswordBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is TextBox passwordBox)
        {
            viewModel.ObsPassword = passwordBox.Text ?? string.Empty;
        }
    }

    private async Task<string?> PickExecutableFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("可执行文件") { Patterns = new[] { "*" } }
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择回放输出目录",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}

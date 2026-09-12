using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CS2_Director_Tool.App.ViewModels;

namespace CS2_Director_Tool.App.Views;

public partial class MatchInfoPage : UserControl
{
    public MatchInfoPage()
    {
        InitializeComponent();
    }

    private MatchInfoViewModel? ViewModel => DataContext as MatchInfoViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        viewModel.FolderPicker = PickFolderAsync;
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择图片下载目录",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
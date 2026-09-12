using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CS2_Director_Tool.App.ViewModels;

namespace CS2_Director_Tool.App.Views;

public partial class LogPage : UserControl
{
    public LogPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is LogViewModel vm)
            {
                vm.FolderPicker = PickFolderAsync;
                vm.Entries.CollectionChanged += OnEntriesChanged;
            }
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is LogViewModel vm)
                vm.Entries.CollectionChanged -= OnEntriesChanged;
        };
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.ItemCount > 0)
            LogList.ScrollIntoView(LogList.Items[LogList.ItemCount - 1]);
    }

    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择日志存储目录",
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
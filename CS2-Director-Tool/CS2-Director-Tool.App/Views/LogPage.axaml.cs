using System.Collections.Specialized;
using Avalonia.Controls;
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
                vm.Entries.CollectionChanged += OnEntriesChanged;
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
}

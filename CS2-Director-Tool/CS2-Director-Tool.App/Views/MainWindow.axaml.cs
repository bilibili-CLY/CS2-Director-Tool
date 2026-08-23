using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CS2_Director_Tool.App.ViewModels;

namespace CS2_Director_Tool.App.Views;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        ViewModel.TabAccessBlocked += OnTabAccessBlocked;
        Loaded += (_, _) => viewModel.Initialize();
    }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel(() => false);
        DataContext = ViewModel;
    }

    private async void OnTabAccessBlocked(object? sender, string message)
    {
        var text = new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            VerticalAlignment = VerticalAlignment.Center
        };

        var ok = new Button
        {
            Content = "知道了",
            Classes = { "btn", "btn-primary" },
            MinWidth = 110,
            MinHeight = 38
        };

        var root = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 20
        };
        root.Children.Add(text);
        root.Children.Add(ok);

        var dialog = new Window
        {
            Title = "请先配置前置条件",
            Width = 460,
            MinWidth = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = root
        };

        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}
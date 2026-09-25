using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CS2_Director_Tool.App.Services;
using CS2_Director_Tool.App.ViewModels;
using CS2_Director_Tool.App.Views;
using Microsoft.Extensions.DependencyInjection;
using TabItem = CS2_Director_Tool.App.Models.TabItem;

namespace CS2_Director_Tool.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) =>
            {
                try
                {
                    _serviceProvider?.GetRequiredService<ILogService>().Log(LogCategory.App, "应用退出");
                    _serviceProvider?.GetRequiredService<IGsiService>().Stop();
                }
                finally
                {
                    _serviceProvider?.Dispose();
                }
            };

            try
            {
                InitializeApplication(desktop);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("应用启动失败", ex);
                desktop.MainWindow = BuildStartupErrorWindow(ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _serviceProvider = BuildServiceProvider();

        var log = _serviceProvider.GetRequiredService<ILogService>();
        log.Log(LogCategory.App, $"应用启动，日志文件: {log.LogFilePath}");

        var homeVm = _serviceProvider.GetRequiredService<HomeViewModel>();
        var killReplayVm = _serviceProvider.GetRequiredService<KillReplayViewModel>();
        var eventActionVm = _serviceProvider.GetRequiredService<EventActionViewModel>();
        var playerRenameVm = _serviceProvider.GetRequiredService<PlayerRenameViewModel>();
        var matchInfoVm = _serviceProvider.GetRequiredService<MatchInfoViewModel>();
        var logVm = _serviceProvider.GetRequiredService<LogViewModel>();
        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();

        var homePage = new HomePage { DataContext = homeVm };
        var killReplayPage = new KillReplayPage { DataContext = killReplayVm };
        var eventActionPage = new EventActionPage { DataContext = eventActionVm };
        var playerRenamePage = new PlayerRenamePage { DataContext = playerRenameVm };
        var matchInfoPage = new MatchInfoPage { DataContext = matchInfoVm };
        var logPage = new LogPage { DataContext = logVm };

        mainVm.TabList.Add(new TabItem
        {
            Id = "home",
            Title = "主页",
            Content = homePage,
            IsSelected = true
        });

        var killReplayTab = new TabItem { Id = "killReplay", Title = "击杀回放", Content = killReplayPage };
        var eventActionTab = new TabItem { Id = "eventAction", Title = "事件动作", Content = eventActionPage };
        var renameTab = new TabItem { Id = "playerRename", Title = "玩家改名", Content = playerRenamePage };
        var matchInfoTab = new TabItem { Id = "matchInfo", Title = "比赛信息", Content = matchInfoPage };

        mainVm.TabList.Add(killReplayTab);
        mainVm.TabList.Add(eventActionTab);
        mainVm.TabList.Add(renameTab);
        mainVm.TabList.Add(matchInfoTab);

        var logTab = new TabItem { Id = "log", Title = "日志", Content = logPage };
        mainVm.TabList.Add(logTab);

        desktop.MainWindow = new MainWindow(mainVm);

        log.Log(LogCategory.App, "应用启动完成，开始启动 GSI 监听");
        _serviceProvider.GetRequiredService<IGsiService>().Start();
    }

    private static Window BuildStartupErrorWindow(Exception ex)
    {
        var message = $"应用启动失败，详情已写入:{Environment.NewLine}{CrashLogWriter.CrashLogPath}{Environment.NewLine}{Environment.NewLine}{ex}";

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            Margin = new Thickness(16)
        };

        var scroll = new ScrollViewer
        {
            Content = text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var hint = new TextBlock
        {
            Text = "可点击右上角关闭本窗口；修改配置或文件权限后重新启动应用。",
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 12)
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(scroll, 0);
        Grid.SetRow(hint, 1);

        panel.Children.Add(scroll);
        panel.Children.Add(hint);

        return new Window
        {
            Width = 680,
            Height = 460,
            Title = "启动失败",
            Content = panel
        };
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ICs2InstallService, Cs2InstallService>();
        services.AddSingleton<IGsiService, GsiService>();
        services.AddSingleton<IObsService, ObsService>();
        services.AddSingleton<IEventActionService, EventActionService>();
        services.AddSingleton<IReplayWorkflowService, ReplayWorkflowService>();
        services.AddSingleton<IFfmpegService, FfmpegService>();
        services.AddSingleton<IPlayerApiService>(sp =>
            new CSDirectorToolPlayerApiService(sp.GetRequiredService<ISettingsService>().PlayerApiBaseUrl));
        services.AddSingleton<IGameRosterApiService>(sp =>
            new GameRosterApiService(sp.GetRequiredService<ISettingsService>().PlayerApiBaseUrl));
        services.AddSingleton<IImageDownloadService>(sp =>
            new ImageDownloadService(sp.GetRequiredService<ISettingsService>().PlayerApiBaseUrl));

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<KillReplayViewModel>();
        services.AddSingleton<EventActionViewModel>();
        services.AddSingleton<PlayerRenameViewModel>();
        services.AddSingleton<MatchInfoViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<MainViewModel>(sp =>
            new MainViewModel(() => sp.GetRequiredService<HomeViewModel>().PrerequisitesMet));

        return services.BuildServiceProvider();
    }
}
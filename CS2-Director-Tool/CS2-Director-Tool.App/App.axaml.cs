using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.ViewModels;
using CS2_Director_Tool.App.Views;
using CS2_Director_Tool.App.Services;
using Microsoft.Extensions.DependencyInjection;

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
            _serviceProvider = BuildServiceProvider();

            var homeVm = _serviceProvider.GetRequiredService<HomeViewModel>();
            var killReplayVm = _serviceProvider.GetRequiredService<KillReplayViewModel>();
            var eventActionVm = _serviceProvider.GetRequiredService<EventActionViewModel>();
            var playerRenameVm = _serviceProvider.GetRequiredService<PlayerRenameViewModel>();
            var logVm = _serviceProvider.GetRequiredService<LogViewModel>();
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();

            var homePage = new HomePage { DataContext = homeVm };
            var killReplayPage = new KillReplayPage { DataContext = killReplayVm };
            var eventActionPage = new EventActionPage { DataContext = eventActionVm };
            var playerRenamePage = new PlayerRenamePage { DataContext = playerRenameVm };
            var logPage = new LogPage { DataContext = logVm };

            mainVm.TabList.Add(new TabItem
            {
                Id = "home",
                Title = "主页",
                Content = homePage,
                IsSelected = true
            });

            var killReplayTab = new TabItem
            {
                Id = "killReplay",
                Title = "击杀回放",
                Content = killReplayPage
            };
            var eventActionTab = new TabItem
            {
                Id = "eventAction",
                Title = "事件动作",
                Content = eventActionPage
            };
            var renameTab = new TabItem
            {
                Id = "playerRename",
                Title = "玩家改名",
                Content = playerRenamePage
            };

            mainVm.TabList.Add(killReplayTab);
            mainVm.TabList.Add(eventActionTab);
            mainVm.TabList.Add(renameTab);

            var logTab = new TabItem
            {
                Id = "log",
                Title = "日志",
                Content = logPage
            };
            mainVm.TabList.Add(logTab);

            desktop.MainWindow = new MainWindow(mainVm);

            desktop.Exit += (_, _) =>
            {
                try
                {
                    _serviceProvider.GetRequiredService<ILogService>().Log(LogCategory.App, "应用退出");
                    _serviceProvider.GetRequiredService<IGsiService>().Stop();
                }
                finally
                {
                    _serviceProvider.Dispose();
                }
            };

            // 在应用退出前启动 GSI 监听。
            _serviceProvider.GetRequiredService<ILogService>().Log(LogCategory.App, "应用启动");
            _serviceProvider.GetRequiredService<IGsiService>().Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICs2InstallService, Cs2InstallService>();
        services.AddSingleton<IGsiService, GsiService>();
        services.AddSingleton<IObsService, ObsService>();
        services.AddSingleton<IEventActionService, EventActionService>();
        services.AddSingleton<IReplayWorkflowService, ReplayWorkflowService>();
        services.AddSingleton<IFfmpegService, FfmpegService>();
        services.AddSingleton<IPlayerApiService>(sp =>
            new MajoCupPlayerApiService(sp.GetRequiredService<ISettingsService>().PlayerApiBaseUrl));
        services.AddSingleton<ILogService, LogService>();

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<KillReplayViewModel>();
        services.AddSingleton<EventActionViewModel>();
        services.AddSingleton<PlayerRenameViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<MainViewModel>(sp =>
            new MainViewModel(() => sp.GetRequiredService<HomeViewModel>().PrerequisitesMet));

        return services.BuildServiceProvider();
    }
}

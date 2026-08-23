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
            var pauseMusicVm = _serviceProvider.GetRequiredService<PauseMusicViewModel>();
            var playerRenameVm = _serviceProvider.GetRequiredService<PlayerRenameViewModel>();
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();

            var homePage = new HomePage { DataContext = homeVm };
            var killReplayPage = new KillReplayPage { DataContext = killReplayVm };
            var pauseMusicPage = new PauseMusicPage { DataContext = pauseMusicVm };
            var playerRenamePage = new PlayerRenamePage { DataContext = playerRenameVm };

            mainVm.TabList.Add(new TabItem
            {
                Id = "home",
                Title = "主页",
                Content = homePage,
                IsSelected = true
            });

            var killTab = new TabItem
            {
                Id = "killReplay",
                Title = "击杀回放",
                Content = killReplayPage
            };
            var pauseTab = new TabItem
            {
                Id = "pauseMusic",
                Title = "暂停音乐",
                Content = pauseMusicPage
            };
            var renameTab = new TabItem
            {
                Id = "playerRename",
                Title = "玩家改名",
                Content = playerRenamePage
            };

            mainVm.TabList.Add(killTab);
            mainVm.TabList.Add(pauseTab);
            mainVm.TabList.Add(renameTab);

            // 击杀回放结束后，通知暂停音乐视图模型恢复被挂起的音乐播放。
            killReplayVm.ReplayPlaybackEnded += () => pauseMusicVm.OnReplayPlaybackEnded();

            desktop.MainWindow = new MainWindow(mainVm);

            desktop.Exit += (_, _) =>
            {
                try
                {
                    _serviceProvider.GetRequiredService<IGsiService>().Stop();
                }
                finally
                {
                    _serviceProvider.Dispose();
                }
            };

            // 在应用退出前启动 GSI 监听。
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
        services.AddSingleton<IFfmpegService, FfmpegService>();
        services.AddSingleton<IPlayerApiService>(sp =>
            new MajoCupPlayerApiService(sp.GetRequiredService<ISettingsService>().PlayerApiBaseUrl));

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<KillReplayViewModel>();
        services.AddSingleton<PauseMusicViewModel>(sp => new PauseMusicViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IGsiService>(),
            sp.GetRequiredService<IObsService>(),
            () => sp.GetRequiredService<KillReplayViewModel>().IsReplayPlaying));
        services.AddSingleton<PlayerRenameViewModel>();
        services.AddSingleton<MainViewModel>(sp =>
            new MainViewModel(() => sp.GetRequiredService<HomeViewModel>().PrerequisitesMet));

        return services.BuildServiceProvider();
    }
}

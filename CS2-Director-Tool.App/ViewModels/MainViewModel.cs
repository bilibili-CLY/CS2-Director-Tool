using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;
using CS2_Director_Tool.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 管理侧边栏标签页与导航的主视图模型。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private TabItem? _selectedTab;

    private readonly HomeViewModel _home;
    private readonly KillReplayViewModel _killReplay;
    private readonly PauseMusicViewModel _pauseMusic;
    private readonly PlayerRenameViewModel _playerRename;

    /// <summary>侧边栏内容</summary>
    public ObservableCollection<TabItem> TabList { get; } = new();

    /// <summary>获取根据 TabItem 实例选中标签页的命令。</summary>
    public ICommand SelectTabCommand { get; }

    public MainViewModel(HomeViewModel home, KillReplayViewModel killReplay, PauseMusicViewModel pauseMusic,
        PlayerRenameViewModel playerRename)
    {
        _home = home;
        _killReplay = killReplay;
        _pauseMusic = pauseMusic;
        _playerRename = playerRename;

        SelectTabCommand = new RelayCommand<TabItem>(tab =>
        {
            if (tab is not null && tab.IsEnabled)
                SelectedTab = tab;
        });

        TabList.Add(new TabItem
        {
            Id = "home",
            Title = "主页",
            Content = new HomePage { DataContext = _home },
            IsSelected = true
        });

        TabList.Add(new TabItem
        {
            Id = "killReplay",
            Title = "击杀回放",
            Content = new KillReplayPage { DataContext = _killReplay }
        });

        TabList.Add(new TabItem
        {
            Id = "pauseMusic",
            Title = "暂停音乐",
            Content = new PauseMusicPage { DataContext = _pauseMusic }
        });

        TabList.Add(new TabItem
        {
            Id = "playerRename",
            Title = "玩家改名",
            Content = new PlayerRenamePage { DataContext = _playerRename }
        });

        _home.PrerequisitesChanged += (_, _) => UpdatePrerequisites();
    }

    /// <summary>初始化侧边导航栏默认选择并应用前置条件门控。</summary>
    public void Initialize()
    {
        if (TabList.Any())
            SelectedTab = TabList.First();
        UpdatePrerequisites();
    }

    private void UpdatePrerequisites()
    {
        var met = _home.PrerequisitesMet;
        if (TabList.Count > 1)
            TabList[1].IsEnabled = met;
        if (TabList.Count > 2)
            TabList[2].IsEnabled = met;
        if (TabList.Count > 3)
            TabList[3].IsEnabled = met;

        _killReplay.OnPrerequisitesChanged(met);
        _pauseMusic.OnPrerequisitesChanged(met);
    }

    /// <summary>获取或设置当前选中的标签页。</summary>
    public TabItem? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value) && value is not null)
            {
                foreach (var tab in TabList)
                    tab.IsSelected = tab == value;
            }
        }
    }
}

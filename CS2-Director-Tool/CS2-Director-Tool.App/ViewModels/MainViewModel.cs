using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 管理侧边栏标签页与导航的主视图模型。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly Func<bool> _prerequisitesMet;
    private TabItem? _selectedTab;

    /// <summary>
    /// 侧边栏内容
    /// </summary>
    public ObservableCollection<TabItem> TabList { get; } = new();

    /// <summary>
    /// 获取根据 TabItem 实例选中标签页的命令。
    /// </summary>
    public ICommand SelectTabCommand { get; }

    /// <summary>
    /// 在发布环境下点击尚未满足前置条件的标签时触发，参数为需要提示用户的文本。
    /// </summary>
    public event EventHandler<string>? TabAccessBlocked;

    public MainViewModel(Func<bool> prerequisitesMet)
    {
        _prerequisitesMet = prerequisitesMet;
        SelectTabCommand = new RelayCommand<TabItem>(tab =>
        {
            if (tab is null || !tab.IsEnabled)
                return;

            // 开发环境下始终可跳转；发布环境需满足前置条件，否则提示先配置。
            // 日志页为诊断页，始终可直接访问。
            if (tab.Id != HomeTabId && tab.Id != LogTabId && !IsDevelopment && !_prerequisitesMet())
            {
                TabAccessBlocked?.Invoke(this,
                    "请先在「主页」页面配置 CS2 路径 / GSI / FFmpeg / OBS 前置条件后再使用该功能。");
                return;
            }

            SelectedTab = tab;
        });
    }

    private const string HomeTabId = "home";
    private const string LogTabId = "log";

    /// <summary>
    /// 是否为开发环境（Debug 构建，仅用于判断侧边栏是否始终可跳转）。
    /// </summary>
    private static bool IsDevelopment =>
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<DebuggableAttribute>()
            ?.IsJITOptimizerDisabled == true;

    /// <summary>
    /// 初始化侧边导航栏默认选择
    /// </summary>
    public void Initialize()
    {
        // 标签页将由 App 类在创建页面后添加
        if (TabList.Any())
            SelectedTab = TabList.First();
    }

    /// <summary>
    /// 获取或设置当前选中的标签页。
    /// </summary>
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

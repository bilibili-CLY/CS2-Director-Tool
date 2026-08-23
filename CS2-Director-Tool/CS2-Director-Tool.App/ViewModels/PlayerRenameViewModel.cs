using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels
{
    /// <summary>
    /// 玩家改名页面视图模型，使用网站上登记的玩家名称生成并复制
    /// mirv_replace_name 命令。
    /// </summary>
    public partial class PlayerRenameViewModel : ViewModelBase
    {
        private readonly IGsiService _gsiService;
        private readonly IPlayerApiService _playerApiService;
        private string _status;
        private bool _isBusy;

        /// <summary>
        /// 获取或设置按钮下方显示的状态文本。
        /// </summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 获取或设置是否正在获取命令。
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    (GetCommandsCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// 获取用于获取登记名称并将生成的 mirv_replace_name
        /// 命令行复制到剪贴板的命令。
        /// </summary>
        public IRelayCommand GetCommandsCommand { get; }

        /// <summary>
        /// 初始化 <see cref="PlayerRenameViewModel"/> 类的新实例。
        /// </summary>
        /// <param name="gsiService">提供当前玩家快照的 GSI 服务。</param>
        /// <param name="playerApiService">解析玩家登记名称的 API 服务。</param>
        public PlayerRenameViewModel(IGsiService gsiService, IPlayerApiService playerApiService)
        {
            _gsiService = gsiService;
            _playerApiService = playerApiService;
            GetCommandsCommand = new RelayCommand(async () => await GetCommandsAsync(), () => !IsBusy);
        }

        private async Task GetCommandsAsync()
        {
            IsBusy = true;
            try
            {
                var players = _gsiService.GetCurrentPlayers();
                if (players.Count == 0)
                {
                    Status = "未获取到 GSI 玩家数据，请确认 CS2 已在运行且 GSI 生效后再试";
                    return;
                }

                var commands = new List<string>();
                var failures = new List<string>();
                int successCount = 0;
                int notFoundCount = 0;

                foreach (var player in players)
                {
                    try
                    {
                        string registeredName = await _playerApiService.GetRegisteredNameAsync(player.SteamId);
                        if (string.IsNullOrEmpty(registeredName))
                        {
                            notFoundCount++;
                        }
                        else
                        {
                            commands.Add($"mirv_replace_name byXuid add \"{player.SteamId}\" \"{EscapeName(registeredName)}\"");
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{player.SteamId}: {ex.Message}");
                    }
                }

                if (successCount == 0)
                {
                    Status = failures.Count > 0
                        ? $"未获取到任何已登记玩家名（未登记 {notFoundCount}，失败 {failures.Count}）失败原因：{string.Join("；", failures)}"
                        : $"未获取到任何已登记玩家名（未登记 {notFoundCount}）";
                    return;
                }

                try
                {
                    if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                        && lifetime.MainWindow?.Clipboard is { } clipboard)
                    {
                        await clipboard.SetTextAsync(string.Join(";", commands));
                    }
                }
                catch
                {
                    // 剪贴板可能不可用；忽略，但仍显示生成的命令摘要。
                }

                string summary = $"（成功 {successCount}，未登记 {notFoundCount}，失败 {failures.Count}）";
                if (failures.Count > 0)
                    summary += $" 失败原因：{string.Join("；", failures)}";
                Status = "已成功获取并复制到剪贴板，请使用 HLAE 运行游戏，提交到游戏内控制台。" + summary;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 转义反斜杠和双引号，使名称可以安全地嵌入控制台命令字符串。
        /// </summary>
        private static string EscapeName(string name)
        {
            return name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

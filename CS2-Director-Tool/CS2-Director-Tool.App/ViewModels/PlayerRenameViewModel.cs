using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels
{
    /// <summary>
    /// 玩家改名页面视图模型：发现当前玩家（按队伍分类展示），生成
    /// mirv_replace_name 改名命令并支持一键复制。
    /// </summary>
    public partial class PlayerRenameViewModel : ViewModelBase
    {
        private readonly IGsiService _gsiService;
        private readonly IPlayerApiService _playerApiService;
        private readonly ILogService _log;
        private string _status;
        private bool _isBusy;
        private bool _hasPlayers;
        private bool _hasNoTeam;
        private string _commandText = string.Empty;
        private bool _hasCommands;

        /// <summary>恐怖分子（T）队伍玩家列表。</summary>
        public ObservableCollection<GsiPlayerInfo> TTeamPlayers { get; } = new();

        /// <summary>反恐精英（CT）队伍玩家列表。</summary>
        public ObservableCollection<GsiPlayerInfo> CTTeamPlayers { get; } = new();

        /// <summary>未知队伍玩家列表。</summary>
        public ObservableCollection<GsiPlayerInfo> NoTeamPlayers { get; } = new();

        /// <summary>获取或设置状态文本。</summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>获取或设置是否正在处理。</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    DiscoverPlayersCommand.NotifyCanExecuteChanged();
                    GenerateCommandsCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>获取或设置是否已发现玩家（控制生成按钮与列表显示）。</summary>
        public bool HasPlayers
        {
            get => _hasPlayers;
            set
            {
                if (SetProperty(ref _hasPlayers, value))
                    GenerateCommandsCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>获取或设置是否存在未知队伍玩家（控制额外列表显示）。</summary>
        public bool HasNoTeam
        {
            get => _hasNoTeam;
            set => SetProperty(ref _hasNoTeam, value);
        }

        /// <summary>获取或设置生成的改名命令（多行文本）。</summary>
        public string CommandText
        {
            get => _commandText;
            set => SetProperty(ref _commandText, value);
        }

        /// <summary>获取或设置是否已生成命令（控制复制按钮可用）。</summary>
        public bool HasCommands
        {
            get => _hasCommands;
            set => SetProperty(ref _hasCommands, value);
        }

        /// <summary>获取幂发现玩家的命令。</summary>
        public IAsyncRelayCommand DiscoverPlayersCommand { get; }

        /// <summary>获取生成改名命令的命令。</summary>
        public IRelayCommand GenerateCommandsCommand { get; }

        /// <summary>获取将生成的命令复制到剪贴板的命令。</summary>
        public IRelayCommand CopyCommand { get; }

        /// <summary>初始化 <see cref="PlayerRenameViewModel"/> 类的新实例。</summary>
        public PlayerRenameViewModel(IGsiService gsiService, IPlayerApiService playerApiService, ILogService log)
        {
            _gsiService = gsiService;
            _playerApiService = playerApiService;
            _log = log;
            DiscoverPlayersCommand = new AsyncRelayCommand(DiscoverPlayersAsync, () => !IsBusy);
            GenerateCommandsCommand = new RelayCommand(GenerateCommands, () => HasPlayers && !IsBusy);
            CopyCommand = new RelayCommand(CopyToClipboard, () => HasCommands);
        }

        private async Task DiscoverPlayersAsync()
        {
            IsBusy = true;
            try
            {
                var players = _gsiService.GetCurrentPlayers();
                _log.Log(LogCategory.PlayerRename, $"开始发现玩家，当前 GSI 玩家数 {players.Count}");
                if (players.Count == 0)
                {
                    HasPlayers = false;
                    HasCommands = false;
                    CommandText = string.Empty;
                    Status = "未获取到 GSI 玩家数据，请确认 CS2 已在运行且 GSI 生效后再试";
                    _log.Log(LogCategory.PlayerRename, "未获取到 GSI 玩家数据，放弃发现");
                    return;
                }

                ClearLists();
                int notFoundCount = 0;
                int failureCount = 0;

                var tasks = players.Select(player => ResolveRegisteredNameAsync(player));
                var resolved = await Task.WhenAll(tasks);

                foreach (var item in resolved)
                {
                    var player = item.Player;
                    if (item.Failed)
                        failureCount++;
                    else if (!player.IsRegistered)
                        notFoundCount++;

                    switch (player.Team)
                    {
                        case "T":
                            TTeamPlayers.Add(player);
                            break;
                        case "CT":
                            CTTeamPlayers.Add(player);
                            break;
                        default:
                            NoTeamPlayers.Add(player);
                            break;
                    }
                }

                HasPlayers = true;
                HasNoTeam = NoTeamPlayers.Count > 0;
                HasCommands = false;
                CommandText = string.Empty;
                Status = $"共发现 {players.Count} 名玩家：T {TTeamPlayers.Count} 名、CT {CTTeamPlayers.Count} 名"
                         + (HasNoTeam ? $"，未知队伍 {NoTeamPlayers.Count} 名" : string.Empty)
                         + $"（未登记 {notFoundCount}，失败 {failureCount}）。点击“生成改名命令”生成命令。";
                _log.Log(LogCategory.PlayerRename, $"发现玩家完成：共 {players.Count}（T {TTeamPlayers.Count}，CT {CTTeamPlayers.Count}，未知 {NoTeamPlayers.Count}，未登记 {notFoundCount}，失败 {failureCount}）");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<(GsiPlayerInfo Player, bool Failed)> ResolveRegisteredNameAsync(GsiPlayerInfo player)
        {
            try
            {
                string? registeredName = await _playerApiService.GetRegisteredNameAsync(player.SteamId);
                player.IsRegistered = !string.IsNullOrEmpty(registeredName);
                player.RegisteredName = player.IsRegistered ? registeredName! : string.Empty;
                if (!player.IsRegistered)
                    _log.Log(LogCategory.PlayerRename, $"玩家 {player.SteamId} 未登记名称");
                return (player, false);
            }
            catch (Exception ex)
            {
                player.IsRegistered = false;
                player.RegisteredName = string.Empty;
                _log.Log(LogCategory.PlayerRename, $"解析玩家 {player.SteamId} 登记名称失败: {ex.Message}");
                return (player, true);
            }
        }

        private void GenerateCommands()
        {
            var commands = new List<string>();
            int successCount = 0;
            int notFoundCount = 0;
            int failureCount = 0;

            foreach (var player in TTeamPlayers.Concat(CTTeamPlayers).Concat(NoTeamPlayers))
            {
                if (player.IsRegistered && !string.IsNullOrEmpty(player.RegisteredName))
                {
                    commands.Add($"mirv_replace_name byXuid add \"{player.SteamId}\" \"{EscapeName(player.RegisteredName)}\"");
                    successCount++;
                }
                else
                {
                    notFoundCount++;
                }
            }

            CommandText = string.Join(";", commands);
            HasCommands = commands.Count > 0;

            string summary = $"（成功 {successCount}，未登记 {notFoundCount}，失败 {failureCount}）";
            Status = commands.Count > 0
                ? "改名命令已生成，请点击“复制命令”复制到剪贴板后提交到游戏内控制台。" + summary
                : "未能生成任何改名命令，请先确认存在已登记名称的玩家。" + summary;
            _log.Log(LogCategory.PlayerRename, $"已生成改名命令{summary}");
        }

        private void CopyToClipboard()
        {
            if (string.IsNullOrEmpty(CommandText))
                return;

            try
            {
                if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                    && lifetime.MainWindow?.Clipboard is { } clipboard)
                {
                    clipboard.SetTextAsync(CommandText);
                }
            }
            catch
            {
                Status = "复制失败：剪贴板不可用";
                _log.Log(LogCategory.PlayerRename, "复制改名命令失败：剪贴板不可用");
                return;
            }

            Status = "已复制到剪贴板，请在游戏内控制台粘贴执行。";
            _log.Log(LogCategory.PlayerRename, "已复制改名命令到剪贴板");
        }

        private void ClearLists()
        {
            TTeamPlayers.Clear();
            CTTeamPlayers.Clear();
            NoTeamPlayers.Clear();
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
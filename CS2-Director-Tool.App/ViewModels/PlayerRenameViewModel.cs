using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 玩家改名页面视图模型，使用网站上登记的玩家名称生成并复制
/// mirv_replace_name 命令。
/// </summary>
public partial class PlayerRenameViewModel : ViewModelBase
{
    private readonly IGsiService _gsi;
    private readonly IPlayerApiService _playerApi;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    public PlayerRenameViewModel(IGsiService gsi, IPlayerApiService playerApi)
    {
        _gsi = gsi;
        _playerApi = playerApi;
    }

    [RelayCommand]
    private async Task GetCommandsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var players = _gsi.GetCurrentPlayers();
            if (players.Count == 0)
            {
                Status = "未获取到 GSI 玩家数据，请确认 CS2 已在运行且 GSI 生效后再试";
                return;
            }

            var commands = new List<string>();
            foreach (var player in players)
            {
                try
                {
                    var registeredName = await _playerApi.GetRegisteredNameAsync(player.SteamId);
                    if (!string.IsNullOrEmpty(registeredName))
                        commands.Add($"mirv_replace_name \"{player.SteamId}\" \"{registeredName}\"");
                }
                catch (System.Exception ex)
                {
                    Status = $"查询 {player.SteamId} 失败: {ex.Message}";
                }
            }

            if (commands.Count == 0)
            {
                Status = "未找到已登记的玩家名称";
                return;
            }

            var text = string.Join("\n", commands);
            await CopyToClipboardAsync(text);
            Status = $"已生成 {commands.Count} 条改名命令并复制到剪贴板";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        var clipboard = Application.Current?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 比赛信息页面视图模型：输入对局 ID 获取对阵双方名单，
/// 以计分板方式展示战队与队员，支持图片下载与改名命令生成。
/// </summary>
public partial class MatchInfoViewModel : ViewModelBase
{
    private readonly IGameRosterApiService _rosterApi;
    private readonly IImageDownloadService _imageDownload;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    private string _gameId = string.Empty;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _hasResult;
    private bool _hasCommands;
    private string _commandText = string.Empty;
    private string _downloadDirectory = string.Empty;
    private string _lastSavedDirectory = string.Empty;
    private string _lastGameId = string.Empty;
    private RosterTeam? _teamA;
    private RosterTeam? _teamB;

    /// <summary>对局 ID 输入。</summary>
    public string GameId
    {
        get => _gameId;
        set
        {
            if (SetProperty(ref _gameId, value))
                FetchCommand.NotifyCanExecuteChanged();
        }
    }

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
                FetchCommand.NotifyCanExecuteChanged();
                DownloadImagesCommand.NotifyCanExecuteChanged();
                GenerateRenameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>是否已成功获取对阵名单。</summary>
    public bool HasResult
    {
        get => _hasResult;
        set
        {
            if (SetProperty(ref _hasResult, value))
            {
                DownloadImagesCommand.NotifyCanExecuteChanged();
                GenerateRenameCommand.NotifyCanExecuteChanged();
                OpenDownloadFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>是否已生成改名命令。</summary>
    public bool HasCommands
    {
        get => _hasCommands;
        set
        {
            if (SetProperty(ref _hasCommands, value))
                CopyCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>生成的改名命令文本。</summary>
    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

    /// <summary>图片本地保存目录（可编辑，持久化到设置）。</summary>
    public string DownloadDirectory
    {
        get => _downloadDirectory;
        set
        {
            if (SetProperty(ref _downloadDirectory, value))
                _settings.MatchAssetsOutputPath = value;
        }
    }

    /// <summary>A 方战队。</summary>
    public RosterTeam? TeamA
    {
        get => _teamA;
        set => SetProperty(ref _teamA, value);
    }

    /// <summary>B 方战队。</summary>
    public RosterTeam? TeamB
    {
        get => _teamB;
        set => SetProperty(ref _teamB, value);
    }

    /// <summary>获取对阵名单并展示，同时预下载 LOGO 与头像用于显示。</summary>
    public IAsyncRelayCommand FetchCommand { get; }

    /// <summary>下载全部战队 LOGO 与队员头像到本地目录。</summary>
    public IAsyncRelayCommand DownloadImagesCommand { get; }

    /// <summary>浏览选择图片下载目录。</summary>
    public IAsyncRelayCommand BrowseDownloadFolderCommand { get; }

    /// <summary>打开图片下载目录（文件管理器）。</summary>
    public IRelayCommand OpenDownloadFolderCommand { get; }

    /// <summary>生成基于 SteamID 与昵称的改名命令。</summary>
    public IRelayCommand GenerateRenameCommand { get; }

    /// <summary>将改名命令复制到剪贴板。</summary>
    public IRelayCommand CopyCommand { get; }

    /// <summary>由视图注入的文件夹选择器（返回所选文件夹完整路径或 null）。</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>初始化 <see cref="MatchInfoViewModel"/> 类的新实例。</summary>
    public MatchInfoViewModel(IGameRosterApiService rosterApi, IImageDownloadService imageDownload,
        ISettingsService settings, ILogService log)
    {
        _rosterApi = rosterApi;
        _imageDownload = imageDownload;
        _settings = settings;
        _log = log;
        _downloadDirectory = settings.MatchAssetsOutputPath;

        FetchCommand = new AsyncRelayCommand(FetchAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(GameId));
        DownloadImagesCommand = new AsyncRelayCommand(DownloadImagesAsync, () => HasResult && !IsBusy);
        BrowseDownloadFolderCommand = new AsyncRelayCommand(BrowseDownloadFolderAsync);
        OpenDownloadFolderCommand = new RelayCommand(OpenDownloadFolder, () => HasResult);
        GenerateRenameCommand = new RelayCommand(GenerateRename, () => HasResult && !IsBusy);
        CopyCommand = new RelayCommand(CopyToClipboard, () => HasCommands);
    }

    private async Task FetchAsync()
    {
        var gameId = GameId.Trim();
        if (string.IsNullOrWhiteSpace(gameId))
        {
            Status = "请先输入对局 ID";
            return;
        }

        IsBusy = true;
        HasCommands = false;
        CommandText = string.Empty;
        HasResult = false;
        TeamA = null;
        TeamB = null;

        try
        {
            _log.Log(LogCategory.Match, $"开始获取对局名单: {gameId}");
            var roster = await _rosterApi.GetRosterAsync(gameId);

            _lastGameId = roster.GameId;
            TeamA = roster.TeamA;
            TeamB = roster.TeamB;

            if (roster.TeamA is not null)
                await LoadTeamImagesAsync(roster.TeamA);
            if (roster.TeamB is not null)
                await LoadTeamImagesAsync(roster.TeamB);

            HasResult = true;

            var teamACount = roster.TeamA?.Members.Count ?? 0;
            var teamBCount = roster.TeamB?.Members.Count ?? 0;
            Status = $"已获取对局 {roster.GameId} 名单"
                     + (roster.TeamA is null ? "；A 方待定（BYE 轮空）" : $"；A 方「{roster.TeamA.TeamName}」{teamACount} 人")
                     + (roster.TeamB is null ? "；B 方待定（BYE 轮空）" : $"；B 方「{roster.TeamB.TeamName}」{teamBCount} 人");
            _log.Log(LogCategory.Match, $"对局 {roster.GameId} 名单获取完成：A {teamACount} 人，B {teamBCount} 人");
        }
        catch (Exception ex)
        {
            TeamA = null;
            TeamB = null;
            HasResult = false;
            Status = $"获取失败: {ex.Message}";
            _log.Log(LogCategory.Match, $"获取对局 {gameId} 名单失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>预下载战队 LOGO 与全部队员头像（用于页面展示）。</summary>
    private async Task LoadTeamImagesAsync(RosterTeam team)
    {
        try
        {
            var logoBytes = await _imageDownload.DownloadAsync(team.LogoUrl);
            if (logoBytes is not null && logoBytes.Length > 0)
            {
                team.LogoBytes = logoBytes;
                team.LogoImage = CreateBitmap(logoBytes);
            }
        }
        catch (Exception ex)
        {
            _log.Log(LogCategory.Match, $"战队「{team.TeamName}」LOGO 下载失败: {ex.Message}");
        }

        foreach (var member in team.Members)
        {
            try
            {
                var avatarBytes = await _imageDownload.DownloadAsync(member.AvatarUrl);
                if (avatarBytes is not null && avatarBytes.Length > 0)
                {
                    member.AvatarBytes = avatarBytes;
                    member.AvatarImage = CreateBitmap(avatarBytes);
                }
            }
            catch (Exception ex)
            {
                _log.Log(LogCategory.Match, $"队员「{member.DisplayName}」头像下载失败: {ex.Message}");
            }
        }
    }

    private async Task DownloadImagesAsync()
    {
        if (!HasResult)
            return;

        IsBusy = true;
        var gameId = string.IsNullOrWhiteSpace(_lastGameId) ? GameId.Trim() : _lastGameId;
        var baseDir = string.IsNullOrWhiteSpace(DownloadDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CSDirectorTool", "assets")
            : DownloadDirectory;

        try
        {
            Directory.CreateDirectory(baseDir);
            int logoCount = 0;
            int avatarCount = 0;

            if (TeamA is not null)
            {
                Directory.CreateDirectory(Path.Combine(baseDir, gameId, "teamA"));
                logoCount += await SaveTeamImagesAsync(TeamA, Path.Combine(baseDir, gameId, "teamA"));
            }

            if (TeamB is not null)
            {
                Directory.CreateDirectory(Path.Combine(baseDir, gameId, "teamB"));
                logoCount += await SaveTeamImagesAsync(TeamB, Path.Combine(baseDir, gameId, "teamB"));
            }

            _lastSavedDirectory = Path.Combine(baseDir, gameId);
            Status = $"图片已保存到 {_lastSavedDirectory}（LOGO {logoCount} 张，头像 {avatarCount} 张）";
            _log.Log(LogCategory.Match, $"图片已保存到 {_lastSavedDirectory}（LOGO {logoCount} 张，头像 {avatarCount} 张）");
        }
        catch (Exception ex)
        {
            Status = $"图片保存失败: {ex.Message}";
            _log.Log(LogCategory.Match, $"图片保存失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<int> SaveTeamImagesAsync(RosterTeam team, string directory)
    {
        int count = 0;

        if (team.LogoBytes is not null && team.LogoBytes.Length > 0)
        {
            var ext = ImageDownloadService.ResolveExtension(team.LogoUrl);
            await File.WriteAllBytesAsync(Path.Combine(directory, $"logo{ext}"), team.LogoBytes);
            count++;
        }

        int index = 0;
        foreach (var member in team.Members)
        {
            index++;
            if (member.AvatarBytes is null || member.AvatarBytes.Length == 0)
                continue;

            var ext = ImageDownloadService.ResolveExtension(member.AvatarUrl);
            var fileName = $"{index:D2}_{SanitizeFileName(member.DisplayName)}{ext}";
            await File.WriteAllBytesAsync(Path.Combine(directory, fileName), member.AvatarBytes);
            count++;
        }

        return count;
    }

    private void GenerateRename()
    {
        var commands = new List<string>();
        int validCount = 0;

        foreach (var team in new[] { TeamA, TeamB })
        {
            if (team is null)
                continue;

            foreach (var member in team.Members)
            {
                if (string.IsNullOrWhiteSpace(member.SteamId) || string.IsNullOrWhiteSpace(member.DisplayName))
                    continue;

                commands.Add($"mirv_replace_name byXuid add \"{member.SteamId}\" \"{EscapeName(member.DisplayName)}\"");
                validCount++;
            }
        }

        CommandText = string.Join(";", commands);
        HasCommands = commands.Count > 0;

        Status = commands.Count > 0
            ? $"已为 {validCount} 名队员生成改名命令，请点击\"复制命令\"复制到剪贴板后提交到游戏内控制台。"
            : "未能生成改名命令：队员缺少 Steam64 ID 或昵称。";
        _log.Log(LogCategory.Match, $"已生成改名命令 {validCount} 条");
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
            _log.Log(LogCategory.Match, "复制改名命令失败：剪贴板不可用");
            return;
        }

        Status = "已复制到剪贴板，请在游戏内控制台粘贴执行。";
        _log.Log(LogCategory.Match, "已复制改名命令到剪贴板");
    }

    private async Task BrowseDownloadFolderAsync()
    {
        var picker = FolderPicker;
        if (picker is null)
            return;

        var path = await picker();
        if (!string.IsNullOrEmpty(path))
        {
            DownloadDirectory = path;
            _log.Log(LogCategory.Match, $"已选择图片下载目录: {path}");
        }
    }

    private void OpenDownloadFolder()
    {
        var target = !string.IsNullOrEmpty(_lastSavedDirectory) && Directory.Exists(_lastSavedDirectory)
            ? _lastSavedDirectory
            : DownloadDirectory;

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            Status = "图片尚未下载，请先点击\"下载全部图片\"。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = $"打开目录失败: {ex.Message}";
            _log.Log(LogCategory.Match, $"打开目录失败: {ex.Message}");
        }
    }

    /// <summary>将原始字节解码为位图（需在 UI 线程执行）。</summary>
    private static Bitmap CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    /// <summary>
    /// 转义反斜杠和双引号，使名称可以安全地嵌入控制台命令字符串。
    /// </summary>
    private static string EscapeName(string name)
    {
        return name.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>清理文件名中的非法字符，空名回退为占位。 </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "member" : cleaned;
    }
}
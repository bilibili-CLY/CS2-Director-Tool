using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 主页视图模型，提供 CS2、ffmpeg 和 OBS 配置功能。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ICs2InstallService _cs2Install;
    private readonly IObsService _obs;
    private readonly IFfmpegService _ffmpeg;
    private readonly ILogService _log;

    private string _cs2Path = string.Empty;
    private string _ffmpegPath = string.Empty;
    private string _obsAddress = string.Empty;
    private string _obsPort = string.Empty;
    private string _obsPassword = string.Empty;
    private string? _gsiStatus;
    private string _obsStatus = "未连接";
    private bool _isObsConnected;
    private bool _isGsiInstalled;
    private bool _ffmpegValid;
    private string _replayOutputPath = string.Empty;

    private readonly AsyncRelayCommand _toggleObsCommand;

    /// <summary>由视图注入的文件选择器（返回所选文件完整路径或 null）。</summary>
    public Func<Task<string?>>? ExecutableFilePicker { get; set; }

    /// <summary>由视图注入的文件夹选择器（返回所选文件夹完整路径或 null）。</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>前置条件（CS2 路径、GSI、ffmpeg、OBS 连接）满足状态发生变化时触发。</summary>
    public event EventHandler? PrerequisitesChanged;

    /// <summary>OBS 连接状态发生变化时触发，参数为是否已连接。</summary>
    public event EventHandler<bool>? ObsConnectionChanged;

    public string Cs2Path
    {
        get => _cs2Path;
        set
        {
            if (SetProperty(ref _cs2Path, value ?? string.Empty))
            {
                _settings.Cs2Path = _cs2Path;
                IsGsiInstalled = _cs2Install.IsGsiConfigInstalled(_cs2Path);
                RaisePrerequisitesChanged();
            }
        }
    }

    public string FfmpegPath
    {
        get => _ffmpegPath;
        set
        {
            if (SetProperty(ref _ffmpegPath, value ?? string.Empty))
            {
                _settings.FfmpegPath = _ffmpegPath;
                _ffmpegValid = _ffmpeg.ValidatePath(_ffmpegPath);
                RaisePrerequisitesChanged();
            }
        }
    }

    public string ObsAddress
    {
        get => _obsAddress;
        set
        {
            if (SetProperty(ref _obsAddress, value ?? string.Empty))
            {
                _settings.ObsWebSocketAddress = _obsAddress;
                _toggleObsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ObsPort
    {
        get => _obsPort;
        set
        {
            if (SetProperty(ref _obsPort, value ?? string.Empty))
            {
                _settings.ObsWebSocketPort = _obsPort;
                _toggleObsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ObsPassword
    {
        get => _obsPassword;
        set
        {
            if (SetProperty(ref _obsPassword, value ?? string.Empty))
                _settings.ObsWebSocketPassword = _obsPassword;
        }
    }

    public string ReplayOutputPath
    {
        get => _replayOutputPath;
        set
        {
            if (SetProperty(ref _replayOutputPath, value ?? string.Empty))
                _settings.ReplayOutputPath = _replayOutputPath;
        }
    }

    public string? GsiStatus
    {
        get => _gsiStatus;
        set => SetProperty(ref _gsiStatus, value);
    }

    public string ObsStatus
    {
        get => _obsStatus;
        private set => SetProperty(ref _obsStatus, value);
    }

    public bool IsObsConnected
    {
        get => _isObsConnected;
        private set
        {
            if (SetProperty(ref _isObsConnected, value))
                OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public string ConnectButtonText => IsObsConnected ? "断开" : "连接";

    public bool IsGsiInstalled
    {
        get => _isGsiInstalled;
        private set
        {
            if (SetProperty(ref _isGsiInstalled, value))
                OnPropertyChanged(nameof(GsiInstallButtonText));
        }
    }

    public string GsiInstallButtonText => IsGsiInstalled ? "重新安装 GSI" : "安装 GSI";

    public bool PrerequisitesMet =>
        !string.IsNullOrEmpty(Cs2Path) && IsGsiInstalled && _ffmpegValid && IsObsConnected;

    public IAsyncRelayCommand BrowseCs2Command { get; }

    public IAsyncRelayCommand BrowseFfmpegCommand { get; }

    public IAsyncRelayCommand InstallGsiCommand { get; }

    public IAsyncRelayCommand ToggleObsCommand => _toggleObsCommand;

    public IAsyncRelayCommand BrowseReplayOutputPathCommand { get; }

    public HomeViewModel(ISettingsService settings, ICs2InstallService cs2Install, IObsService obs,
        IFfmpegService ffmpeg, ILogService log)
    {
        _settings = settings;
        _cs2Install = cs2Install;
        _obs = obs;
        _ffmpeg = ffmpeg;
        _log = log;

        BrowseCs2Command = new AsyncRelayCommand(BrowseCs2Async);
        BrowseFfmpegCommand = new AsyncRelayCommand(BrowseFfmpegAsync);
        InstallGsiCommand = new AsyncRelayCommand(InstallGsiAsync);
        BrowseReplayOutputPathCommand = new AsyncRelayCommand(BrowseReplayOutputPathAsync);
        _toggleObsCommand = new AsyncRelayCommand(ToggleObsAsync,
            () => !string.IsNullOrEmpty(ObsAddress) && !string.IsNullOrEmpty(ObsPort));

        // 载入已保存的设置。
        Cs2Path = _settings.Cs2Path;
        FfmpegPath = _settings.FfmpegPath;
        ObsAddress = _settings.ObsWebSocketAddress;
        ObsPort = _settings.ObsWebSocketPort;
        ObsPassword = _settings.ObsWebSocketPassword;
        ReplayOutputPath = _settings.ReplayOutputPath;

        _ffmpegValid = !string.IsNullOrEmpty(_ffmpegPath) && _ffmpeg.ValidatePath(_ffmpegPath);
        IsGsiInstalled = !string.IsNullOrEmpty(_cs2Path) && _cs2Install.IsGsiConfigInstalled(_cs2Path);

        _obs.OnConnected += (_, _) =>
        {
            IsObsConnected = true;
            ObsStatus = "已连接";
            ObsConnectionChanged?.Invoke(this, true);
            RaisePrerequisitesChanged();
        };
        _obs.OnDisconnected += (_, _) =>
        {
            IsObsConnected = false;
            ObsStatus = "未连接";
            ObsConnectionChanged?.Invoke(this, false);
            RaisePrerequisitesChanged();
        };
    }

    private void RaisePrerequisitesChanged() => PrerequisitesChanged?.Invoke(this, EventArgs.Empty);

    private async Task<string?> PickFileAsync()
    {
        var picker = ExecutableFilePicker;
        if (picker is null)
            return null;
        return await picker();
    }

    private async Task BrowseCs2Async()
    {
        var path = await PickFileAsync();
        if (!string.IsNullOrEmpty(path))
        {
            Cs2Path = path;
            _log.Log(LogCategory.Home, $"已选择 CS2 路径: {path}");
        }
    }

    private async Task BrowseFfmpegAsync()
    {
        var path = await PickFileAsync();
        if (!string.IsNullOrEmpty(path))
        {
            FfmpegPath = path;
            _log.Log(LogCategory.Home, $"已选择 ffmpeg 路径: {path}");
        }
    }

    private async Task BrowseReplayOutputPathAsync()
    {
        var picker = FolderPicker;
        if (picker is null)
            return;
        var path = await picker();
        if (!string.IsNullOrEmpty(path))
        {
            ReplayOutputPath = path;
            _log.Log(LogCategory.Home, $"已选择回放输出目录: {path}");
        }
    }

    /// <summary>
    /// 安装 GSI 配置文件
    /// </summary>
    private async Task InstallGsiAsync()
    {
        if (string.IsNullOrEmpty(Cs2Path))
        {
            GsiStatus = "错误: 请先填写 CS2 路径";
            _log.Log(LogCategory.Home, "安装 GSI 失败: 未填写 CS2 路径");
            return;
        }

        // 获取内嵌的 GSI 配置文件内容
        var content = LoadEmbeddedGsiConfig();
        if (content is null)
        {
            GsiStatus = "错误: 找不到内嵌的 GSI 配置文件";
            _log.Log(LogCategory.Home, "安装 GSI 失败: 找不到内嵌的 GSI 配置文件");
            return;
        }

        // 获取 CS2 cfg 目录
        var destPath = _cs2Install.InstallGsiConfig(Cs2Path, content);
        if (destPath is null)
        {
            GsiStatus = "错误: 无法定位 CS2 cfg 目录，请检查 CS2 路径";
            IsGsiInstalled = false;
            _log.Log(LogCategory.Home, "安装 GSI 失败: 无法定位 CS2 cfg 目录");
            return;
        }

        IsGsiInstalled = true;
        GsiStatus = $"GSI 已安装: {destPath}";
        _log.Log(LogCategory.Home, $"GSI 已安装到: {destPath}");
        RaisePrerequisitesChanged();
    }

    private async Task ToggleObsAsync()
    {
        if (IsObsConnected)
        {
            try
            {
                _obs.Disconnect();
                IsObsConnected = false;
                ObsStatus = "未连接";
                ObsConnectionChanged?.Invoke(this, false);
                RaisePrerequisitesChanged();
                _log.Log(LogCategory.Home, "已断开 OBS 连接");
            }
            catch (Exception ex)
            {
                ObsStatus = $"断开失败: {ex.Message}";
                _log.Log(LogCategory.Home, $"断开 OBS 失败: {ex.Message}");
            }
            return;
        }

        try
        {
            ObsStatus = "连接中...";
            var url = ObsWebSocketUrlBuilder.Build(ObsAddress, ObsPort);
            _log.Log(LogCategory.Home, $"正在连接 OBS: {url}");
            await _obs.ConnectAsync(url, ObsPassword);
            IsObsConnected = true;
            ObsStatus = "已连接";
            ObsConnectionChanged?.Invoke(this, true);
            RaisePrerequisitesChanged();
            _log.Log(LogCategory.Home, "OBS 连接成功");
        }
        catch (Exception ex)
        {
            IsObsConnected = false;
            ObsStatus = $"连接失败: {ex.Message}";
            ObsConnectionChanged?.Invoke(this, false);
            RaisePrerequisitesChanged();
            _log.Log(LogCategory.Home, $"OBS 连接失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从嵌入的资源中加载 GSI 配置文件内容。
    /// </summary>
    /// <returns>返回 GSI 配置文件内容，或 null 如果加载失败。</returns>
    private static string? LoadEmbeddedGsiConfig()
    {
        const string resourceName = "CS2-Director-Tool.App.Resources.gsi_cfg.cfg";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

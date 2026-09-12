using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 日志页视图模型，集中展示并支持按类别、时间范围与内容筛选全应用日志，
/// 并提供日志文件的位置调整、打开与清空操作。
/// </summary>
public partial class LogViewModel : ViewModelBase
{
    private const int MaxEntries = 3000;
    private const string AllCategory = "全部";

    private readonly ILogService _log;
    private string _selectedCategory = AllCategory;
    private string _startTimeText = string.Empty;
    private string _endTimeText = string.Empty;
    private string _searchText = string.Empty;
    private string _logDirectory;

    public LogViewModel(ILogService log)
    {
        _log = log;
        _logDirectory = log.LogDirectory;

        var options = new List<string> { AllCategory };
        options.AddRange(LogCategory.All);
        CategoryOptions = options;

        ClearCommand = new RelayCommand(() =>
        {
            _log.Clear();
            Entries.Clear();
        });

        ChangeLogDirectoryCommand = new AsyncRelayCommand(ChangeLogDirectoryAsync);
        OpenLogFileCommand = new RelayCommand(OpenLogFile);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        ClearLogFileCommand = new RelayCommand(ClearLogFile);

        _log.EntryAdded += OnEntryAdded;
        _log.LogFileChanged += OnLogFileChanged;
        ApplyFilter();
    }

    /// <summary>类别筛选选项（全部 + 各类别）。</summary>
    public List<string> CategoryOptions { get; }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                ApplyFilter();
        }
    }

    /// <summary>开始时间（格式 yyyy-MM-dd HH:mm:ss，可省略时分秒）。</summary>
    public string StartTimeText
    {
        get => _startTimeText;
        set
        {
            if (SetProperty(ref _startTimeText, value))
                ApplyFilter();
        }
    }

    /// <summary>结束时间（格式 yyyy-MM-dd HH:mm:ss，可省略时分秒）。</summary>
    public string EndTimeText
    {
        get => _endTimeText;
        set
        {
            if (SetProperty(ref _endTimeText, value))
                ApplyFilter();
        }
    }

    /// <summary>内容搜索关键字。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    /// <summary>当前日志文件完整路径。</summary>
    public string LogFilePath => _log.LogFilePath;

    /// <summary>日志存储目录（双向绑定，可由用户直接编辑或通过文件夹选择器修改）。</summary>
    public string LogDirectory
    {
        get => _logDirectory;
        set
        {
            var newValue = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newValue))
                return;
            if (SetProperty(ref _logDirectory, newValue))
                _log.LogDirectory = newValue;
        }
    }

    /// <summary>由视图代码后置注入的文件夹选择器。</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>当前过滤后的日志条目。</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    public IRelayCommand ClearCommand { get; }
    public IAsyncRelayCommand ChangeLogDirectoryCommand { get; }
    public IRelayCommand OpenLogFileCommand { get; }
    public IRelayCommand OpenLogFolderCommand { get; }
    public IRelayCommand ClearLogFileCommand { get; }

    private async Task ChangeLogDirectoryAsync()
    {
        var picker = FolderPicker;
        if (picker is null)
            return;

        var folder = await picker();
        if (string.IsNullOrEmpty(folder))
            return;

        LogDirectory = folder;
        _log.Log(LogCategory.App, $"日志存储目录已调整为: {_log.LogDirectory}");
    }

    private void OpenLogFile()
    {
        var path = _log.LogFilePath;
        if (!File.Exists(path))
        {
            _log.Log(LogCategory.App, $"日志文件不存在: {path}");
            return;
        }

        OpenWithSystem(path);
        _log.Log(LogCategory.App, $"已打开日志文件: {path}");
    }

    private void OpenLogFolder()
    {
        var folder = _log.LogDirectory;
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        OpenWithSystem(folder);
        _log.Log(LogCategory.App, $"已打开日志目录: {folder}");
    }

    private void ClearLogFile()
    {
        _log.ClearFile();
        _log.Log(LogCategory.App, "已清空日志文件");
    }

    private void OnLogFileChanged(object? sender, EventArgs e)
    {
        LogDirectory = _log.LogDirectory;
        OnPropertyChanged(nameof(LogFilePath));
    }

    private static void OpenWithSystem(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"打开路径失败: {ex.Message}");
        }
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (!Matches(entry))
            return;

        Entries.Add(entry);
        Trim();
    }

    private void ApplyFilter()
    {
        var start = ParseStartTime(StartTimeText);
        var end = ParseEndTime(EndTimeText);
        var search = SearchText?.Trim() ?? string.Empty;

        Entries.Clear();
        foreach (var entry in _log.GetEntries())
        {
            if (Matches(entry, start, end, search))
                Entries.Add(entry);
        }
        Trim();
    }

    private bool Matches(LogEntry entry) =>
        Matches(entry, ParseStartTime(StartTimeText), ParseEndTime(EndTimeText), SearchText?.Trim() ?? string.Empty);

    private bool Matches(LogEntry entry, DateTime? start, DateTime? end, string search)
    {
        if (SelectedCategory != AllCategory &&
            !string.Equals(entry.Category, SelectedCategory, StringComparison.Ordinal))
            return false;

        if (start.HasValue && entry.Timestamp < start.Value)
            return false;
        if (end.HasValue && entry.Timestamp > end.Value)
            return false;

        if (!string.IsNullOrEmpty(search) &&
            !entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) &&
            !entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void Trim()
    {
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }

    private static DateTime? ParseStartTime(string text) => ParseTime(text, isEnd: false);

    private static DateTime? ParseEndTime(string text) => ParseTime(text, isEnd: true);

    private static DateTime? ParseTime(string text, bool isEnd)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "HH:mm:ss", "HH:mm" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            // 只给日期时，开始视为当天 00:00:00，结束视为当天 23:59:59。
            if (text.Length <= 10)
                return isEnd ? dt.Date.AddDays(1).AddTicks(-1) : dt.Date;
            return dt;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback
            : (DateTime?)null;
    }
}

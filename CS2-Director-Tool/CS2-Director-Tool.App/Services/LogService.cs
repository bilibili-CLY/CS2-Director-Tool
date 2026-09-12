using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Threading;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 集中式日志服务：线程安全，内存存储 + 文件持久化，超出上限丢弃最旧条目。
/// </summary>
public class LogService : ILogService
{
    private const int MaxEntries = 3000;
    private const string LogFileName = "app.log";

    private readonly object _lock = new object();
    private readonly List<LogEntry> _entries = new List<LogEntry>(MaxEntries);
    private readonly ISettingsService _settings;
    private StreamWriter? _writer;

    public event EventHandler<LogEntry>? EntryAdded;
    public event EventHandler? LogFileChanged;

    public LogService(ISettingsService settings)
    {
        _settings = settings;
        var dir = settings.LogDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CSDirectorTool", "logs");
        _logDirectory = dir;
        OpenWriter();
    }

    private string _logDirectory;

    public string LogDirectory
    {
        get => _logDirectory;
        set
        {
            var newValue = value ?? string.Empty;
            if (string.Equals(_logDirectory, newValue, StringComparison.Ordinal))
                return;

            CloseWriter();
            _logDirectory = newValue;
            _settings.LogDirectory = newValue;
            OpenWriter();
            LogFileChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string LogFilePath => Path.Combine(_logDirectory, LogFileName);

    public void Log(string category, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Category = category,
            Message = message
        };

        Dispatcher.UIThread.Post(() =>
        {
            lock (_lock)
            {
                _entries.Add(entry);
                while (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0);
            }

            WriteToFile(entry);
            EntryAdded?.Invoke(this, entry);
        });
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        });
    }

    public void ClearFile()
    {
        lock (_lock)
        {
            CloseWriter();
            try
            {
                if (File.Exists(LogFilePath))
                    File.WriteAllText(LogFilePath, string.Empty);
            }
            catch
            {
                // 文件操作失败时静默忽略，避免级联崩溃。
            }
            OpenWriter();
        }
    }

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        lock (_lock)
        {
            try
            {
                _writer?.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Category}] {entry.Message}");
                _writer?.Flush();
            }
            catch
            {
                // 写入失败时静默忽略。
            }
        }
    }

    private void OpenWriter()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream) { AutoFlush = false };
        }
        catch
        {
            _writer = null;
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch
        {
            // 关闭失败时静默忽略。
        }
        _writer = null;
    }
}

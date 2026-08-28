using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 集中式内存日志服务：线程安全，统一在 UI 线程更新并触发事件，超出上限丢弃最旧日志。
/// </summary>
public class LogService : ILogService
{
    private const int MaxEntries = 3000;

    private readonly object _lock = new object();
    private readonly List<LogEntry> _entries = new List<LogEntry>(MaxEntries);

    public event EventHandler<LogEntry>? EntryAdded;

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

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }
}

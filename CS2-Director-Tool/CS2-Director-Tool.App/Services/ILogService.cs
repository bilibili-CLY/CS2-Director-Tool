using System;
using System.Collections.Generic;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 集中式日志服务，供全应用（服务层与视图模型）写入与读取诊断日志。
/// </summary>
public interface ILogService
{
    /// <summary>写入一条带类别与时间戳的日志。</summary>
    void Log(string category, string message);

    /// <summary>清空全部日志。</summary>
    void Clear();

    /// <summary>新增日志条目时发生（在 UI 线程触发）。</summary>
    event EventHandler<LogEntry>? EntryAdded;

    /// <summary>返回当前全部日志条目的快照。</summary>
    IReadOnlyList<LogEntry> GetEntries();
}

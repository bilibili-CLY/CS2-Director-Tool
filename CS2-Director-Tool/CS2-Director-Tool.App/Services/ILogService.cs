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

    /// <summary>清空内存中的全部日志。</summary>
    void Clear();

    /// <summary>新增日志条目时发生（在 UI 线程触发）。</summary>
    event EventHandler<LogEntry>? EntryAdded;

    /// <summary>返回当前全部日志条目的快照。</summary>
    IReadOnlyList<LogEntry> GetEntries();

    /// <summary>获取当前日志文件的完整路径。</summary>
    string LogFilePath { get; }

    /// <summary>获取或设置日志存储目录。设置后将切换日志文件并触发 <see cref="LogFileChanged"/>。</summary>
    string LogDirectory { get; set; }

    /// <summary>清空日志文件内容。</summary>
    void ClearFile();

    /// <summary>日志文件路径变更时发生。</summary>
    event EventHandler? LogFileChanged;
}

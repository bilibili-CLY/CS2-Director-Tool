using System;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 一条日志记录。
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

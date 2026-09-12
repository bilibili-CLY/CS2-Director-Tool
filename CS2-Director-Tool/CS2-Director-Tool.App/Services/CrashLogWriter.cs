using System;
using System.IO;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 在应用主日志服务不可用（如启动阶段或发生未处理异常）时，将崩溃信息写入独立文件，
/// 便于甲方在 Windows 下无弹窗退出后定位原因。
/// </summary>
internal static class CrashLogWriter
{
    public static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CSDirectorTool", "logs", "crash.log");

    public static void Write(string source, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}{Environment.NewLine}{ex}{Environment.NewLine}---{Environment.NewLine}";
            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
            // 写入崩溃日志本身失败时无法再做处理。
        }
    }
}
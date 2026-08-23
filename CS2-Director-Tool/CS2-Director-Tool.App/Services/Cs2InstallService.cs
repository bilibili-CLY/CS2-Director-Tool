using System;
using System.IO;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 解析 CS2 安装目录并管理游戏状态集成（GSI）配置文件。
/// </summary>
public class Cs2InstallService : ICs2InstallService
{
    /// <summary>GSI 配置文件名。Valve GSI 规范要求使用 gamestate_integration_ 前缀。</summary>
    public const string GsiConfigFileName = "gamestate_integration_majo_cup.cfg";

    /// <summary>根据 cs2 可执行文件路径解析 CS2 cfg 目录。</summary>
    public string? FindCfgDirectory(string? cs2ExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(cs2ExecutablePath))
            return null;

        var exeDir = Path.GetDirectoryName(cs2ExecutablePath);
        if (string.IsNullOrEmpty(exeDir))
            return null;

        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(exeDir, "csgo", "cfg")),
            Path.GetFullPath(Path.Combine(exeDir, "game", "csgo", "cfg")),
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "csgo", "cfg"))
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>将 GSI 配置写入解析出的 cfg 目录。</summary>
    public string? InstallGsiConfig(string? cs2ExecutablePath, string cfgContent)
    {
        var cfgDir = FindCfgDirectory(cs2ExecutablePath);
        if (cfgDir is null)
            return null;

        var destPath = Path.Combine(cfgDir, GsiConfigFileName);
        File.WriteAllText(destPath, cfgContent);
        return destPath;
    }

    /// <summary>判断解析出的 cfg 目录中是否已存在 GSI 配置文件。</summary>
    public bool IsGsiConfigInstalled(string? cs2ExecutablePath)
    {
        var cfgDir = FindCfgDirectory(cs2ExecutablePath);
        if (cfgDir is null)
            return false;

        return File.Exists(Path.Combine(cfgDir, GsiConfigFileName));
    }
}

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 提供 CS2 安装目录解析与游戏状态集成（GSI）配置安装功能。
/// </summary>
public interface ICs2InstallService
{
    /// <summary>根据 cs2 可执行文件路径解析 CS2 cfg 目录。</summary>
    string? FindCfgDirectory(string? cs2ExecutablePath);

    /// <summary>将 GSI 配置写入解析出的 cfg 目录。</summary>
    string? InstallGsiConfig(string? cs2ExecutablePath, string cfgContent);

    /// <summary>判断解析出的 cfg 目录中是否已存在 GSI 配置文件。</summary>
    bool IsGsiConfigInstalled(string? cs2ExecutablePath);
}

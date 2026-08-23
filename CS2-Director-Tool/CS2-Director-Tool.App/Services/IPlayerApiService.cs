using System.Threading.Tasks;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站 API 解析玩家的登记名称（API 基础地址可配置）。
/// </summary>
public interface IPlayerApiService
{
    /// <summary>
    /// 获取给定 Steam 64 位 ID 对应的登记名称；当玩家未在网站上登记时为 null。
    /// </summary>
    /// <param name="steamId">玩家的 Steam 64 位 ID。</param>
    /// <returns>登记名称；若玩家未登记则为 null。</returns>
    Task<string?> GetRegisteredNameAsync(string steamId);
}

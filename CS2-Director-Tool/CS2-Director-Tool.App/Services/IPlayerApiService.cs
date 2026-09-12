using System.Collections.Generic;
using System.Threading.Tasks;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站 API 批量解析玩家的登记名称（API 基础地址可配置）。
/// </summary>
public interface IPlayerApiService
{
    /// <summary>
    /// 批量获取给定 Steam 64 位 ID 对应的登记名称。仅返回已登记的玩家。
    /// </summary>
    /// <param name="steamIds">玩家的 Steam 64 位 ID 集合。</param>
    /// <returns>键为 SteamID，值为登记名称的字典；未登记的玩家不包含在内。</returns>
    Task<IDictionary<string, string>> GetRegisteredNamesAsync(IEnumerable<string> steamIds);
}

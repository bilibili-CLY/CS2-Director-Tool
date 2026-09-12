using System.Threading.Tasks;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 查询对阵双方名单的接口服务（API 基础地址可配置）。
/// </summary>
public interface IGameRosterApiService
{
    /// <summary>
    /// 按对局 ID 查询对阵双方战队与队员名单。
    /// </summary>
    /// <param name="gameId">对阵图内的对局 ID（Guid）。</param>
    /// <returns>双方名单；<paramref name="gameId"/> 非法或对局不存在时抛出异常。</returns>
    Task<GameRosterData> GetRosterAsync(string gameId);
}
using System;
using System.Net.Http;
using System.Threading.Tasks;
using CS2_Director_Tool.App.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站 API 查询对阵双方名单。API 基础地址由设置提供。
/// </summary>
public class GameRosterApiService : IGameRosterApiService, IDisposable
{
    private const int TimeoutSeconds = 15;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// 初始化 <see cref="GameRosterApiService"/> 类的新实例。
    /// </summary>
    /// <param name="baseUrl">API 基础地址，例如 https://www.yuzibei.cn</param>
    public GameRosterApiService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public async Task<GameRosterData> GetRosterAsync(string gameId)
    {
        var url = $"{_baseUrl}/api/matches/games/{gameId}/roster";

        using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "对局名单接口返回 404：接口可能尚未部署，或该对局不存在/未落定");

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ParseRoster(json);
    }

    private static GameRosterData ParseRoster(string json)
    {
        var root = JObject.Parse(json);
        var success = root["success"]?.Value<bool>() ?? false;
        if (!success)
        {
            var message = root["message"]?.ToString() ?? "未知错误";
            throw new InvalidOperationException($"接口返回失败：{message}");
        }

        var data = root["data"];
        if (data is null)
            throw new InvalidOperationException("接口返回数据为空");

        return data.ToObject<GameRosterData>() ?? throw new InvalidOperationException("接口返回数据解析失败");
    }

    public void Dispose() => _httpClient.Dispose();
}
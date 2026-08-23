using System;
using System.Net.Http;
using System.Threading.Tasks;
using CS2_Director_Tool.App.Services;
using Newtonsoft.Json.Linq;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站 API 解析玩家的登记名称。API 基础地址由设置提供，便于复用为通用 CS2 导播工具。
/// </summary>
public class MajoCupPlayerApiService : IPlayerApiService, IDisposable
{
    private const int TimeoutSeconds = 10;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>初始化 <see cref="MajoCupPlayerApiService"/> 类的新实例。</summary>
    /// <param name="baseUrl">API 基础地址，例如 https://majo-cup.laffeynyaa.com</param>
    public MajoCupPlayerApiService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public async Task<string?> GetRegisteredNameAsync(string steamId)
    {
        var url = $"{_baseUrl}/api/v1/players/steam/{steamId}";
        using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var data = JObject.Parse(json);
        var name = data["name"]?.ToString();

        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public void Dispose() => _httpClient.Dispose();
}

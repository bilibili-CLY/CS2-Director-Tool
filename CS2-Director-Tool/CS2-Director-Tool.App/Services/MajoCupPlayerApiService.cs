using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CS2_Director_Tool.App.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站 API 批量解析玩家的登记名称。API 基础地址由设置提供，便于复用为通用 CS2 导播工具。
/// </summary>
public class MajoCupPlayerApiService : IPlayerApiService, IDisposable
{
    private const int TimeoutSeconds = 10;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>初始化 <see cref="MajoCupPlayerApiService"/> 类的新实例。</summary>
    /// <param name="baseUrl">API 基础地址，例如 https://www.yuzibei.cn</param>
    public MajoCupPlayerApiService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public async Task<IDictionary<string, string>> GetRegisteredNamesAsync(IEnumerable<string> steamIds)
    {
        var idList = new List<string>();
        foreach (var id in steamIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                idList.Add(id.Trim());
        }

        if (idList.Count == 0)
            return new Dictionary<string, string>();

        var url = $"{_baseUrl}/api/players/batch";
        var body = new { steamIds = idList };
        var json = JsonConvert.SerializeObject(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ParseBatchResponse(responseJson);
    }

    private static IDictionary<string, string> ParseBatchResponse(string json)
    {
        var result = new Dictionary<string, string>();
        var root = JObject.Parse(json);
        var data = root["data"];

        if (data is not JArray array)
            return result;

        foreach (var item in array)
        {
            var steamId = item["steamId"]?.ToString();
            var name = item["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(steamId) && !string.IsNullOrWhiteSpace(name))
                result[steamId] = name.Trim();
        }

        return result;
    }

    public void Dispose() => _httpClient.Dispose();
}

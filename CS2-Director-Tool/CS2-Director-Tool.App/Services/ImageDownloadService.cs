using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站下载图片资源，相对路径补全 API 基础地址前缀。
/// </summary>
public class ImageDownloadService : IImageDownloadService, IDisposable
{
    private const int TimeoutSeconds = 15;

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// 初始化 <see cref="ImageDownloadService"/> 类的新实例。
    /// </summary>
    /// <param name="baseUrl">API 基础地址，例如 https://www.yuzibei.cn</param>
    public ImageDownloadService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public async Task<byte[]?> DownloadAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var absoluteUrl = BuildAbsoluteUrl(url);
        using var response = await _httpClient.GetAsync(absoluteUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private string BuildAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return url;

        if (url.StartsWith("/", StringComparison.Ordinal))
            return _baseUrl + url;

        return $"{_baseUrl}/{url}";
    }

    /// <summary>
    /// 从图片地址中解析本地保存使用的扩展名（无扩展名时回退为 png）。
    /// </summary>
    public static string ResolveExtension(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return ".png";

        var path = url.Split('?')[0];
        var dotIndex = path.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < path.Length - 1)
        {
            var ext = path[dotIndex..].ToLowerInvariant();
            if (ext.Length is >= 2 and <= 5 && ext.All(c => char.IsAsciiLetterOrDigit(c) || c == '.'))
                return ext;
        }

        return ".png";
    }

    public void Dispose() => _httpClient.Dispose();
}
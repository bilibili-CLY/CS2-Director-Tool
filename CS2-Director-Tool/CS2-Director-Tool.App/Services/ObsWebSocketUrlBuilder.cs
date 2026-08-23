using System;
using System.Net;
using System.Net.Sockets;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 根据主机与端口构建供 obs-websocket-dotnet 使用的有效 ws:// URL。
/// </summary>
public sealed class ObsWebSocketUrlBuilder
{
    /// <summary>根据给定的主机与端口构建 ws:// URL。</summary>
    /// <exception cref="ArgumentException">当主机或端口为空，或生成的 URL 无效时抛出。</exception>
    public static string Build(string? host, string? port)
    {
        host = host?.Trim();
        port = port?.Trim();

        if (string.IsNullOrEmpty(host))
            throw new ArgumentException("OBS 地址不能为空", nameof(host));

        if (string.IsNullOrEmpty(port))
            throw new ArgumentException("OBS 端口不能为空", nameof(port));

        var url = $"ws://{WrapIpv6IfNeeded(host)}:{port}";

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException($"OBS 地址或端口无效：{host}:{port}");

        return url;
    }

    private static string WrapIpv6IfNeeded(string host)
    {
        if (host.StartsWith("["))
            return host;

        var candidate = host.Trim('[', ']');

        if (IPAddress.TryParse(candidate, out var address)
            && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"[{candidate}]";
        }

        return host;
    }
}

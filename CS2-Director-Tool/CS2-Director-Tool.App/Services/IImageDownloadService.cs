using System.Threading.Tasks;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 从赛事网站下载战队 LOGO / 队员头像等图片资源。
/// </summary>
public interface IImageDownloadService
{
    /// <summary>
    /// 下载图片原始字节。相对路径会补全 API 基础地址前缀。
    /// </summary>
    /// <param name="url">图片地址（可为相对路径）；为 null 或空白时返回 null。</param>
    Task<byte[]?> DownloadAsync(string? url);
}
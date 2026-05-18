using System.Net;
using System.Net.Sockets;

namespace KeyNestForWin.Services;

/// <summary>
/// 站点身份：域名 + 系统 DNS/hosts 解析到的 IP，用于区分同一域名指向不同测试环境。
/// </summary>
public static class SiteIdentityService
{
    /// <summary>从 URL 提取主机名（小写，不含端口）。</summary>
    public static string? NormalizedHost(string raw) => VaultService.NormalizedSiteHostKey(raw);

    /// <summary>解析 URL 对应主机在当前系统下的 IP（遵循 hosts 与 DNS）。</summary>
    public static string? ResolveEndpointForUrl(string raw)
    {
        if (NormalizedHost(raw) is not { } host)
            return null;
        return ResolveEndpointForHost(host);
    }

    public static string? ResolveEndpointForHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;
        try
        {
            var addrs = Dns.GetHostAddresses(host.Trim());
            if (addrs.Length == 0)
                return null;
            var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? addrs[0];
            return ip.ToString().ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static string? HostWithPort(string raw)
    {
        if (NormalizedHost(raw) is not { } host)
            return null;
        var s = raw.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            return host;
        if (uri.IsDefaultPort)
            return host;
        return $"{host}:{uri.Port}";
    }

    /// <summary>站点分组/合并键；未开启 IP 区分时仅主机名。</summary>
    public static string? GetIdentityKey(string url, string? siteEndpoint, bool distinguishByIp)
    {
        var hostPart = HostWithPort(url);
        if (hostPart == null)
            return null;
        if (!distinguishByIp)
            return hostPart;

        var ep = NormalizeEndpoint(siteEndpoint);
        if (string.IsNullOrEmpty(ep))
            ep = ResolveEndpointForUrl(url);
        return string.IsNullOrEmpty(ep) ? hostPart : $"{hostPart}@{ep}";
    }

    public static string? NormalizeEndpoint(string? endpoint)
    {
        var t = endpoint?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>列表分组标题，如 test.example.com · 10.1.2.3</summary>
    public static string FormatGroupTitle(string? identityKey)
    {
        if (string.IsNullOrEmpty(identityKey))
            return "无网站";
        var at = identityKey.LastIndexOf('@');
        if (at <= 0 || at >= identityKey.Length - 1)
            return identityKey;
        return $"{identityKey[..at]} · {identityKey[(at + 1)..]}";
    }

    /// <summary>侧栏站点列：主机 + 可选 IP 标注。</summary>
    public static string FormatSiteDisplay(string url, string? siteEndpoint, bool distinguishByIp)
    {
        var hostPart = HostWithPort(url);
        if (hostPart == null)
            return "";
        if (!distinguishByIp)
            return hostPart;
        var ep = NormalizeEndpoint(siteEndpoint);
        return string.IsNullOrEmpty(ep) ? hostPart : $"{hostPart} ({ep})";
    }

    /// <summary>页面与条目是否属于同一站点环境（域名 + IP）。</summary>
    public static bool ContextsMatch(
        string pageUrl,
        string itemUrl,
        string? itemSiteEndpoint,
        bool distinguishByIp,
        Func<string, string, bool> hostsMatch)
    {
        if (NormalizedHost(pageUrl) is not { } pageHost)
            return false;
        if (NormalizedHost(itemUrl) is not { } itemHost)
            return false;
        if (!hostsMatch(pageHost, itemHost))
            return false;
        if (!distinguishByIp)
            return true;

        var pageEp = ResolveEndpointForUrl(pageUrl);
        var itemEp = NormalizeEndpoint(itemSiteEndpoint);

        // 未绑定 IP 的旧条目：仍按主机名匹配（兼容）
        if (itemEp == null)
            return true;

        if (pageEp == null)
            return false;

        return pageEp == itemEp;
    }
}

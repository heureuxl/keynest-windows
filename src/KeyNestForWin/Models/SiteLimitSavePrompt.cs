namespace KeyNestForWin.Models;

/// <summary>同一网站账号数已达上限、再保存新用户名时需要用户确认。</summary>
public sealed class SiteLimitSavePrompt
{
    public string SiteLabel { get; init; } = "";
    public int MaxAccounts { get; init; }
    public int CurrentCount { get; init; }
    public string IncomingUsername { get; init; } = "";
    public string EvictTitle { get; init; } = "";
    public string EvictUsername { get; init; } = "";

    public string FormatMessage() =>
        $"网站「{SiteLabel}」已保存 {CurrentCount} 个不同账号（上限 {MaxAccounts} 个）。\n\n" +
        $"继续保存账号「{DisplayUsername(IncomingUsername)}」将移除最早条目：\n" +
        $"{EvictTitle}（{DisplayUsername(EvictUsername)}）\n\n是否继续保存？";

    private static string DisplayUsername(string u) =>
        string.IsNullOrWhiteSpace(u) ? "（无用户名）" : u;
}

public sealed class BridgeSiteLimitCheckDto
{
    public bool NeedsConfirm { get; set; }
    public int MaxAccounts { get; set; }
    public int CurrentCount { get; set; }
    public string SiteLabel { get; set; } = "";
    public string EvictTitle { get; set; } = "";
    public string EvictUsername { get; set; } = "";
    public string IncomingUsername { get; set; } = "";
}

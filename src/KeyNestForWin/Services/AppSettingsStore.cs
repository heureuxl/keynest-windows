using System.IO;
using System.Text.Json;

namespace KeyNestForWin.Services;

/// <summary>本地应用设置（明文 JSON，不含保管库密钥）。</summary>
public sealed class AppSettingsStore
{
    private const int DefaultMaxAccountsPerSite = 3;
    private const int MinMaxAccounts = 1;
    private const int MaxMaxAccounts = 99;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    public int MaxAccountsPerSiteHost { get; private set; } = DefaultMaxAccountsPerSite;

    /// <summary>同一域名按 hosts/DNS 解析 IP 区分环境（测试多套系统）。</summary>
    public bool DistinguishHostsByIp { get; private set; } = true;

    public event EventHandler? SettingsChanged;

    public AppSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "KeyNest");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions);
            if (dto != null)
            {
                MaxAccountsPerSiteHost = ClampMaxAccounts(dto.MaxAccountsPerSiteHost);
                DistinguishHostsByIp = dto.DistinguishHostsByIp;
            }
        }
        catch { /* ignore corrupt file */ }
    }

    public void Save(int maxAccountsPerSiteHost, bool distinguishHostsByIp)
    {
        MaxAccountsPerSiteHost = ClampMaxAccounts(maxAccountsPerSiteHost);
        DistinguishHostsByIp = distinguishHostsByIp;
        var dto = new SettingsDto
        {
            MaxAccountsPerSiteHost = MaxAccountsPerSiteHost,
            DistinguishHostsByIp = DistinguishHostsByIp
        };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, JsonOptions));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveMaxAccountsPerSiteHost(int value) => Save(value, DistinguishHostsByIp);

    public static int ClampMaxAccounts(int value) =>
        Math.Clamp(value, MinMaxAccounts, MaxMaxAccounts);

    public static int MinAllowed => MinMaxAccounts;
    public static int MaxAllowed => MaxMaxAccounts;

    private sealed class SettingsDto
    {
        public int MaxAccountsPerSiteHost { get; set; } = DefaultMaxAccountsPerSite;
        public bool DistinguishHostsByIp { get; set; } = true;
    }
}

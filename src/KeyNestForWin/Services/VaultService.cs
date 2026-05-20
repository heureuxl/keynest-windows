using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyNestForWin.Crypto;
using KeyNestForWin.Models;

namespace KeyNestForWin.Services;

public enum VaultError
{
    None,
    InvalidVault,
    WrongPassword,
    WrongRecoveryKey
}

public sealed class VaultService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string VaultPath { get; }

    private readonly AppSettingsStore _settings;

    private string? _masterPassword;
    private byte[]? _dataKey;
    private byte[]? _saltMaster;
    private byte[]? _saltRecovery;
    private byte[]? _wrappedRecovery;

    public bool IsUnlocked { get; private set; }
    public ObservableCollection<PasswordItemDto> Items { get; } = new();
    public string? PendingRecoveryKeyToDisplay { get; private set; }

    public event EventHandler? StateChanged;

    public VaultService(AppSettingsStore settings)
    {
        _settings = settings;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "KeyNest");
        Directory.CreateDirectory(dir);
        VaultPath = Path.Combine(dir, "vault.keynest");
        var oldDir = Path.Combine(appData, "TwoPassword");
        var oldVault = Path.Combine(oldDir, "vault.twopw");
        if (!File.Exists(VaultPath) && File.Exists(oldVault))
        {
            try { File.Copy(oldVault, VaultPath, overwrite: false); } catch { /* ignore */ }
        }
    }

    public bool VaultExists => File.Exists(VaultPath);

    public void AcknowledgeRecoveryKeySaved() => PendingRecoveryKeyToDisplay = null;

    public void Lock()
    {
        _masterPassword = null;
        _dataKey = null;
        _saltMaster = null;
        _saltRecovery = null;
        _wrappedRecovery = null;
        Items.Clear();
        IsUnlocked = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<VaultError> UnlockOrCreateAsync(string password, CancellationToken ct = default)
    {
        await Task.Yield();
        if (string.IsNullOrEmpty(password)) return VaultError.WrongPassword;
        try
        {
            if (VaultExists)
            {
                var data = await File.ReadAllBytesAsync(VaultPath, ct).ConfigureAwait(false);
                var outer = JsonSerializer.Deserialize<VaultFileDto>(data, JsonOptions);
                if (outer == null) return VaultError.InvalidVault;
                if (outer.Version == 1 && outer.Salt != null && outer.Ciphertext != null)
                {
                    await MigrateFromV1Async(password, outer.Salt, outer.Ciphertext, ct).ConfigureAwait(false);
                }
                else
                {
                    if (outer.Version != 2
                        || outer.SaltMaster == null || outer.SaltRecovery == null
                        || outer.WrappedDataKeyMaster == null || outer.WrappedDataKeyRecovery == null
                        || outer.PayloadCiphertext == null)
                        return VaultError.InvalidVault;
                    var km = VaultCryptography.DeriveKey(password, outer.SaltMaster);
                    byte[] dkBytes;
                    try { dkBytes = VaultCryptography.Decrypt(outer.WrappedDataKeyMaster, km); }
                    catch { return VaultError.WrongPassword; }
                    var plain = VaultCryptography.Decrypt(outer.PayloadCiphertext, dkBytes);
                    var payload = JsonSerializer.Deserialize<VaultPayloadDto>(plain, JsonOptions);
                    if (payload?.Items == null) return VaultError.InvalidVault;
                    _masterPassword = password;
                    _dataKey = dkBytes;
                    _saltMaster = outer.SaltMaster;
                    _saltRecovery = outer.SaltRecovery;
                    _wrappedRecovery = outer.WrappedDataKeyRecovery;
                    Items.Clear();
                    foreach (var i in payload.Items)
                        Items.Add(i);
                }
            }
            else
            {
                await CreateNewVaultAsync(password, ct).ConfigureAwait(false);
            }
            IsUnlocked = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return VaultError.None;
        }
        catch
        {
            return VaultError.InvalidVault;
        }
    }

    public async Task<VaultError> UnlockWithRecoveryAsync(string recoveryPhrase, string newMasterPassword, CancellationToken ct = default)
    {
        await Task.Yield();
        var normalized = recoveryPhrase.Trim();
        if (string.IsNullOrEmpty(normalized) || newMasterPassword.Length < 8) return VaultError.WrongRecoveryKey;
        if (!VaultExists) return VaultError.InvalidVault;
        var data = await File.ReadAllBytesAsync(VaultPath, ct).ConfigureAwait(false);
        var outer = JsonSerializer.Deserialize<VaultFileDto>(data, JsonOptions);
        if (outer?.Version != 2
            || outer.SaltRecovery == null || outer.SaltMaster == null
            || outer.WrappedDataKeyRecovery == null || outer.PayloadCiphertext == null)
            return VaultError.InvalidVault;
        var kr = VaultCryptography.DeriveKey(normalized, outer.SaltRecovery);
        byte[] dkBytes;
        try { dkBytes = VaultCryptography.Decrypt(outer.WrappedDataKeyRecovery, kr); }
        catch { return VaultError.WrongRecoveryKey; }
        var plain = VaultCryptography.Decrypt(outer.PayloadCiphertext, dkBytes);
        var payload = JsonSerializer.Deserialize<VaultPayloadDto>(plain, JsonOptions);
        if (payload?.Items == null) return VaultError.InvalidVault;
        _masterPassword = newMasterPassword;
        _dataKey = dkBytes;
        _saltMaster = outer.SaltMaster;
        _saltRecovery = outer.SaltRecovery;
        _wrappedRecovery = outer.WrappedDataKeyRecovery;
        Items.Clear();
        foreach (var i in payload.Items)
            Items.Add(i);
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        IsUnlocked = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return VaultError.None;
    }

    private async Task CreateNewVaultAsync(string password, CancellationToken ct)
    {
        var recoveryPhrase = VaultCryptography.GenerateRecoveryKeyPhrase();
        var saltM = VaultCryptography.RandomSalt();
        var saltR = VaultCryptography.RandomSalt();
        var dkBytes = VaultCryptography.RandomSalt();
        var km = VaultCryptography.DeriveKey(password, saltM);
        var kr = VaultCryptography.DeriveKey(recoveryPhrase, saltR);
        var wM = VaultCryptography.Encrypt(dkBytes, km);
        var wR = VaultCryptography.Encrypt(dkBytes, kr);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(new VaultPayloadDto { Items = new() }, JsonOptions);
        var pc = VaultCryptography.Encrypt(payloadJson, dkBytes);
        _masterPassword = password;
        _dataKey = dkBytes;
        _saltMaster = saltM;
        _saltRecovery = saltR;
        _wrappedRecovery = wR;
        var outer = new VaultFileDto
        {
            Version = 2,
            SaltMaster = saltM,
            SaltRecovery = saltR,
            WrappedDataKeyMaster = wM,
            WrappedDataKeyRecovery = wR,
            PayloadCiphertext = pc
        };
        await File.WriteAllBytesAsync(VaultPath, JsonSerializer.SerializeToUtf8Bytes(outer, JsonOptions), ct).ConfigureAwait(false);
        PendingRecoveryKeyToDisplay = recoveryPhrase;
    }

    private async Task MigrateFromV1Async(string password, byte[] salt, byte[] ciphertext, CancellationToken ct)
    {
        var km = VaultCryptography.DeriveKey(password, salt);
        var plain = VaultCryptography.Decrypt(ciphertext, km);
        var payload = JsonSerializer.Deserialize<VaultPayloadDto>(plain, JsonOptions) ?? new VaultPayloadDto();
        var recoveryPhrase = VaultCryptography.GenerateRecoveryKeyPhrase();
        var saltM = VaultCryptography.RandomSalt();
        var saltR = VaultCryptography.RandomSalt();
        var dkBytes = VaultCryptography.RandomSalt();
        var kmNew = VaultCryptography.DeriveKey(password, saltM);
        var kr = VaultCryptography.DeriveKey(recoveryPhrase, saltR);
        var wM = VaultCryptography.Encrypt(dkBytes, kmNew);
        var wR = VaultCryptography.Encrypt(dkBytes, kr);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var pc = VaultCryptography.Encrypt(body, dkBytes);
        _masterPassword = password;
        _dataKey = dkBytes;
        _saltMaster = saltM;
        _saltRecovery = saltR;
        _wrappedRecovery = wR;
        Items.Clear();
        foreach (var i in payload.Items)
            Items.Add(i);
        var outer = new VaultFileDto
        {
            Version = 2,
            SaltMaster = saltM,
            SaltRecovery = saltR,
            WrappedDataKeyMaster = wM,
            WrappedDataKeyRecovery = wR,
            PayloadCiphertext = pc
        };
        await File.WriteAllBytesAsync(VaultPath, JsonSerializer.SerializeToUtf8Bytes(outer, JsonOptions), ct).ConfigureAwait(false);
        PendingRecoveryKeyToDisplay = recoveryPhrase;
    }

    public async Task PersistAsync(CancellationToken ct = default)
    {
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>在已用主密码解锁的前提下生成新的恢复密钥，旧短语立即失效；返回新短语供界面展示。</summary>
    public async Task<string> RotateRecoveryKeyAsync(CancellationToken ct = default)
    {
        if (!IsUnlocked || _dataKey == null || _masterPassword == null || _saltMaster == null)
            throw new InvalidOperationException("请先使用主密码解锁保管库。");

        var recoveryPhrase = VaultCryptography.GenerateRecoveryKeyPhrase();
        var saltR = VaultCryptography.RandomSalt();
        var kr = VaultCryptography.DeriveKey(recoveryPhrase, saltR);
        var wR = VaultCryptography.Encrypt(_dataKey, kr);

        _saltRecovery = saltR;
        _wrappedRecovery = wR;

        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return recoveryPhrase;
    }

    private async Task PersistVaultV2Async(CancellationToken ct)
    {
        if (_masterPassword == null || _dataKey == null || _saltMaster == null || _saltRecovery == null || _wrappedRecovery == null)
            throw new InvalidOperationException("保管库未解锁");
        DedupeSameHostSameUsername();
        EnforceMaxAccountsPerSiteHost();
        var km = VaultCryptography.DeriveKey(_masterPassword, _saltMaster);
        var wM = VaultCryptography.Encrypt(_dataKey, km);
        var list = Items.ToList();
        var payload = new VaultPayloadDto { Items = list };
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var pc = VaultCryptography.Encrypt(plain, _dataKey);
        var outer = new VaultFileDto
        {
            Version = 2,
            SaltMaster = _saltMaster,
            SaltRecovery = _saltRecovery,
            WrappedDataKeyMaster = wM,
            WrappedDataKeyRecovery = _wrappedRecovery,
            PayloadCiphertext = pc
        };
        await File.WriteAllBytesAsync(VaultPath, JsonSerializer.SerializeToUtf8Bytes(outer, JsonOptions), ct).ConfigureAwait(false);
    }

    /// <summary>合并重复条目：同一主机 + 同一用户名只保留最后一次（仅有 URL 主机时参与）。</summary>
    private string? SiteIdentityKey(PasswordItemDto item) =>
        SiteIdentityService.GetIdentityKey(item.Url, item.SiteEndpoint, _settings.DistinguishHostsByIp);

    private void ApplyAutoSiteEndpoint(PasswordItemDto item, string? pageUrl = null)
    {
        if (!_settings.DistinguishHostsByIp) return;
        if (!string.IsNullOrWhiteSpace(item.SiteEndpoint)) return;
        var resolved = SiteIdentityService.ResolveEndpointForUrl(pageUrl ?? item.Url);
        if (!string.IsNullOrEmpty(resolved))
            item.SiteEndpoint = resolved;
    }

    private void DedupeSameHostSameUsername()
    {
        var snapshot = Items.ToList();
        var keyToLastIndex = new Dictionary<string, int>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            var sk = SiteIdentityKey(snapshot[i]);
            if (sk == null) continue;
            var uk = NormalizeUsernameKey(snapshot[i].Username);
            var key = sk + "\u001f" + uk;
            keyToLastIndex[key] = i;
        }
        var removeIds = new HashSet<Guid>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            var sk = SiteIdentityKey(snapshot[i]);
            if (sk == null) continue;
            var uk = NormalizeUsernameKey(snapshot[i].Username);
            var key = sk + "\u001f" + uk;
            if (keyToLastIndex.TryGetValue(key, out var keepIdx) && keepIdx != i)
                removeIds.Add(snapshot[i].Id);
        }
        foreach (var id in removeIds)
        {
            var found = Items.FirstOrDefault(x => x.Id == id);
            if (found != null)
                Items.Remove(found);
        }
    }

    private void EnforceMaxAccountsPerSiteHost()
    {
        var snapshot = Items.ToList();
        var groups = new Dictionary<string, List<PasswordItemDto>>();
        var keyOrder = new List<string>();
        foreach (var it in snapshot)
        {
            var k = SiteIdentityKey(it);
            if (k == null) continue;
            if (!groups.ContainsKey(k)) keyOrder.Add(k);
            if (!groups.TryGetValue(k, out var g))
            {
                g = new List<PasswordItemDto>();
                groups[k] = g;
            }
            g.Add(it);
        }
        var keep = new HashSet<Guid>();
        foreach (var k in keyOrder)
        {
            var g = groups[k];
            var pick = new List<PasswordItemDto>();
            foreach (var it in g)
            {
                var uk = NormalizeUsernameKey(it.Username);
                var idx = pick.FindIndex(x => NormalizeUsernameKey(x.Username) == uk);
                if (idx >= 0) pick[idx] = it;
                else if (pick.Count < _settings.MaxAccountsPerSiteHost) pick.Add(it);
            }
            foreach (var p in pick) keep.Add(p.Id);
        }
        foreach (var it in snapshot)
        {
            if (SiteIdentityKey(it) == null) keep.Add(it.Id);
        }
        var remove = Items.Where(x => !keep.Contains(x.Id)).ToList();
        foreach (var r in remove)
            Items.Remove(r);
    }

    private static string NormalizeUsernameKey(string username) =>
        username.Trim().ToLowerInvariant();

    /// <summary>站点分组与页面匹配：仅主机名（域名或 IP，小写），不含路径、查询与端口。</summary>
    public static string? NormalizedSiteHostKey(string raw)
    {
        var t = raw.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        var s = t.Contains("://", StringComparison.Ordinal) ? t : "https://" + t;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;
        return uri.Host.ToLowerInvariant();
    }

    /// <summary>新增用户名且已达站点上限时返回确认信息；更新已有用户名或未满则不返回。</summary>
    public SiteLimitSavePrompt? GetSiteLimitSavePrompt(PasswordItemDto item, string? pageUrl = null)
    {
        if (!IsUnlocked) return null;
        var probe = new PasswordItemDto
        {
            Id = item.Id,
            Title = item.Title,
            Username = item.Username,
            Url = item.Url,
            SiteEndpoint = item.SiteEndpoint
        };
        ApplyAutoSiteEndpoint(probe, pageUrl);
        var siteKey = SiteIdentityKey(probe);
        if (siteKey == null) return null;

        var userKey = NormalizeUsernameKey(probe.Username);
        var group = Items.Where(x => SiteIdentityKey(x) == siteKey).ToList();
        if (group.Any(x => NormalizeUsernameKey(x.Username) == userKey))
            return null;
        if (group.Count < _settings.MaxAccountsPerSiteHost)
            return null;

        var oldest = Items.Select((x, idx) => (x, idx))
            .Where(t => SiteIdentityKey(t.x) == siteKey)
            .OrderBy(t => t.idx)
            .FirstOrDefault();
        if (oldest.x == null) return null;

        var siteLabel = SiteIdentityService.FormatGroupTitle(siteKey);
        return new SiteLimitSavePrompt
        {
            SiteLabel = siteLabel,
            MaxAccounts = _settings.MaxAccountsPerSiteHost,
            CurrentCount = group.Count,
            IncomingUsername = probe.Username,
            EvictTitle = string.IsNullOrEmpty(oldest.x.Title) ? "未命名" : oldest.x.Title,
            EvictUsername = oldest.x.Username
        };
    }

    public BridgeSiteLimitCheckDto ToBridgeSiteLimitCheck(SiteLimitSavePrompt prompt) => new()
    {
        NeedsConfirm = true,
        MaxAccounts = prompt.MaxAccounts,
        CurrentCount = prompt.CurrentCount,
        SiteLabel = prompt.SiteLabel,
        EvictTitle = prompt.EvictTitle,
        EvictUsername = prompt.EvictUsername,
        IncomingUsername = prompt.IncomingUsername
    };

    /// <summary>Chrome 扩展保存：按站点环境+用户名合并；<paramref name="pageUrl"/> 用于解析 hosts IP。</summary>
    /// <returns>是否已写入（达上限且未确认时为 false）。</returns>
    public async Task<bool> AddOrUpdateItemAsync(
        PasswordItemDto item,
        string? pageUrl = null,
        bool allowEvictOldest = false,
        CancellationToken ct = default)
    {
        ApplyAutoSiteEndpoint(item, pageUrl);
        return await MergeIncomingBySiteHostAsync(item, ct, allowEvictOldest).ConfigureAwait(false);
    }

    /// <summary>界面新建/编辑：若 Id 已存在则整行替换，否则按主机名规则合并。</summary>
    /// <returns>是否已写入（达上限且未确认时为 false）。</returns>
    public async Task<bool> UpsertItemAsync(
        PasswordItemDto item,
        bool allowEvictOldest = false,
        CancellationToken ct = default)
    {
        ApplyAutoSiteEndpoint(item);
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id != item.Id) continue;
            Items[i] = item;
            EnforceMaxAccountsPerSiteHost();
            await PersistVaultV2Async(ct).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return await MergeIncomingBySiteHostAsync(item, ct, allowEvictOldest).ConfigureAwait(false);
    }

    private async Task<bool> MergeIncomingBySiteHostAsync(
        PasswordItemDto item,
        CancellationToken ct,
        bool allowEvictOldest)
    {
        var siteKey = SiteIdentityKey(item);
        if (siteKey == null)
        {
            Items.Add(item);
            await PersistVaultV2Async(ct).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        var userKey = NormalizeUsernameKey(item.Username);
        var group = Items.Where(x => SiteIdentityKey(x) == siteKey).ToList();
        var hit = group.FirstOrDefault(x => NormalizeUsernameKey(x.Username) == userKey);
        if (hit != null)
        {
            hit.Title = item.Title;
            hit.Username = item.Username;
            hit.Password = item.Password;
            hit.Url = item.Url;
            hit.SiteEndpoint = item.SiteEndpoint;
            hit.Notes = item.Notes;
            if (item.CustomFields.Count > 0)
                hit.CustomFields = item.CustomFields;
            hit.IsFavorite = item.IsFavorite;
            foreach (var dup in group.Where(x => x.Id != hit.Id && NormalizeUsernameKey(x.Username) == userKey).ToList())
                Items.Remove(dup);
        }
        else
        {
            if (group.Count >= _settings.MaxAccountsPerSiteHost)
            {
                if (!allowEvictOldest)
                    return false;
                var oldest = Items.Select((x, idx) => (x, idx))
                    .Where(t => SiteIdentityKey(t.x) == siteKey)
                    .OrderBy(t => t.idx).FirstOrDefault();
                if (oldest.x != null)
                    Items.Remove(oldest.x);
            }
            Items.Add(item);
        }
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task RemoveItemAsync(Guid id, CancellationToken ct = default)
    {
        var found = Items.FirstOrDefault(x => x.Id == id);
        if (found != null) Items.Remove(found);
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        var found = Items.FirstOrDefault(x => x.Id == id);
        if (found == null) return;
        found.IsFavorite = !found.IsFavorite;
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>同一主机 + 同一用户名仅保留一条（保留最后一次）。返回合并删除的条数。</summary>
    /// <summary>按当前设置重新收紧每站点账号上限并落盘（设置变更后调用）。</summary>
    public async Task EnforceLimitsAndPersistAsync(CancellationToken ct = default)
    {
        if (!IsUnlocked) return;
        EnforceMaxAccountsPerSiteHost();
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>将条目移动到目标条目之前（保管库列表顺序）。</summary>
    public async Task MoveItemBeforeAsync(Guid draggedId, Guid targetId, CancellationToken ct = default)
    {
        if (draggedId == targetId) return;
        var dragged = Items.FirstOrDefault(x => x.Id == draggedId);
        var target = Items.FirstOrDefault(x => x.Id == targetId);
        if (dragged == null || target == null) return;
        var from = Items.IndexOf(dragged);
        var to = Items.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        Items.RemoveAt(from);
        if (from < to) to--;
        Items.Insert(to, dragged);
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task MoveItemToEndAsync(Guid draggedId, CancellationToken ct = default)
    {
        var dragged = Items.FirstOrDefault(x => x.Id == draggedId);
        if (dragged == null) return;
        var from = Items.IndexOf(dragged);
        if (from < 0) return;
        if (from == Items.Count - 1) return;
        Items.RemoveAt(from);
        Items.Add(dragged);
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<int> MergeDuplicateHostUsernamesAsync(CancellationToken ct = default)
    {
        var before = Items.Count;
        DedupeSameHostSameUsername();
        if (Items.Count == before) return 0;
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return before - Items.Count;
    }

    /// <summary>浏览器扩展 POST /api/save：同一页 + 用户名已存在且密码一致时可跳过写入。</summary>
    public bool ShouldSkipBridgeSave(string pageUrl, string username, string password)
    {
        var uk = NormalizeUsernameKey(username);
        foreach (var item in MatchCredentialsForPage(pageUrl))
        {
            if (NormalizeUsernameKey(item.Username) != uk) continue;
            return item.Password == password;
        }
        return false;
    }

    public IReadOnlyList<PasswordItemDto> MatchCredentialsForPage(string pageUrl)
    {
        if (!IsUnlocked) return Array.Empty<PasswordItemDto>();
        if (NormalizedSiteHostKey(pageUrl) is null)
            return Array.Empty<PasswordItemDto>();
        return Items
            .Where(item =>
            {
                if (string.IsNullOrEmpty(item.Url)) return false;
                return SiteIdentityService.ContextsMatch(
                    pageUrl, item.Url, item.SiteEndpoint, _settings.DistinguishHostsByIp, HostsMatch);
            })
            .OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>匹配时忽略前缀 www，减少「带 www 的条目 vs 子域登录页」漏配。</summary>
    private static string CanonicalHostForMatch(string host)
    {
        var h = host.Trim().ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }

    private static bool HostsMatch(string pageHost, string itemHost)
    {
        var p = CanonicalHostForMatch(pageHost);
        var i = CanonicalHostForMatch(itemHost);
        if (p == i) return true;
        if (p.EndsWith("." + i, StringComparison.Ordinal) || i.EndsWith("." + p, StringComparison.Ordinal))
            return true;
        // m.site.com 与 www.site.com 等兄弟子域：取注册近似域（最后两段）比对
        static string? ApproxRegistrable(string h)
        {
            var parts = h.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : null;
        }
        var rp = ApproxRegistrable(p);
        var ri = ApproxRegistrable(i);
        if (rp != null && rp == ri) return true;
        return false;
    }
}

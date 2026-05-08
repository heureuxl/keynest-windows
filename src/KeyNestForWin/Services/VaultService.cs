using System.Collections.ObjectModel;
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

    private string? _masterPassword;
    private byte[]? _dataKey;
    private byte[]? _saltMaster;
    private byte[]? _saltRecovery;
    private byte[]? _wrappedRecovery;

    public bool IsUnlocked { get; private set; }
    public ObservableCollection<PasswordItemDto> Items { get; } = new();
    public string? PendingRecoveryKeyToDisplay { get; private set; }

    public event EventHandler? StateChanged;

    public VaultService()
    {
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
                    foreach (var i in payload.Items.OrderBy(x => x.Title))
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

    private async Task PersistVaultV2Async(CancellationToken ct)
    {
        if (_masterPassword == null || _dataKey == null || _saltMaster == null || _saltRecovery == null || _wrappedRecovery == null)
            throw new InvalidOperationException("保管库未解锁");
        EnforceMaxAccountsPerSiteUrl();
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

    private void EnforceMaxAccountsPerSiteUrl()
    {
        var snapshot = Items.ToList();
        var groups = new Dictionary<string, List<PasswordItemDto>>();
        var keyOrder = new List<string>();
        foreach (var it in snapshot)
        {
            var k = NormalizedSiteUrlKey(it.Url);
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
                else if (pick.Count < 3) pick.Add(it);
            }
            foreach (var p in pick) keep.Add(p.Id);
        }
        foreach (var it in snapshot)
        {
            if (NormalizedSiteUrlKey(it.Url) == null) keep.Add(it.Id);
        }
        var remove = Items.Where(x => !keep.Contains(x.Id)).ToList();
        foreach (var r in remove)
            Items.Remove(r);
    }

    private static string NormalizeUsernameKey(string username) =>
        username.Trim().ToLowerInvariant();

    public static string? NormalizedSiteUrlKey(string raw)
    {
        var t = raw.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        var s = t.Contains("://", StringComparison.Ordinal) ? t : "https://" + t;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return t.ToLowerInvariant();
        var scheme = string.IsNullOrEmpty(uri.Scheme) ? "https" : uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path[..^1];
        return $"{scheme}://{host}{path}";
    }

    /// <summary>Chrome 扩展保存：按 URL+用户名合并；新建条目请使用 <see cref="UpsertItemAsync"/>。</summary>
    public async Task AddOrUpdateItemAsync(PasswordItemDto item, CancellationToken ct = default)
    {
        await MergeIncomingBySiteUrlAsync(item, ct).ConfigureAwait(false);
    }

    /// <summary>界面新建/编辑：若 Id 已存在则整行替换，否则按 URL 规则合并。</summary>
    public async Task UpsertItemAsync(PasswordItemDto item, CancellationToken ct = default)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id != item.Id) continue;
            Items[i] = item;
            EnforceMaxAccountsPerSiteUrl();
            await PersistVaultV2Async(ct).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        await MergeIncomingBySiteUrlAsync(item, ct).ConfigureAwait(false);
    }

    private async Task MergeIncomingBySiteUrlAsync(PasswordItemDto item, CancellationToken ct)
    {
        var urlKey = NormalizedSiteUrlKey(item.Url);
        if (urlKey == null)
        {
            Items.Add(item);
            await PersistVaultV2Async(ct).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var userKey = NormalizeUsernameKey(item.Username);
        var group = Items.Where(x => NormalizedSiteUrlKey(x.Url) == urlKey).ToList();
        var hit = group.FirstOrDefault(x => NormalizeUsernameKey(x.Username) == userKey);
        if (hit != null)
        {
            hit.Title = item.Title;
            hit.Username = item.Username;
            hit.Password = item.Password;
            hit.Url = item.Url;
            hit.Notes = item.Notes;
            foreach (var dup in group.Where(x => x.Id != hit.Id && NormalizeUsernameKey(x.Username) == userKey).ToList())
                Items.Remove(dup);
        }
        else
        {
            if (group.Count >= 3)
            {
                var oldest = Items.Select((x, idx) => (x, idx))
                    .Where(t => NormalizedSiteUrlKey(t.x.Url) == urlKey)
                    .OrderBy(t => t.idx).FirstOrDefault();
                if (oldest.x != null)
                    Items.Remove(oldest.x);
            }
            Items.Add(item);
        }
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveItemAsync(Guid id, CancellationToken ct = default)
    {
        var found = Items.FirstOrDefault(x => x.Id == id);
        if (found != null) Items.Remove(found);
        await PersistVaultV2Async(ct).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<PasswordItemDto> MatchCredentialsForPage(string pageUrl)
    {
        if (!IsUnlocked) return Array.Empty<PasswordItemDto>();
        if (NormalizedSiteUrlKey(pageUrl) is { } pageKey)
        {
            var exact = Items.Where(x => NormalizedSiteUrlKey(x.Url) == pageKey)
                .OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase).ToList();
            if (exact.Count > 0) return exact;
        }
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            return Array.Empty<PasswordItemDto>();
        var host = pageUri.Host.ToLowerInvariant();
        return Items.Where(item =>
            {
                if (string.IsNullOrEmpty(item.Url)) return false;
                if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var u)) return false;
                var h = u.Host.ToLowerInvariant();
                return host == h || host.EndsWith("." + h, StringComparison.Ordinal) || h.EndsWith("." + host, StringComparison.Ordinal);
            })
            .OrderBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

using System.IO;
using System.Text.Json;

namespace KeyNestForWin.Services;

/// <summary>记录条目最近一次打开/复制密码的时间（仅存 UUID 与时间戳，不含密钥明文）。</summary>
public sealed class EntryUsageStore
{
    private sealed class FilePayload
    {
        public Dictionary<string, double> LastAccess { get; set; } = new();
    }

    private readonly string _filePath;
    private Dictionary<Guid, double> _lastAccess = new();

    public EntryUsageStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "KeyNest");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "entry-usage.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var p = JsonSerializer.Deserialize<FilePayload>(json);
            if (p?.LastAccess == null) return;
            _lastAccess = new Dictionary<Guid, double>();
            foreach (var (k, v) in p.LastAccess)
            {
                if (Guid.TryParse(k, out var id))
                    _lastAccess[id] = v;
            }
        }
        catch { /* ignore corrupt file */ }
    }

    private void Save()
    {
        var dict = _lastAccess.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        var p = new FilePayload { LastAccess = dict };
        var data = JsonSerializer.SerializeToUtf8Bytes(p);
        File.WriteAllBytes(_filePath, data);
    }

    public void RecordAccess(Guid id)
    {
        _lastAccess[id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Save();
    }

    public void Remove(Guid id)
    {
        if (_lastAccess.Remove(id))
            Save();
    }

    public void Prune(IReadOnlySet<Guid> knownIds)
    {
        var before = _lastAccess.Count;
        _lastAccess = _lastAccess.Where(kv => knownIds.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (_lastAccess.Count != before)
            Save();
    }

    public IReadOnlyList<Guid> SortIdsByRecentFirst(IReadOnlyList<Guid> ids)
    {
        return ids.OrderByDescending(id => _lastAccess.GetValueOrDefault(id, 0))
            .ThenBy(id => id.ToString(), StringComparer.Ordinal)
            .ToList();
    }
}

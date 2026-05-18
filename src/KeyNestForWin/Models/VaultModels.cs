using System.Text.Json.Serialization;

namespace KeyNestForWin.Models;

/// <summary>磁盘保管库文件（与 macOS KeyNest Swift 版 JSON 结构一致）。</summary>
public sealed class VaultFileDto
{
    public int Version { get; set; }

    public byte[]? Salt { get; set; }
    public byte[]? Ciphertext { get; set; }

    public byte[]? SaltMaster { get; set; }
    public byte[]? SaltRecovery { get; set; }
    public byte[]? WrappedDataKeyMaster { get; set; }
    public byte[]? WrappedDataKeyRecovery { get; set; }
    public byte[]? PayloadCiphertext { get; set; }
}

public sealed class VaultPayloadDto
{
    public List<PasswordItemDto> Items { get; set; } = new();
}

public sealed class CustomFieldDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class PasswordItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>绑定解析 IP（hosts/DNS），同一域名不同环境独立存储；空表示不区分 IP。</summary>
    public string? SiteEndpoint { get; set; }
    public string Notes { get; set; } = "";
    public List<CustomFieldDto> CustomFields { get; set; } = new();
    public bool IsFavorite { get; set; }
}

public sealed class BridgeCredentialDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class BridgeSavePayloadDto
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>主界面列表行（含分组键）。</summary>
public sealed class VaultListRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Username { get; init; } = "";
    public string SiteHost { get; init; } = "";
    public string HostGroupKey { get; init; } = "";
    public string HostGroupTitle { get; init; } = "";
    public bool IsFavorite { get; init; }
    public string FavoriteMark => IsFavorite ? "★" : "☆";
    public PasswordItemDto Source { get; init; } = null!;
}

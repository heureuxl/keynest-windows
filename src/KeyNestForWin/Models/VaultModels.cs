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

public sealed class PasswordItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
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

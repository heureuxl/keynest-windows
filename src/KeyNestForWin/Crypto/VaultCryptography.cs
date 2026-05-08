using System.Security.Cryptography;

namespace KeyNestForWin.Crypto;

/// <summary>与 macOS KeyNest VaultCrypto 对齐：PBKDF2-SHA256（310000 轮）、AES-GCM（nonce 12 + 密文 + tag 16）。</summary>
public static class VaultCryptography
{
    public const int Pbkdf2Iterations = 310_000;
    public const int SaltLength = 32;
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static byte[] RandomSalt()
    {
        var buf = new byte[SaltLength];
        RandomNumberGenerator.Fill(buf);
        return buf;
    }

    public static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeyLength);
    }

    public static byte[] Encrypt(byte[] plaintext, byte[] key32)
    {
        if (key32.Length != 32) throw new ArgumentException("AES-256 key must be 32 bytes.");
        var nonce = new byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(key32, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        var combined = new byte[NonceLength + ciphertext.Length + TagLength];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceLength);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceLength, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceLength + ciphertext.Length, TagLength);
        return combined;
    }

    public static byte[] Decrypt(byte[] combined, byte[] key32)
    {
        if (key32.Length != 32) throw new ArgumentException("AES-256 key must be 32 bytes.");
        if (combined.Length < NonceLength + TagLength)
            throw new CryptographicException("密文过短");
        var nonceLen = NonceLength;
        var tagStart = combined.Length - TagLength;
        var cipherLen = combined.Length - nonceLen - TagLength;
        var nonce = combined.AsSpan(0, nonceLen);
        var cipher = combined.AsSpan(nonceLen, cipherLen);
        var tag = combined.AsSpan(tagStart, TagLength);
        var plaintext = new byte[cipherLen];
        using var aes = new AesGcm(key32, TagLength);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    /// <summary>与 Swift generateRecoveryKeyPhrase 一致：32 字节随机数的 Base64。</summary>
    public static string GenerateRecoveryKeyPhrase()
    {
        var bytes = RandomSalt();
        return Convert.ToBase64String(bytes);
    }
}

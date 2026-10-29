using System.Security.Cryptography;
using System.Text;

namespace Legend.Contracts.Payment;

/// <summary>Shared RSA / AES-GCM helpers for WeChat APIv3 and Alipay RSA2.</summary>
public static class PaymentCrypto
{
    public static RSA CreateRsaFromPem(string pemOrPath)
    {
        string pem = LoadPem(pemOrPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    public static string LoadPem(string pemOrPath)
    {
        if (string.IsNullOrWhiteSpace(pemOrPath))
            throw new ArgumentException("PEM empty", nameof(pemOrPath));
        string trimmed = pemOrPath.Trim();
        if (trimmed.Contains("BEGIN", StringComparison.Ordinal))
            return trimmed.Replace("\\n", "\n");
        if (File.Exists(trimmed))
            return File.ReadAllText(trimmed);
        throw new FileNotFoundException($"PEM not found: {trimmed}");
    }

    public static string SignSha256WithRsaBase64(RSA privateKey, string content)
    {
        byte[] sig = privateKey.SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(sig);
    }

    public static bool VerifySha256WithRsaBase64(RSA publicKey, string content, string signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64)) return false;
        try
        {
            byte[] sig = Convert.FromBase64String(signatureBase64.Trim());
            return publicKey.VerifyData(Encoding.UTF8.GetBytes(content), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>WeChat APIv3 resource decrypt (AES-256-GCM). ciphertext/nonce/associatedData are UTF-8 / Base64 per WeChat docs.</summary>
    public static string AesGcmDecrypt(string apiV3Key, string associatedData, string nonce, string ciphertextBase64)
    {
        byte[] key = Encoding.UTF8.GetBytes(apiV3Key);
        if (key.Length != 32)
            throw new InvalidOperationException("WECHAT_API_V3_KEY must be 32 bytes");
        byte[] cipher = Convert.FromBase64String(ciphertextBase64);
        // last 16 bytes = tag
        if (cipher.Length < 16)
            throw new InvalidOperationException("ciphertext too short");
        int plainLen = cipher.Length - 16;
        byte[] plaintext = new byte[plainLen];
        byte[] tag = new byte[16];
        Buffer.BlockCopy(cipher, plainLen, tag, 0, 16);
        byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);
        byte[] ad = Encoding.UTF8.GetBytes(associatedData ?? "");
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonceBytes, cipher.AsSpan(0, plainLen), tag, plaintext, ad);
        return Encoding.UTF8.GetString(plaintext);
    }

    public static string AesGcmEncryptToBase64(string apiV3Key, string associatedData, string nonce, string plaintext)
    {
        byte[] key = Encoding.UTF8.GetBytes(apiV3Key);
        if (key.Length != 32)
            throw new InvalidOperationException("WECHAT_API_V3_KEY must be 32 bytes");
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[16];
        byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);
        byte[] ad = Encoding.UTF8.GetBytes(associatedData ?? "");
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonceBytes, plain, cipher, tag, ad);
        byte[] packed = new byte[cipher.Length + tag.Length];
        Buffer.BlockCopy(cipher, 0, packed, 0, cipher.Length);
        Buffer.BlockCopy(tag, 0, packed, cipher.Length, tag.Length);
        return Convert.ToBase64String(packed);
    }

    public static (RSA Private, RSA Public, string PrivatePem, string PublicPem) GenerateRsa2048()
    {
        using var rsa = RSA.Create(2048);
        string priv = rsa.ExportRSAPrivateKeyPem();
        string pub = rsa.ExportRSAPublicKeyPem();
        // Also export SubjectPublicKeyInfo for ImportFromPem friendliness
        string pubSpki = rsa.ExportSubjectPublicKeyInfoPem();
        var privKey = CreateRsaFromPem(priv);
        var pubKey = CreateRsaFromPem(pubSpki);
        return (privKey, pubKey, priv, pubSpki);
    }
}

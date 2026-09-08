using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace RigBooster.Services;

/// <summary>
/// Local 6-digit key check against an AES-256 encrypted table of "username|sha256(key)" records.
///
/// Threat model, stated plainly: this stops a casual user from opening the file in Notepad. It does
/// NOT stop anyone who decompiles the exe — <see cref="BuildSecret"/> is right there in the binary,
/// and .NET IL is trivial to read. If the key list has to be tamper-proof, it must live server-side
/// and this class has to be replaced by an online check.
/// </summary>
public static class LicenseService
{
    /// <summary>
    /// Replace before shipping, and use the SAME value in tools/LicenseGen when producing
    /// licenses.dat. Anything you generate with a different secret will not decrypt here.
    /// </summary>
    private const string BuildSecret = "PsHiRveTG2Bq4EXx6GiwJliYBTvlxgMbfj7xVxuu";

    private const string ResourceName = "RigBooster.licenses.dat";
    private const int SaltLen = 16, IvLen = 16, Iterations = 200_000;

    private static readonly string ActivationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RigBooster", "activation.dat");

    public static string? ActivatedUser { get; private set; }
    public static bool IsActivated => ActivatedUser is not null;

    /// <summary>True if a previous successful activation is still on disk.</summary>
    public static bool TryRestoreActivation()
    {
        try
        {
            if (!File.Exists(ActivationPath)) return false;
            var plain = Decrypt(File.ReadAllBytes(ActivationPath));
            var parts = plain.Split('|');
            if (parts.Length != 2) return false;

            // Re-check against the embedded table so a revoked key stops working on next launch.
            if (!Table().Any(r => r.User == parts[0] && r.Hash == parts[1])) return false;

            ActivatedUser = parts[0];
            return true;
        }
        catch { return false; }
    }

    public enum Result { Ok, BadFormat, NotFound, TableMissing, TableUnreadable }

    /// <summary>
    /// Keys are 4-32 letters and/or digits. Word keys are allowed, so they are matched
    /// case-insensitively (lowercased before hashing) — a friend typing "Giorgakis" still gets in.
    /// LicenseGen applies the same rule; change both together or the hashes stop lining up.
    /// </summary>
    public static bool IsWellFormedKey(string key)
        => key.Length is >= 4 and <= 32 && key.All(char.IsAsciiLetterOrDigit);

    public static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();

    public static Result Validate(string username, string key, bool remember = true)
    {
        username = username.Trim();
        key = key.Trim();

        if (!IsWellFormedKey(key) || username.Length == 0)
            return Result.BadFormat;

        List<(string User, string Hash)> table;
        try { table = Table(); }
        catch (FileNotFoundException) { return Result.TableMissing; }   // not embedded at build time
        catch { return Result.TableUnreadable; }                        // embedded, but wrong secret
        if (table.Count == 0) return Result.TableMissing;

        var hash = Sha256(NormalizeKey(key));
        var match = table.FirstOrDefault(r =>
            string.Equals(r.User, username, StringComparison.OrdinalIgnoreCase) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(r.Hash), Encoding.ASCII.GetBytes(hash)));

        if (match.User is null) return Result.NotFound;

        ActivatedUser = match.User;
        if (remember) TrySaveActivation(match.User, match.Hash);
        return Result.Ok;
    }

    public static void Deactivate()
    {
        ActivatedUser = null;
        try { if (File.Exists(ActivationPath)) File.Delete(ActivationPath); } catch { /* best effort */ }
    }

    private static void TrySaveActivation(string user, string hash)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ActivationPath)!);
            File.WriteAllBytes(ActivationPath, Encrypt($"{user}|{hash}"));
        }
        catch { /* activation caching is a convenience, not a requirement */ }
    }

    private static List<(string User, string Hash)> Table()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                      ?? throw new FileNotFoundException(ResourceName);
        using var ms = new MemoryStream();
        s.CopyTo(ms);

        // Decrypted in memory only — never written back to disk.
        return Decrypt(ms.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('|'))
            .Select(l => { var p = l.Split('|', 2); return (p[0].Trim(), p[1].Trim()); })
            .ToList();
    }

    public static string Sha256(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    // --- AES-256-CBC, layout: [salt 16][iv 16][ciphertext] ------------------------------------

    public static byte[] Encrypt(string plaintext, string? secret = null)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = DeriveKey(secret ?? BuildSecret, salt);
        aes.GenerateIV();

        using var enc = aes.CreateEncryptor();
        var body = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plaintext), 0, Encoding.UTF8.GetByteCount(plaintext));

        var outBuf = new byte[SaltLen + IvLen + body.Length];
        salt.CopyTo(outBuf, 0);
        aes.IV.CopyTo(outBuf, SaltLen);
        body.CopyTo(outBuf, SaltLen + IvLen);
        return outBuf;
    }

    public static string Decrypt(byte[] blob, string? secret = null)
    {
        if (blob.Length <= SaltLen + IvLen) throw new CryptographicException("License table is truncated.");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = DeriveKey(secret ?? BuildSecret, blob[..SaltLen]);
        aes.IV = blob[SaltLen..(SaltLen + IvLen)];

        using var dec = aes.CreateDecryptor();
        var body = blob[(SaltLen + IvLen)..];
        return Encoding.UTF8.GetString(dec.TransformFinalBlock(body, 0, body.Length));
    }

    private static byte[] DeriveKey(string secret, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, 32);
}

using System.Security.Cryptography;
using System.Text;

// Builds the encrypted licenses.dat that FiveMTweaks embeds.
//
//   dotnet run --project tools/LicenseGen -- new  <secret> <username> [count]
//   dotnet run --project tools/LicenseGen -- pack <secret> <users.txt> <out.dat>
//   dotnet run --project tools/LicenseGen -- show <secret> <licenses.dat>
//
// <secret> MUST equal LicenseService.BuildSecret in the app, or the table will not decrypt.
// users.txt holds one "username,key" pair per line; the keys are hashed before they are written,
// so the .dat never contains a key in any recoverable form. A key is 4-32 letters and/or digits and
// is matched case-insensitively; "new" generates random 6-digit ones, but a word key is fine too.

const int SaltLen = 16, IvLen = 16, Iterations = 200_000;

if (args.Length < 2) { Usage(); return 1; }
var mode = args[0].ToLowerInvariant();
var secret = args[1];

switch (mode)
{
    case "new":
    {
        if (args.Length < 3) { Usage(); return 1; }
        var user = args[2];
        var count = args.Length > 3 && int.TryParse(args[3], out var c) ? c : 1;
        for (var i = 0; i < count; i++)
        {
            var key = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var name = count == 1 ? user : $"{user}{i + 1}";
            Console.WriteLine($"{name},{key}");
        }
        Console.Error.WriteLine("\nSave those pairs somewhere safe — the key itself is never recoverable from the .dat.");
        Console.Error.WriteLine("Then: licensegen pack <secret> users.txt src/FiveMTweaks/licenses.dat");
        return 0;
    }

    case "pack":
    {
        if (args.Length < 4) { Usage(); return 1; }
        var lines = File.ReadAllLines(args[2])
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        var sb = new StringBuilder();
        var n = 0;
        foreach (var line in lines)
        {
            var parts = line.Split(',', 2);
            if (parts.Length != 2) { Console.Error.WriteLine($"Skipping malformed line: {line}"); continue; }

            var user = parts[0].Trim();
            var key = parts[1].Trim();
            if (!IsWellFormedKey(key))
            {
                Console.Error.WriteLine($"Skipping {user}: key must be 4-32 letters or numbers.");
                continue;
            }

            // Lowercased before hashing so the app can match case-insensitively. Must stay in step
            // with LicenseService.NormalizeKey.
            sb.Append(user).Append('|').Append(Sha256(key.ToLowerInvariant())).Append('\n');
            n++;
        }

        if (n == 0) { Console.Error.WriteLine("Nothing valid to pack."); return 1; }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
        File.WriteAllBytes(args[3], Encrypt(sb.ToString(), secret));
        Console.WriteLine($"Wrote {n} record(s) to {args[3]}. Rebuild the app to embed it.");
        return 0;
    }

    case "show":
    {
        if (args.Length < 3) { Usage(); return 1; }
        Console.WriteLine(Decrypt(File.ReadAllBytes(args[2]), secret));
        return 0;
    }

    default:
        Usage();
        return 1;
}

static void Usage() => Console.Error.WriteLine(
    """
    licensegen new  <secret> <username> [count]   generate username,key pairs (prints them once)
    licensegen pack <secret> <users.txt> <out>    encrypt username|hash records into licenses.dat
    licensegen show <secret> <licenses.dat>       dump the records in a .dat (hashes only)
    """);

static bool IsWellFormedKey(string key) =>
    key.Length is >= 4 and <= 32 && key.All(char.IsAsciiLetterOrDigit);

static string Sha256(string s) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

static byte[] Encrypt(string plaintext, string secret)
{
    var salt = RandomNumberGenerator.GetBytes(SaltLen);
    using var aes = Aes.Create();
    aes.KeySize = 256;
    aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, 32);
    aes.GenerateIV();

    using var enc = aes.CreateEncryptor();
    var bytes = Encoding.UTF8.GetBytes(plaintext);
    var body = enc.TransformFinalBlock(bytes, 0, bytes.Length);

    var outBuf = new byte[SaltLen + IvLen + body.Length];
    salt.CopyTo(outBuf, 0);
    aes.IV.CopyTo(outBuf, SaltLen);
    body.CopyTo(outBuf, SaltLen + IvLen);
    return outBuf;
}

static string Decrypt(byte[] blob, string secret)
{
    using var aes = Aes.Create();
    aes.KeySize = 256;
    aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), blob[..SaltLen], Iterations, HashAlgorithmName.SHA256, 32);
    aes.IV = blob[SaltLen..(SaltLen + IvLen)];

    using var dec = aes.CreateDecryptor();
    var body = blob[(SaltLen + IvLen)..];
    return Encoding.UTF8.GetString(dec.TransformFinalBlock(body, 0, body.Length));
}

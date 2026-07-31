using System.Security.Cryptography;
using System.Text.Json;
using FiveMCleaner.UpdateRuntime;

if (args.Length == 3 && args[0] == "generate-key")
{
    var password = RequirePassword();
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    WriteExclusive(args[1], key.ExportEncryptedPkcs8PrivateKeyPem(password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000)));
    WriteExclusive(args[2], key.ExportSubjectPublicKeyInfoPem());
    return 0;
}

if (args.Length == 8 && args[0] == "sign")
{
    var package = Path.GetFullPath(args[1]);
    using var stream = File.OpenRead(package);
    var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    var unsigned = new SignedReleaseManifest(args[2], args[3], args[4], args[5], hash, stream.Length, "");
    using var key = ECDsa.Create();
    key.ImportFromEncryptedPem(File.ReadAllText(args[6]), RequirePassword());
    var signed = unsigned with { SignatureBase64 = Convert.ToBase64String(key.SignData(unsigned.CanonicalPayload(), HashAlgorithmName.SHA256)) };
    var output = Path.GetFullPath(args[7]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(signed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
    return 0;
}

if (args.Length == 4 && args[0] == "verify")
{
    var manifest = JsonSerializer.Deserialize<SignedReleaseManifest>(
        File.ReadAllText(args[1]),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Manifesto inválido.");
    using var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(args[2]));
    ReleaseTrustPolicy.Verify(manifest, key.ExportSubjectPublicKeyInfo(), args[3]);
    return 0;
}

Console.Error.WriteLine("Uso: generate-key <private.pem> <public.pem> | sign <package.zip> <channel> <version> <minimum> <url> <private.pem> <manifest.json> | verify <manifest.json> <public.pem> <highest-version>");
return 2;

static string RequirePassword() => Environment.GetEnvironmentVariable("FIVEMCLEANER_SIGNING_PASSWORD") is { Length: >= 16 } value
    ? value
    : throw new InvalidOperationException("Defina FIVEMCLEANER_SIGNING_PASSWORD com pelo menos 16 caracteres.");

static void WriteExclusive(string path, string content)
{
    var fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var writer = new StreamWriter(stream);
    writer.Write(content);
}

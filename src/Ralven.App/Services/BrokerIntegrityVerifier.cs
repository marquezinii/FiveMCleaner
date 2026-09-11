using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ralven.Contracts;
using Ralven.UpdateRuntime;

namespace Ralven.App.Services;

internal static class BrokerIntegrityVerifier
{
    private const int MaximumBrokerFiles = 512;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IDisposable VerifyBeforeElevation(string baseDirectory, string brokerPath)
    {
        try
        {
            var requireSignedManifest = IsVersionedRuntimeLayout(baseDirectory);
            using var resource = typeof(BrokerIntegrityVerifier).Assembly.GetManifestResourceStream(
                "Ralven.App.Assets.broker-integrity-public-key.pem")
                ?? throw new InvalidOperationException("A chave pública de integridade do broker não foi incorporada ao aplicativo.");
            using var reader = new StreamReader(resource, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(reader.ReadToEnd());
            return Verify(
                baseDirectory,
                brokerPath,
                verifier.ExportSubjectPublicKeyInfo(),
                requireSignedManifest);
        }
        catch (Exception exception) when (exception is not (
            OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            throw new BrokerIntegrityException(exception);
        }
    }

    internal static IDisposable Verify(
        string baseDirectory,
        string brokerPath,
        ReadOnlySpan<byte> publicKey,
        bool requireSignedManifest)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var expectedBroker = Path.GetFullPath(Path.Combine(root, "broker", "Ralven.Broker.exe"));
        var resolvedBroker = Path.GetFullPath(brokerPath);
        if (!resolvedBroker.Equals(expectedBroker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O caminho do componente administrativo é inválido.");
        }

        var signedManifestPath = Path.Combine(root, BrokerTrustPolicy.ManifestFileName);
        if (!File.Exists(signedManifestPath))
        {
            if (requireSignedManifest)
            {
                throw new InvalidDataException(
                    "A instalação não contém o manifesto assinado do componente administrativo.");
            }

            return EmptyLease.Instance;
        }

        var leases = new List<FileStream>();
        try
        {
            EnsurePlainPath(root, Path.Combine(root, "broker"));
            var signedManifestStream = OpenLockedRead(signedManifestPath);
            leases.Add(signedManifestStream);
            if (signedManifestStream.Length is <= 0 or > BrokerTrustPolicy.MaximumSignedManifestBytes)
            {
                throw new InvalidDataException("Manifesto assinado do componente administrativo inválido.");
            }

            var signedManifest = JsonSerializer.Deserialize<SignedBrokerManifest>(
                signedManifestStream,
                JsonOptions)
                ?? throw new InvalidDataException("Manifesto assinado do componente administrativo vazio.");
            var expectedVersion = new DirectoryInfo(root).Name;
            BrokerTrustPolicy.VerifySignature(signedManifest, publicKey, expectedVersion);

            var fileManifestPath = ResolveContainedPath(root, signedManifest.FileManifestRelativePath);
            var fileManifestStream = OpenLockedRead(fileManifestPath);
            leases.Add(fileManifestStream);
            if (fileManifestStream.Length is <= 0 or > BrokerTrustPolicy.MaximumFileManifestBytes)
            {
                throw new InvalidDataException("Manifesto de arquivos do componente administrativo inválido.");
            }

            var actualManifestHash = SHA256.HashData(fileManifestStream);
            var expectedManifestHash = Convert.FromHexString(signedManifest.FileManifestSha256);
            if (!CryptographicOperations.FixedTimeEquals(actualManifestHash, expectedManifestHash))
            {
                throw new CryptographicException("O manifesto de arquivos do componente administrativo foi alterado.");
            }

            fileManifestStream.Position = 0;
            var brokerDirectory = Path.GetDirectoryName(fileManifestPath)!;
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var manifestReader = new StreamReader(
                fileManifestStream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true))
            {
                while (manifestReader.ReadLine() is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (declared.Count >= MaximumBrokerFiles)
                    {
                        throw new InvalidDataException("O componente administrativo excede o limite de arquivos.");
                    }

                    var separator = line.IndexOf("  ", StringComparison.Ordinal);
                    if (separator != 64 || !line.AsSpan(0, 64).ContainsOnlyAsciiHexDigits())
                    {
                        throw new InvalidDataException("Manifesto de arquivos do componente administrativo inválido.");
                    }

                    var relative = line[(separator + 2)..];
                    if (relative.Contains('\\') || !declared.Add(relative))
                    {
                        throw new InvalidDataException("Manifesto de arquivos do componente administrativo inválido.");
                    }

                    var filePath = ResolveContainedPath(brokerDirectory, relative);
                    var fileStream = OpenLockedRead(filePath);
                    leases.Add(fileStream);
                    var actualHash = SHA256.HashData(fileStream);
                    var expectedHash = Convert.FromHexString(line[..64]);
                    if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                    {
                        throw new CryptographicException(
                            "Um arquivo do componente administrativo foi alterado.");
                    }
                }
            }

            if (!declared.Contains("Ralven.Broker.exe"))
            {
                throw new InvalidDataException("O executável administrativo não foi declarado no manifesto.");
            }

            var actualFiles = Directory.EnumerateFiles(brokerDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(brokerDirectory, path).Replace('\\', '/'))
                .Where(path => !path.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actualFiles.SetEquals(declared))
            {
                throw new InvalidDataException("A pasta do componente administrativo contém arquivos não declarados.");
            }

            return new IntegrityLease(leases);
        }
        catch
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }

            throw;
        }
    }

    internal static bool IsVersionedRuntimeLayout(string baseDirectory)
    {
        var versionDirectory = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        return Version.TryParse(versionDirectory.Name, out _)
            && versionDirectory.Parent?.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) == true
            && versionDirectory.Parent.Parent?.Name.Equals("Runtime", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ResolveContainedPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)
            || Path.IsPathRooted(relative)
            || relative.Contains('\\'))
        {
            throw new InvalidDataException("Manifesto contém caminho inválido.");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Manifesto contém caminho fora do componente administrativo.");
        }

        EnsurePlainPath(fullRoot, fullPath);
        return fullPath;
    }

    private static void EnsurePlainPath(string root, string path)
    {
        var current = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        while (current.Length >= fullRoot.Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("O componente administrativo não pode atravessar links ou junctions.");
            }

            if (current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new IOException("O caminho do componente administrativo é inválido.");
        }
    }

    private static FileStream OpenLockedRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 16 * 1024,
        FileOptions.SequentialScan);

    private sealed class IntegrityLease(List<FileStream> streams) : IDisposable
    {
        public void Dispose()
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        public static EmptyLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}


internal static class CharacterSpanExtensions
{
    public static bool ContainsOnlyAsciiHexDigits(this ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}

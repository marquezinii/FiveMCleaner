using System.IO.Compression;
using System.Security.Cryptography;

namespace FiveMCleaner.UpdateRuntime;

public sealed class RuntimePackageStager
{
    private readonly RuntimeActivationStore activation;
    private readonly string runtimeRoot;

    public RuntimePackageStager(string runtimeRoot)
    {
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
        activation = new RuntimeActivationStore(runtimeRoot);
    }

    public string Stage(string archivePath, string version, string expectedSha256, long expectedSize)
    {
        if (!Version.TryParse(version, out _)) throw new ArgumentException("Versão inválida.", nameof(version));
        if (new FileInfo(archivePath).Length != expectedSize) throw new InvalidDataException("Tamanho do pacote não confere.");
        using (var stream = File.OpenRead(archivePath))
        {
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 do pacote não confere.");
        }

        Directory.CreateDirectory(activation.VersionsRoot);
        var destination = Path.Combine(activation.VersionsRoot, version);
        if (Directory.Exists(destination))
        {
            try
            {
                VerifyFileManifest(destination);
                return destination;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Existing directory is corrupt or tampered; remove and re-extract from verified archive.
                try { Directory.Delete(destination, recursive: true); }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidDataException("Diretório de versão existente está corrompido e não pôde ser removido.", cleanupException);
                }
            }
        }
        var staging = Path.Combine(runtimeRoot, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > 10_000
                || archive.Entries.Sum(entry => entry.Length) > 2_147_483_648L)
                throw new InvalidDataException("Pacote excede os limites de extração.");
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var output = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!output.StartsWith(Path.TrimEndingDirectorySeparator(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Pacote contém caminho fora do runtime.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                entry.ExtractToFile(output, overwrite: false);
            }
            VerifyFileManifest(staging);
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void VerifyFileManifest(string root)
    {
        var manifest = Path.Combine(root, "SHA256SUMS.txt");
        if (!File.Exists(manifest)) throw new InvalidDataException("Pacote não contém manifesto de arquivos.");
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(manifest).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator != 64) throw new InvalidDataException("Manifesto de arquivos inválido.");
            var expected = line[..separator];
            var relative = line[(separator + 2)..].Replace('/', Path.DirectorySeparatorChar);
            if (!declared.Add(relative)) throw new InvalidDataException("Manifesto declara arquivo duplicado.");
            var file = Path.GetFullPath(Path.Combine(root, relative));
            if (!file.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(file)) throw new InvalidDataException("Arquivo declarado ausente.");
            using var stream = File.OpenRead(file);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Integridade de arquivo extraído falhou.");
        }
        var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(path => !path.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (actual.Any(path => !declared.Contains(path)))
            throw new InvalidDataException("Pacote contém arquivo não declarado.");
    }
}

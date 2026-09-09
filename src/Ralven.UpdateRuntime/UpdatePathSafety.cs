namespace Ralven.UpdateRuntime;

/// <summary>Rejects links and junctions before the update chain reads or writes a mutable path.</summary>
public static class UpdatePathSafety
{
    public static string EnsureNoReparsePoints(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        for (var current = normalized; ;)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"O caminho de atualização '{current}' não pode atravessar links ou junctions.");
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // A parte ainda inexistente do caminho é criada pelo chamador;
                // os ancestrais existentes continuam sendo verificados abaixo.
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent.Equals(current, StringComparison.OrdinalIgnoreCase))
                return normalized;
            current = parent;
        }
    }
}

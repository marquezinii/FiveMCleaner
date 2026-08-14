namespace FiveMCleaner.Tests.App;

internal static class TestHelpers
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FiveMCleaner.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FiveMCleaner repository root was not found.");
    }

    public static SortedSet<T> ToSortedSet<T>(
        this IEnumerable<T> source,
        IComparer<T>? comparer = null)
    {
        return new SortedSet<T>(source, comparer);
    }
}

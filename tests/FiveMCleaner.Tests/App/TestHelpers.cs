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

    /// <summary>
    /// Concatenates every <c>MainWindow*.xaml.cs</c> partial-class file so
    /// source-content assertions keep working regardless of which physical
    /// file a given member currently lives in (see PROJECT_STATE.md item 10
    /// — MainWindow.xaml.cs was split into several partial files by concern).
    /// </summary>
    public static string ReadMainWindowSource()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "src", "FiveMCleaner.App");
        var files = Directory.GetFiles(appDirectory, "MainWindow*.xaml.cs").OrderBy(path => path, StringComparer.Ordinal);
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    public static SortedSet<T> ToSortedSet<T>(
        this IEnumerable<T> source,
        IComparer<T>? comparer = null)
    {
        return new SortedSet<T>(source, comparer);
    }
}

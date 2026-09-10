namespace Ralven.App.Services;

// The opt-in build probe observes timings in memory. Normal builds perform no I/O.
internal static class StartupTrace
{
    internal static Action<string>? Observer { get; set; }

    internal static void Mark(string stage) => Observer?.Invoke(stage);
}

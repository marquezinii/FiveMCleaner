namespace Ralven.Contracts;

/// <summary>
/// Groups every <see cref="BugCode"/> into its category (the prefix before
/// the first underscore, e.g. "BRK" for <see cref="BugCode.BRK_ACTION_EXECUTION"/>).
/// Categories are few and stable, so — unlike individual codes — each one
/// can carry a translated resource key without a per-code translation step;
/// a newly appended <see cref="BugCode"/> is automatically covered as long
/// as it reuses an existing category prefix.
/// </summary>
public static class BugCodeCatalog
{
    /// <summary>Maps each known category prefix to its localization resource key.</summary>
    public static readonly IReadOnlyDictionary<string, string> CategoryResourceKeys =
        new Dictionary<string, string>
        {
            ["APP"] = "BugCode.Category.App",
            ["UPD"] = "BugCode.Category.Updater",
            ["BRK"] = "BugCode.Category.Broker",
            ["NET"] = "BugCode.Category.Network",
            ["FIVEM"] = "BugCode.Category.FiveM",
            ["GTAV"] = "BugCode.Category.GtaV",
            ["WIN"] = "BugCode.Category.Windows",
            ["CFG"] = "BugCode.Category.Config",
            ["SYS"] = "BugCode.Category.System",
            ["SEC"] = "BugCode.Category.Security",
        };

    /// <summary>Extracts the category prefix from a <see cref="BugCode"/> (e.g. "BRK").</summary>
    public static string GetCategory(BugCode code)
    {
        var name = code.ToString();
        var separatorIndex = name.IndexOf('_');
        return separatorIndex > 0 ? name[..separatorIndex] : name;
    }

    /// <summary>Resource key for the category's localized label, or null if the category is unknown.</summary>
    public static string? GetCategoryResourceKey(BugCode code) =>
        CategoryResourceKeys.TryGetValue(GetCategory(code), out var key) ? key : null;
}

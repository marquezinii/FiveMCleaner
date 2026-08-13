namespace FiveMCleaner.Contracts;

/// <summary>Product-wide constants every process agrees on.</summary>
public static class ProductIdentity
{
    /// <summary>Also names the local data directory under <c>%LOCALAPPDATA%</c>.</summary>
    public const string Name = "FiveMCleaner";

    public const string Subtitle = "optimizer for FiveM";

    public const string RepositoryUrl = "https://github.com/marquezinii/FiveMCleaner";

    public const string DiscordInviteUrl = "https://discord.gg/bazcuQB9n6";

    /// <summary>
    /// Shape of <see cref="OptimizationPlanDto"/> on the wire. The elevated
    /// broker rejects any plan that does not declare this exact version, so it
    /// must be bumped whenever the plan contract stops being readable by an
    /// older reader.
    /// </summary>
    public const int PlanSchemaVersion = 1;
}

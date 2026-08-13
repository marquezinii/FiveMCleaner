namespace FiveMCleaner.Contracts;

public static class ProductIdentity
{
    public const string Name = "FiveMCleaner";
    public const string Subtitle = "optimizer for FiveM";
    public const string RepositoryUrl = "https://github.com/marquezinii/FiveMCleaner";
    public const string DiscordInviteUrl = "https://discord.gg/bazcuQB9n6";

    /// <summary>Bump when the plan stops being readable by an older broker, which rejects any other value.</summary>
    public const int PlanSchemaVersion = 1;
}

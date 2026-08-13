using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiveMCleaner.Contracts;

public static class FiveMCleanerJson
{
    /// <summary>
    /// Shared by the plan sent to the broker, the broker events, the durable
    /// journal and the local settings. Read-only: mutating it would change all
    /// four boundaries at once, so a boundary that needs different behaviour
    /// copies it instead.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string SerializeRequest(OptimizationPlanRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(request, Options);
    }

    public static OptimizationPlanRequestDto DeserializeRequest(string json)
    {
        return DeserializeRequired<OptimizationPlanRequestDto>(json);
    }

    public static string SerializePlan(OptimizationPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, Options);
    }

    public static OptimizationPlanDto DeserializePlan(string json)
    {
        return DeserializeRequired<OptimizationPlanDto>(json);
    }

    private static T DeserializeRequired<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new JsonException($"The payload did not contain a {typeof(T).Name} value.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

        // Locked here, not on first use, so the guarantee does not depend on
        // which boundary happens to serialize first.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

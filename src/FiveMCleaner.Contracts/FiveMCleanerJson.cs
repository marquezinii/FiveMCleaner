using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiveMCleaner.Contracts;

/// <summary>
/// The single JSON contract shared by every FiveMCleaner boundary: the plan
/// handed to the elevated broker, the broker's progress events, the durable
/// transaction journal and the locally persisted settings.
/// </summary>
public static class FiveMCleanerJson
{
    /// <summary>
    /// Canonical, strict serializer settings: camelCase members and enums,
    /// no comments, no numeric enum values and no unmapped members.
    /// </summary>
    /// <remarks>
    /// Deliberately read-only. A consumer that added a converter or relaxed a
    /// setting here would silently change every boundary at once, including
    /// the journal that keeps past runs reversible; a boundary that genuinely
    /// needs different behaviour copies these options
    /// (<c>new JsonSerializerOptions(FiveMCleanerJson.Options)</c>) and adjusts
    /// the copy.
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string SerializeRequest(OptimizationPlanRequestDto request)
    {
        return Serialize(request);
    }

    public static OptimizationPlanRequestDto DeserializeRequest(string json)
    {
        return DeserializeRequired<OptimizationPlanRequestDto>(json);
    }

    public static string SerializePlan(OptimizationPlanDto plan)
    {
        return Serialize(plan);
    }

    public static OptimizationPlanDto DeserializePlan(string json)
    {
        return DeserializeRequired<OptimizationPlanDto>(json);
    }

    private static string Serialize<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        return JsonSerializer.Serialize(value, Options);
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

        // Locked here rather than left to the first serialization so the
        // guarantee does not depend on which boundary happens to run first.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

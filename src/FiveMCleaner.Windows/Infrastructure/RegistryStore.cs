using Microsoft.Win32;

namespace FiveMCleaner.Windows.Infrastructure;

public sealed record RegistryAddress
{
    public RegistryAddress(RegistryHive hive, string subKey, string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        Hive = hive;
        SubKey = subKey.Trim('\\');
        ValueName = valueName;
    }

    public RegistryHive Hive { get; }

    public string SubKey { get; }

    public string ValueName { get; }
}

public sealed record RegistryValueState
{
    public required bool Exists { get; init; }

    public RegistryValueKind? Kind { get; init; }

    public string? StringValue { get; init; }

    public long? NumericValue { get; init; }

    public IReadOnlyList<string>? MultiStringValue { get; init; }

    public string? BinaryBase64Value { get; init; }

    public static RegistryValueState Missing { get; } = new() { Exists = false };

    public static RegistryValueState FromDword(int value)
    {
        return new RegistryValueState
        {
            Exists = true,
            Kind = RegistryValueKind.DWord,
            NumericValue = value
        };
    }

    public static RegistryValueState FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new RegistryValueState
        {
            Exists = true,
            Kind = RegistryValueKind.String,
            StringValue = value
        };
    }
}

public interface IRegistryStore
{
    RegistryValueState Read(RegistryAddress address);

    void Write(RegistryAddress address, RegistryValueState state);

    void Delete(RegistryAddress address);
}

public sealed class WindowsRegistryStore : IRegistryStore
{
    private readonly RegistryView view;

    public WindowsRegistryStore(RegistryView view = RegistryView.Default)
    {
        this.view = view;
    }

    public RegistryValueState Read(RegistryAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var baseKey = RegistryKey.OpenBaseKey(address.Hive, view);
        using var key = baseKey.OpenSubKey(address.SubKey, writable: false);
        if (key is null || !key.GetValueNames().Contains(address.ValueName, StringComparer.Ordinal))
        {
            return RegistryValueState.Missing;
        }

        var kind = key.GetValueKind(address.ValueName);
        var value = key.GetValue(address.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return FromRegistryValue(kind, value);
    }

    public void Write(RegistryAddress address, RegistryValueState state)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Exists || state.Kind is null)
        {
            throw new ArgumentException("A missing registry value cannot be written.", nameof(state));
        }

        using var baseKey = RegistryKey.OpenBaseKey(address.Hive, view);
        using var key = baseKey.CreateSubKey(address.SubKey, writable: true)
            ?? throw new UnauthorizedAccessException($"Cannot create registry key '{address.SubKey}'.");
        key.SetValue(address.ValueName, ToRegistryValue(state), state.Kind.Value);
    }

    public void Delete(RegistryAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var baseKey = RegistryKey.OpenBaseKey(address.Hive, view);
        using var key = baseKey.OpenSubKey(address.SubKey, writable: true);
        key?.DeleteValue(address.ValueName, throwOnMissingValue: false);
    }

    private static RegistryValueState FromRegistryValue(RegistryValueKind kind, object? value)
    {
        return kind switch
        {
            RegistryValueKind.DWord => new RegistryValueState
            {
                Exists = true,
                Kind = kind,
                NumericValue = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
            },
            RegistryValueKind.QWord => new RegistryValueState
            {
                Exists = true,
                Kind = kind,
                NumericValue = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
            },
            RegistryValueKind.MultiString => new RegistryValueState
            {
                Exists = true,
                Kind = kind,
                MultiStringValue = (value as string[]) ?? []
            },
            RegistryValueKind.Binary => new RegistryValueState
            {
                Exists = true,
                Kind = kind,
                BinaryBase64Value = Convert.ToBase64String((value as byte[]) ?? [])
            },
            RegistryValueKind.String or RegistryValueKind.ExpandString => new RegistryValueState
            {
                Exists = true,
                Kind = kind,
                StringValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            },
            _ => throw new NotSupportedException($"Registry value kind '{kind}' is not supported.")
        };
    }

    private static object ToRegistryValue(RegistryValueState state)
    {
        return state.Kind switch
        {
            RegistryValueKind.DWord => checked((int)(state.NumericValue
                ?? throw new InvalidOperationException("DWORD snapshot has no numeric value."))),
            RegistryValueKind.QWord => state.NumericValue
                ?? throw new InvalidOperationException("QWORD snapshot has no numeric value."),
            RegistryValueKind.MultiString => state.MultiStringValue?.ToArray()
                ?? throw new InvalidOperationException("Multi-string snapshot has no value."),
            RegistryValueKind.Binary => Convert.FromBase64String(state.BinaryBase64Value
                ?? throw new InvalidOperationException("Binary snapshot has no value.")),
            RegistryValueKind.String or RegistryValueKind.ExpandString => state.StringValue
                ?? throw new InvalidOperationException("String snapshot has no value."),
            _ => throw new NotSupportedException($"Registry value kind '{state.Kind}' is not supported.")
        };
    }
}

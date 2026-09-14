namespace FluidScript.Runtime;

/// <summary>A capability exposed by the embedding application to FluidScript.</summary>
public delegate FluidValue NativeFunction(IReadOnlyList<FluidValue> arguments);

/// <summary>
/// Registry of explicitly granted C# functions.  Function identifiers are stable for
/// the lifetime of the registry and are embedded in compiled P-code call sites.
/// </summary>
public sealed class FluidScriptHost
{
    internal const int PrintBuiltinId = 0;
    internal const int JsonSerializeBuiltinId = -1;
    internal const int JsonDeserializeBuiltinId = -2;
    internal const int JsonDeserializeAsBuiltinId = -3;
    private readonly List<NativeFunction> functions = new();
    private readonly Dictionary<string, int> names = new(StringComparer.Ordinal);

    public int RegisterFunction(string name, NativeFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        if (IsBuiltinName(name))
            throw new ArgumentException($"The '{name}' builtin is reserved.", nameof(name));
        if (names.ContainsKey(name))
            throw new ArgumentException($"A host function named '{name}' is already registered.", nameof(name));

        var id = functions.Count + 1;
        names.Add(name, id);
        functions.Add(function);
        return id;
    }

    public bool Contains(string name) => names.ContainsKey(name);

    internal static bool IsBuiltinName(string name) =>
        name is "print" or "jsonSerialize" or "jsonDeserialize" or "jsonDeserializeAs";

    internal static bool IsJsonBuiltinName(string name) =>
        name is "jsonSerialize" or "jsonDeserialize" or "jsonDeserializeAs";

    internal bool TryGetFunctionId(string name, out int id) => names.TryGetValue(name, out id);

    internal bool TryGetFunction(int id, out NativeFunction? function)
    {
        var index = id - 1;
        if (index >= 0 && index < functions.Count)
        {
            function = functions[index];
            return true;
        }

        function = null;
        return false;
    }
}

/// <summary>State and capabilities supplied to one VM execution.</summary>
public sealed class FluidScriptExecutionContext
{
    public FluidScriptExecutionContext(
        FluidScriptHost? host = null,
        IDictionary<string, FluidValue>? globals = null,
        Action<string>? output = null)
    {
        Host = host;
        Globals = globals ?? new Dictionary<string, FluidValue>(StringComparer.Ordinal);
        Output = output;
    }

    public FluidScriptHost? Host { get; }
    public IDictionary<string, FluidValue> Globals { get; }
    public Action<string>? Output { get; }
}

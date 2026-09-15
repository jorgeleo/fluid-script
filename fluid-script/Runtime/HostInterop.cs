using System.Collections;
using System.Globalization;
using System.Reflection;

namespace FluidScript.Runtime;

/// <summary>A capability exposed by the embedding application to FluidScript.</summary>
public delegate FluidValue NativeFunction(IReadOnlyList<FluidValue> arguments);

/// <summary>
/// An explicitly registered CLR object held by a FluidScript value. The object is
/// opaque until its CLR type is registered on the host used to execute the script.
/// </summary>
public sealed class FluidHostObject
{
    internal FluidHostObject(HostTypeRegistration registration, object instance)
    {
        Registration = registration;
        Instance = instance;
    }

    internal HostTypeRegistration Registration { get; }
    public object Instance { get; }
    public string TypeName => Registration.Name;
    public Type ClrType => Registration.ClrType;

    public override string ToString() => $"<{TypeName}>";
}

/// <summary>
/// Registry of explicitly granted C# functions and CLR object types. Registered
/// types expose only public instance constructors, properties, fields, and methods.
/// No unregistered CLR type is reflectable by the VM.
/// </summary>
public sealed class FluidScriptHost
{
    internal const int PrintBuiltinId = 0;
    internal const int JsonSerializeBuiltinId = -1;
    internal const int JsonDeserializeBuiltinId = -2;
    internal const int JsonDeserializeAsBuiltinId = -3;
    private readonly List<NativeFunction> functions = new();
    private readonly Dictionary<string, int> names = new(StringComparer.Ordinal);
    private readonly Dictionary<int, NativeFunction> functionsById = new();
    private readonly Dictionary<string, string> functionReturnTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativePropertyRegistration> properties = new(StringComparer.Ordinal);
    private readonly List<HostTypeRegistration> types = new();
    private readonly Dictionary<string, HostTypeRegistration> typeNames = new(StringComparer.Ordinal);

    public int RegisterFunction(string name, NativeFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        if (IsBuiltinName(name))
            throw new ArgumentException($"The '{name}' builtin is reserved.", nameof(name));
        if (names.ContainsKey(name) || typeNames.ContainsKey(name))
            throw new ArgumentException($"A host capability named '{name}' is already registered.", nameof(name));

        var id = functions.Count + 1;
        names.Add(name, id);
        functions.Add(function);
        functionsById.Add(id, function);
        return id;
    }

    /// <summary>Registers a CLR type under the name used by FluidScript.</summary>
    public int RegisterType(string name, Type clrType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clrType);
        return RegisterTypeCore(name, clrType, allowBuiltinName: false);
    }

    public int RegisterType<T>(string name) => RegisterType(name, typeof(T));

    public int RegisterType<T>() => RegisterType(typeof(T));

    /// <summary>Registers a CLR type using its simple CLR name.</summary>
    public int RegisterType(Type clrType) => RegisterType(clrType.Name, clrType);

    public bool Contains(string name) => names.ContainsKey(name) || typeNames.ContainsKey(name);
    public bool ContainsType(string name) => typeNames.ContainsKey(name);

    /// <summary>Creates a host containing the standard runtime library registrations.</summary>
    public static FluidScriptHost CreateStandardLibraryHost()
    {
        var host = new FluidScriptHost();
        host.RegisterStandardLibraries();
        return host;
    }

    /// <summary>Registers the standard libraries on this host if they are not already present.</summary>
    public void RegisterStandardLibraries()
    {
        new StringLibrary().Register(this);
        new RegExpLibrary().Register(this);
        new NumberLibrary().Register(this);
        new MathLibrary().Register(this);
    }

    /// <summary>
    /// Wraps an instance whose runtime type is covered by one of this host's
    /// registrations. This is the explicit C# to FluidScript boundary.
    /// </summary>
    public FluidValue Wrap(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var registration = types
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(candidate => candidate.ClrType.IsInstanceOfType(instance));
        if (registration is null)
            throw new ArgumentException($"The CLR type '{instance.GetType()}' is not registered.", nameof(instance));
        return FluidValue.FromHostObject(new FluidHostObject(registration, instance));
    }

    internal bool TryGetType(string name, out HostTypeRegistration? registration) => typeNames.TryGetValue(name, out registration);

    internal bool TryGetType(int id, out HostTypeRegistration? registration)
    {
        var index = id - 1;
        if (index >= 0 && index < types.Count)
        {
            registration = types[index];
            return true;
        }
        registration = null;
        return false;
    }

    internal bool TryGetType(Type clrType, out HostTypeRegistration? registration)
    {
        registration = types.AsEnumerable().Reverse().FirstOrDefault(candidate => candidate.ClrType == clrType);
        return registration is not null;
    }

    internal bool TryGetFunctionId(string name, out int id) => names.TryGetValue(name, out id);

    internal bool TryGetFunction(int id, out NativeFunction? function)
    {
        return functionsById.TryGetValue(id, out function);
    }

    internal bool TryGetNativeFunctionId(string name, out int id) => names.TryGetValue(name, out id);

    internal bool TryGetNativeFunctionReturnType(string name, out string returnType) => functionReturnTypes.TryGetValue(name, out returnType!);

    internal bool TryGetNativeProperty(string name, out NativePropertyRegistration property) => properties.TryGetValue(name, out property!);

    internal void RegisterLibraryFunction(string name, int id, NativeFunction function, string returnType = "any")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        if (id >= 0 || id is -1 or -2 or -3)
            throw new ArgumentOutOfRangeException(nameof(id), "Library function IDs must use the reserved negative range.");
        if (names.TryGetValue(name, out var existingId))
        {
            if (existingId != id)
                throw new ArgumentException($"A native function named '{name}' is already registered.", nameof(name));
            return;
        }
        if (functionsById.TryGetValue(id, out var existingFunction))
        {
            if (!Equals(existingFunction, function))
                throw new ArgumentException($"Native function ID '{id}' is already registered.", nameof(id));
            names.Add(name, id);
            functionReturnTypes[name] = returnType;
            return;
        }
        names.Add(name, id);
        functionsById.Add(id, function);
        functionReturnTypes[name] = returnType;
    }

    internal int RegisterLibraryType(string name, Type clrType) => RegisterTypeCore(name, clrType, allowBuiltinName: true);

    private int RegisterTypeCore(string name, Type clrType, bool allowBuiltinName)
    {
        if (!allowBuiltinName && IsBuiltinName(name))
            throw new ArgumentException($"The '{name}' builtin is reserved.", nameof(name));
        if (typeNames.TryGetValue(name, out var existing))
        {
            if (allowBuiltinName && existing.ClrType == clrType)
                return existing.Id;
            throw new ArgumentException($"A host capability named '{name}' is already registered.", nameof(name));
        }
        if (names.ContainsKey(name))
            throw new ArgumentException($"A host capability named '{name}' is already registered.", nameof(name));

        var registration = new HostTypeRegistration(types.Count + 1, name, clrType);
        types.Add(registration);
        typeNames.Add(name, registration);
        return registration.Id;
    }

    internal void RegisterLibraryProperty(
        string name,
        int getterId,
        NativeFunction getter,
        int? setterId,
        NativeFunction? setter,
        string returnType,
        bool writable)
    {
        RegisterLibraryFunction(name, getterId, getter, returnType);
        if (setterId is not null && setter is not null)
            RegisterLibraryFunction(name + "=", setterId.Value, setter, "null");
        properties[name] = new NativePropertyRegistration(getterId, setterId, returnType, writable);
    }

    internal static bool IsBuiltinName(string name) =>
        name is "print" or "jsonSerialize" or "jsonDeserialize" or "jsonDeserializeAs" or
        "String" or "RegExp" or "Number" or "Math" ||
        name.StartsWith("String.", StringComparison.Ordinal) ||
        name.StartsWith("RegExp.", StringComparison.Ordinal) ||
        name.StartsWith("Number.", StringComparison.Ordinal) ||
        name.StartsWith("Math.", StringComparison.Ordinal);

    internal static bool IsJsonBuiltinName(string name) =>
        name is "jsonSerialize" or "jsonDeserialize" or "jsonDeserializeAs";
}

internal sealed record NativePropertyRegistration(int GetterId, int? SetterId, string ReturnType, bool Writable);

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

internal sealed class HostTypeRegistration
{
    public HostTypeRegistration(int id, string name, Type clrType)
    {
        Id = id;
        Name = name;
        ClrType = clrType;
        Constructors = clrType.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(constructor => constructor.MetadataToken)
            .ToArray();
        Properties = clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Fields = clrType.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Methods = clrType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => !method.IsSpecialName && method.DeclaringType != typeof(object))
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(method => method.MetadataToken).ToArray(), StringComparer.Ordinal);
    }

    public int Id { get; }
    public string Name { get; }
    public Type ClrType { get; }
    public ConstructorInfo[] Constructors { get; }
    public Dictionary<string, PropertyInfo> Properties { get; }
    public Dictionary<string, FieldInfo> Fields { get; }
    public Dictionary<string, MethodInfo[]> Methods { get; }
}

internal sealed class HostBindingException(string message, Exception? inner = null) : Exception(message, inner);

internal static class HostBinding
{
    public static object Create(HostTypeRegistration registration, IReadOnlyList<FluidValue> arguments, FluidScriptHost host)
    {
        var candidate = SelectCandidate(registration.Constructors, arguments, host, $"constructor for '{registration.Name}'");
        try
        {
            return ((ConstructorInfo)candidate.Method).Invoke(candidate.Arguments)!;
        }
        catch (TargetInvocationException exception)
        {
            throw new HostBindingException($"The constructor for '{registration.Name}' failed: {exception.InnerException?.Message ?? exception.Message}", exception.InnerException ?? exception);
        }
    }

    public static FluidValue GetProperty(FluidHostObject target, string name, FluidScriptHost host)
    {
        try
        {
            if (target.Registration.Properties.TryGetValue(name, out var property) && property.GetMethod?.IsPublic == true)
                return ToFluid(property.GetValue(target.Instance), host);
            if (target.Registration.Fields.TryGetValue(name, out var field))
                return ToFluid(field.GetValue(target.Instance), host);
        }
        catch (TargetInvocationException exception)
        {
            throw new HostBindingException($"Property '{name}' failed: {exception.InnerException?.Message ?? exception.Message}", exception.InnerException ?? exception);
        }
        throw new HostBindingException($"Type '{target.TypeName}' has no readable property '{name}'.");
    }

    public static void SetProperty(FluidHostObject target, string name, FluidValue value, FluidScriptHost host)
    {
        if (target.Registration.Properties.TryGetValue(name, out var property) && property.SetMethod?.IsPublic == true)
        {
            try
            {
                property.SetValue(target.Instance, ConvertToClr(value, property.PropertyType, host));
            }
            catch (TargetInvocationException exception)
            {
                throw new HostBindingException($"Property '{name}' failed: {exception.InnerException?.Message ?? exception.Message}", exception.InnerException ?? exception);
            }
            return;
        }
        if (target.Registration.Fields.TryGetValue(name, out var field) && !field.IsInitOnly)
        {
            field.SetValue(target.Instance, ConvertToClr(value, field.FieldType, host));
            return;
        }
        throw new HostBindingException($"Type '{target.TypeName}' has no writable property '{name}'.");
    }

    public static FluidValue CallMethod(FluidHostObject target, string name, IReadOnlyList<FluidValue> arguments, FluidScriptHost host)
    {
        if (!target.Registration.Methods.TryGetValue(name, out var methods))
            throw new HostBindingException($"Type '{target.TypeName}' has no method '{name}'.");
        var candidate = SelectCandidate(methods, arguments, host, $"method '{name}' on '{target.TypeName}'");
        try
        {
            return ToFluid(candidate.Method.Invoke(target.Instance, candidate.Arguments), host);
        }
        catch (TargetInvocationException exception)
        {
            throw new HostBindingException($"Method '{name}' on '{target.TypeName}' failed: {exception.InnerException?.Message ?? exception.Message}", exception.InnerException ?? exception);
        }
    }

    private static Candidate SelectCandidate(IEnumerable<MethodBase> methods, IReadOnlyList<FluidValue> arguments, FluidScriptHost host, string description)
    {
        Candidate? best = null;
        var ambiguous = false;
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (arguments.Count > parameters.Length || arguments.Count < parameters.Count(parameter => !parameter.IsOptional))
                continue;
            var converted = new object?[parameters.Length];
            var score = 0;
            var valid = true;
            for (var index = 0; index < parameters.Length; index++)
            {
                if (index >= arguments.Count)
                {
                    converted[index] = parameters[index].DefaultValue;
                    continue;
                }
                try
                {
                    converted[index] = ConvertToClr(arguments[index], parameters[index].ParameterType, host, out var conversionScore);
                    score += conversionScore;
                }
                catch (HostBindingException)
                {
                    valid = false;
                    break;
                }
            }
            if (!valid)
                continue;
            var candidate = new Candidate(method, converted, score);
            if (best is null || candidate.Score < best.Value.Score)
            {
                best = candidate;
                ambiguous = false;
            }
            else if (candidate.Score == best.Value.Score)
            {
                ambiguous = true;
            }
        }

        if (best is null)
            throw new HostBindingException($"No {description} accepts the supplied arguments.");
        if (ambiguous)
            throw new HostBindingException($"The {description} call is ambiguous.");
        return best.Value;
    }

    private static FluidValue ToFluid(object? value, FluidScriptHost host)
    {
        if (value is null)
            return FluidValue.Null;
        if (value is FluidValue fluidValue)
            return fluidValue;
        if (value is bool boolean) return FluidValue.From(boolean);
        if (value is string text) return FluidValue.From(text);
        if (value is char character) return FluidValue.From(character.ToString());
        if (value is byte byteValue) return FluidValue.From(byteValue);
        if (value is sbyte or short or ushort or int or uint or long or ulong)
            return FluidValue.From(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        if (value is decimal decimalValue) return FluidValue.From(decimalValue);
        if (value is float or double)
            return FluidValue.From(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
        if (value is DateTimeOffset dateTimeOffset) return FluidValue.From(dateTimeOffset);
        if (value is DateTime dateTime) return FluidValue.From(new DateTimeOffset(dateTime));
        if (value is Guid guid) return FluidValue.From(guid);
        if (value is IDictionary dictionary)
        {
            var entries = new Dictionary<string, FluidValue>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                    throw new HostBindingException("A host dictionary must have string keys.");
                entries[key] = ToFluid(entry.Value, host);
            }
            return FluidValue.FromDictionary(new FluidDictionary(entries));
        }
        if (value is IEnumerable sequence)
        {
            var values = new List<FluidValue>();
            foreach (var item in sequence)
                values.Add(ToFluid(item, host));
            return FluidValue.FromArray(values);
        }
        try
        {
            return host.Wrap(value);
        }
        catch (ArgumentException exception)
        {
            throw new HostBindingException(exception.Message, exception);
        }
    }

    private static object? ConvertToClr(FluidValue value, Type targetType, FluidScriptHost host, out int score)
    {
        score = 0;
        var nullableTarget = Nullable.GetUnderlyingType(targetType);
        if (value.Kind == FluidValueKind.Null)
        {
            if (!targetType.IsValueType || nullableTarget is not null)
                return null;
            throw new HostBindingException($"Null cannot be converted to '{targetType}'.");
        }
        if (targetType == typeof(FluidValue)) return value;
        if (targetType == typeof(FluidHostObject))
        {
            if (value.Kind == FluidValueKind.HostObject) return value.AsHostObject();
            throw new HostBindingException("The value is not a host object.");
        }
        if (value.Kind == FluidValueKind.HostObject && targetType.IsInstanceOfType(value.AsHostObject().Instance))
            return value.AsHostObject().Instance;
        if (targetType == typeof(object))
        {
            score = 5;
            return value.Kind == FluidValueKind.HostObject ? value.AsHostObject().Instance : value.Raw;
        }

        var effectiveTarget = nullableTarget ?? targetType;
        if (effectiveTarget.IsEnum)
        {
            score = 2;
            try { return value.Kind == FluidValueKind.String ? Enum.Parse(effectiveTarget, value.AsString(), true) : Enum.ToObject(effectiveTarget, value.AsInt()); }
            catch (Exception exception) { throw new HostBindingException($"The value cannot be converted to '{targetType}'.", exception); }
        }
        if (effectiveTarget == typeof(string) && value.Kind == FluidValueKind.String) return value.AsString();
        if (effectiveTarget == typeof(bool) && value.Kind == FluidValueKind.Bool) return value.AsBool();
        if (effectiveTarget == typeof(Guid) && value.Kind == FluidValueKind.Guid) return (Guid)value.Raw!;
        if (effectiveTarget == typeof(DateTimeOffset) && value.Kind == FluidValueKind.DateTime) return (DateTimeOffset)value.Raw!;
        if (effectiveTarget == typeof(byte) && value.Kind == FluidValueKind.Byte) return (byte)value.Raw!;
        if (IsNumericType(effectiveTarget) && value.Kind is FluidValueKind.Int or FluidValueKind.Decimal or FluidValueKind.Byte)
        {
            score = value.Kind == FluidValueKind.Int && effectiveTarget == typeof(long) ? 0 : 1;
            try { return Convert.ChangeType(value.Raw, effectiveTarget, CultureInfo.InvariantCulture); }
            catch (Exception exception) { throw new HostBindingException($"The value cannot be converted to '{targetType}'.", exception); }
        }
        if (effectiveTarget.IsArray && value.Kind == FluidValueKind.Array)
        {
            var elementType = effectiveTarget.GetElementType()!;
            var result = Array.CreateInstance(elementType, value.AsArray().Count);
            for (var index = 0; index < value.AsArray().Count; index++)
                result.SetValue(ConvertToClr(value.AsArray()[index], elementType, host, out _), index);
            score = 3;
            return result;
        }
        if (value.Kind == FluidValueKind.Array && effectiveTarget.IsGenericType &&
            (effectiveTarget.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
             effectiveTarget.GetGenericTypeDefinition() == typeof(ICollection<>) ||
             effectiveTarget.GetGenericTypeDefinition() == typeof(IList<>) ||
             effectiveTarget.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) ||
             effectiveTarget.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
             effectiveTarget.GetGenericTypeDefinition() == typeof(List<>)))
        {
            var elementType = effectiveTarget.GetGenericArguments()[0];
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in value.AsArray())
                list.Add(ConvertToClr(item, elementType, host, out _));
            if (!effectiveTarget.IsAssignableFrom(listType))
                throw new HostBindingException($"The array cannot be converted to '{targetType}'.");
            score = 3;
            return list;
        }
        if (value.Kind == FluidValueKind.Dictionary && effectiveTarget.IsGenericType &&
            effectiveTarget.GetGenericTypeDefinition() == typeof(Dictionary<,>) && effectiveTarget.GetGenericArguments()[0] == typeof(string))
        {
            var valueType = effectiveTarget.GetGenericArguments()[1];
            var result = (IDictionary)Activator.CreateInstance(effectiveTarget)!;
            foreach (var entry in value.AsDictionary().Entries)
                result.Add(entry.Key, ConvertToClr(entry.Value, valueType, host, out _));
            score = 3;
            return result;
        }
        if (value.Raw is not null && effectiveTarget.IsInstanceOfType(value.Raw)) return value.Raw;
        throw new HostBindingException($"The value of kind '{value.Kind}' cannot be converted to '{targetType}'.");
    }

    private static object? ConvertToClr(FluidValue value, Type targetType, FluidScriptHost host) => ConvertToClr(value, targetType, host, out _);

    private static bool IsNumericType(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
        type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private readonly record struct Candidate(MethodBase Method, object?[] Arguments, int Score);
}

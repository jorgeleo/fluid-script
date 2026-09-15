using System.Globalization;
using System.Text;

namespace FluidScript.Runtime;

internal sealed class StandardLibraryException(string message, Exception? inner = null) : Exception(message, inner);

internal static class StandardLibrarySupport
{
    public static FluidValue Bool(bool value) => FluidValue.From(value);
    public static FluidValue Int(long value) => FluidValue.From(value);

    public static FluidValue Decimal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new StandardLibraryException("The standard-library result is not a finite number.");
        try
        {
            var decimalValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return Decimal(decimalValue);
        }
        catch (OverflowException exception)
        {
            throw new StandardLibraryException("The standard-library result is outside the FluidScript numeric range.", exception);
        }
    }

    public static FluidValue Decimal(decimal value) =>
        value == decimal.Truncate(value) && value >= long.MinValue && value <= long.MaxValue
            ? FluidValue.From(decimal.ToInt64(value))
            : FluidValue.From(value);

    public static void RequireRange(IReadOnlyList<FluidValue> arguments, int minimum, int maximum, string name)
    {
        if (arguments.Count < minimum || arguments.Count > maximum)
            throw new StandardLibraryException($"{name} expects between {minimum} and {maximum} argument(s).");
    }

    public static long RequireInt(FluidValue value, string name) => value.Kind switch
    {
        FluidValueKind.Int => value.AsInt(),
        FluidValueKind.Byte => (byte)value.Raw!,
        _ => throw new StandardLibraryException($"{name} requires an integer argument.")
    };

    public static long OptionalInt(IReadOnlyList<FluidValue> arguments, int index, long fallback, string name) =>
        index < arguments.Count ? RequireInt(arguments[index], name) : fallback;

    public static FluidValue Number(FluidValue value, string name)
    {
        if (value.Kind is FluidValueKind.Int or FluidValueKind.Decimal)
            return value;
        if (value.Kind == FluidValueKind.Byte)
            return FluidValue.From((long)(byte)value.Raw!);
        if (value.Kind == FluidValueKind.Null)
            return FluidValue.From(0L);
        if (value.Kind == FluidValueKind.Bool)
            return FluidValue.From(value.AsBool() ? 1L : 0L);
        if (value.Kind == FluidValueKind.String && decimal.TryParse(value.AsString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return Decimal(parsed);
        throw new StandardLibraryException($"{name} requires a finite numeric value.");
    }

    public static FluidValue Number(IReadOnlyList<FluidValue> arguments, int index, string name)
    {
        if (index >= arguments.Count)
            throw new StandardLibraryException($"{name} is missing an argument.");
        return Number(arguments[index], name);
    }

    public static string RequireString(FluidValue value, string name) => value.Kind == FluidValueKind.String
        ? value.AsString()
        : throw new StandardLibraryException($"{name} requires a string argument.");

    public static string StringAt(IReadOnlyList<FluidValue> arguments, int index, string name)
    {
        if (index >= arguments.Count)
            throw new StandardLibraryException($"{name} is missing an argument.");
        return RequireString(arguments[index], name);
    }

    public static string ToJsString(FluidValue value)
    {
        if (value.Kind == FluidValueKind.HostObject && value.AsHostObject().Instance is FluidRegExp regex)
            return regex.ToString();
        return value.Kind switch
        {
            FluidValueKind.Null => "null",
            FluidValueKind.Bool => value.AsBool() ? "true" : "false",
            FluidValueKind.Int => value.AsInt().ToString(CultureInfo.InvariantCulture),
            FluidValueKind.Decimal => ((decimal)value.Raw!).ToString(CultureInfo.InvariantCulture),
            FluidValueKind.Byte => ((byte)value.Raw!).ToString(CultureInfo.InvariantCulture),
            FluidValueKind.String => value.AsString(),
            FluidValueKind.Array => string.Join(',', value.AsArray().Select(ToJsString)),
            FluidValueKind.Dictionary or FluidValueKind.Object => "[object Object]",
            _ => value.ToString()
        };
    }

    public static long NormalizeIndex(long index, int length) => index < 0
        ? Math.Max(length + index, 0)
        : Math.Min(index, length);

    public static string RepeatToLength(string value, int length)
    {
        if (length <= 0 || value.Length == 0)
            return string.Empty;
        var builder = new StringBuilder(length);
        while (builder.Length < length)
            builder.Append(value);
        return builder.ToString()[..length];
    }

    public static bool IsFiniteNumber(FluidValue value) => value.Kind is FluidValueKind.Int or FluidValueKind.Decimal or FluidValueKind.Byte;

    public static bool IsInteger(FluidValue value) => value.Kind switch
    {
        FluidValueKind.Int or FluidValueKind.Byte => true,
        FluidValueKind.Decimal => (decimal)value.Raw! == decimal.Truncate((decimal)value.Raw!),
        _ => false
    };

    public static bool IsSafeInteger(FluidValue value) => IsInteger(value) && Number(value, "Number.isSafeInteger").AsDecimal() is >= -9007199254740991m and <= 9007199254740991m;

    public static FluidValue UnaryMath(IReadOnlyList<FluidValue> arguments, Func<double, double> operation, string name) =>
        Decimal(operation((double)Number(arguments, 0, name).AsDecimal()));

    public static FluidValue BinaryMath(IReadOnlyList<FluidValue> arguments, Func<double, double, double> operation, string name)
    {
        RequireRange(arguments, 2, 2, name);
        return Decimal(operation((double)Number(arguments, 0, name).AsDecimal(), (double)Number(arguments, 1, name).AsDecimal()));
    }

    public static FluidValue AggregateMath(IReadOnlyList<FluidValue> arguments, Func<double, double, double> operation, string name, double initial)
    {
        if (arguments.Count == 0)
            throw new StandardLibraryException($"{name} requires at least one argument in FluidScript.");
        var result = initial;
        foreach (var argument in arguments)
            result = operation(result, (double)Number(argument, name).AsDecimal());
        return Decimal(result);
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FluidScript.Runtime;

/// <summary>Registers JavaScript-inspired number functions.</summary>
public sealed class NumberLibrary : IRegister
{
    private const int Constructor = -70;
    private const int IsFinite = -71;
    private const int IsInteger = -72;
    private const int IsNaN = -73;
    private const int IsSafeInteger = -74;
    private const int ParseFloat = -75;
    private const int ParseInt = -76;
    private const int ToFixed = -77;
    private const int ToStringId = -78;
    private const int ValueOf = -79;
    private const int Epsilon = -80;
    private const int MaxSafeInteger = -81;
    private const int MinSafeInteger = -82;

    public void Register(FluidScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.RegisterLibraryFunction("Number", Constructor, ConstructorValue, "decimal");
        host.RegisterLibraryFunction("Number.isFinite", IsFinite, arguments => FluidValue.From(arguments.Count == 1 && StandardLibrarySupport.IsFiniteNumber(arguments[0])), "bool");
        host.RegisterLibraryFunction("Number.isInteger", IsInteger, arguments => FluidValue.From(arguments.Count == 1 && StandardLibrarySupport.IsInteger(arguments[0])), "bool");
        host.RegisterLibraryFunction("Number.isNaN", IsNaN, _ => FluidValue.From(false), "bool");
        host.RegisterLibraryFunction("Number.isSafeInteger", IsSafeInteger, arguments => FluidValue.From(arguments.Count == 1 && StandardLibrarySupport.IsSafeInteger(arguments[0])), "bool");
        host.RegisterLibraryFunction("Number.parseFloat", ParseFloat, ParseFloatValue, "decimal");
        host.RegisterLibraryFunction("Number.parseInt", ParseInt, ParseIntValue, "int");
        host.RegisterLibraryProperty("Number.EPSILON", Epsilon, _ => StandardLibrarySupport.Decimal(0.0000000000000002220446049250313m), null, null, "decimal", false);
        host.RegisterLibraryProperty("Number.MAX_SAFE_INTEGER", MaxSafeInteger, _ => FluidValue.From(9007199254740991L), null, null, "int", false);
        host.RegisterLibraryProperty("Number.MIN_SAFE_INTEGER", MinSafeInteger, _ => FluidValue.From(-9007199254740991L), null, null, "int", false);
        Register(host, "toFixed", ToFixed, ToFixedValue, "string");
        Register(host, "toString", ToStringId, ToStringValue, "string");
        Register(host, "valueOf", ValueOf, arguments => StandardLibrarySupport.Number(arguments, 0, "Number.valueOf"), "decimal");
    }

    private static void Register(FluidScriptHost host, string name, int id, NativeFunction function, string returnType)
    {
        host.RegisterLibraryFunction("int." + name, id, function, returnType);
        host.RegisterLibraryFunction("decimal." + name, id, function, returnType);
    }

    private static FluidValue ConstructorValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 0, 1, "Number");
        return arguments.Count == 0 ? FluidValue.From(0L) : StandardLibrarySupport.Number(arguments[0], "Number");
    }

    private static FluidValue ParseFloatValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 1, "Number.parseFloat");
        var text = StandardLibrarySupport.ToJsString(arguments[0]).TrimStart();
        var match = Regex.Match(text, "^[+-]?(?:(?:\\d+\\.\\d*|\\.\\d+|\\d+)(?:[eE][+-]?\\d+)?)", RegexOptions.CultureInvariant);
        if (!match.Success || !decimal.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new StandardLibraryException("Number.parseFloat could not parse a finite number.");
        return StandardLibrarySupport.Decimal(result);
    }

    private static FluidValue ParseIntValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 2, "Number.parseInt");
        var text = StandardLibrarySupport.ToJsString(arguments[0]).TrimStart();
        var radix = arguments.Count == 2 ? StandardLibrarySupport.RequireInt(arguments[1], "Number.parseInt") : 0;
        var sign = 1;
        if (text.StartsWith('+')) text = text[1..];
        else if (text.StartsWith('-')) { sign = -1; text = text[1..]; }
        if (radix == 0)
            radix = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10;
        if (radix is < 2 or > 36)
            throw new StandardLibraryException("Number.parseInt radix must be between 2 and 36.");
        if (radix == 16 && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        long result = 0;
        var consumed = 0;
        foreach (var character in text)
        {
            var digit = character is >= '0' and <= '9' ? character - '0' : character is >= 'a' and <= 'z' ? character - 'a' + 10 : character is >= 'A' and <= 'Z' ? character - 'A' + 10 : -1;
            if (digit < 0 || digit >= radix)
                break;
            result = checked(result * radix + digit);
            consumed++;
        }
        if (consumed == 0)
            throw new StandardLibraryException("Number.parseInt could not parse an integer.");
        return FluidValue.From(checked(result * sign));
    }

    private static FluidValue ToFixedValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 2, "Number.toFixed");
        var digits = arguments.Count == 2 ? StandardLibrarySupport.RequireInt(arguments[1], "Number.toFixed") : 0;
        if (digits is < 0 or > 100)
            throw new StandardLibraryException("Number.toFixed digits must be between 0 and 100.");
        return FluidValue.From(StandardLibrarySupport.Number(arguments, 0, "Number.toFixed").AsDecimal().ToString($"F{digits}", CultureInfo.InvariantCulture));
    }

    private static FluidValue ToStringValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 2, "Number.toString");
        var radix = arguments.Count == 2 ? StandardLibrarySupport.RequireInt(arguments[1], "Number.toString") : 10;
        if (radix is < 2 or > 36)
            throw new StandardLibraryException("Number.toString radix must be between 2 and 36.");
        var value = StandardLibrarySupport.Number(arguments[0], "Number.toString");
        if (radix == 10)
            return FluidValue.From(StandardLibrarySupport.ToJsString(value));
        if (!StandardLibrarySupport.IsInteger(value))
            throw new StandardLibraryException("Number.toString with a non-decimal radix requires an integer.");
        var number = value.AsDecimal() is >= long.MinValue and <= long.MaxValue ? decimal.ToInt64(value.AsDecimal()) : throw new StandardLibraryException("Number.toString value is outside the integer range.");
        var negative = number < 0;
        var remaining = negative ? (ulong)(-(number + 1)) + 1UL : (ulong)number;
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var builder = new StringBuilder();
        do
        {
            builder.Insert(0, digits[(int)(remaining % (ulong)radix)]);
            remaining /= (ulong)radix;
        } while (remaining > 0);
        if (negative) builder.Insert(0, '-');
        return FluidValue.From(builder.ToString());
    }
}

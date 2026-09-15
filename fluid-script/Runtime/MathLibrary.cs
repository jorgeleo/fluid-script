namespace FluidScript.Runtime;

/// <summary>Registers JavaScript-inspired mathematical functions.</summary>
public sealed class MathLibrary : IRegister
{
    private const int E = -90;
    private const int Ln2 = -91;
    private const int Ln10 = -92;
    private const int Log2E = -93;
    private const int Log10E = -94;
    private const int Pi = -95;
    private const int Sqrt1_2 = -96;
    private const int Sqrt2 = -97;
    private const int Abs = -100;
    private const int Ceil = -101;
    private const int Floor = -102;
    private const int Max = -103;
    private const int Min = -104;
    private const int Pow = -105;
    private const int Random = -106;
    private const int Round = -107;
    private const int Sign = -108;
    private const int Sqrt = -109;
    private const int Trunc = -110;
    private const int Cbrt = -111;
    private const int Exp = -112;
    private const int Log = -113;
    private const int Log10 = -114;
    private const int Log2 = -115;
    private const int Sin = -116;
    private const int Cos = -117;
    private const int Tan = -118;
    private const int Asin = -119;
    private const int Acos = -120;
    private const int Atan = -121;
    private const int Atan2 = -122;
    private const int Hypot = -123;
    private const int Imul = -124;
    private const int Clz32 = -125;
    private const int Fround = -126;

    public void Register(FluidScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.RegisterLibraryProperty("Math.E", E, _ => StandardLibrarySupport.Decimal(Math.E), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.LN2", Ln2, _ => StandardLibrarySupport.Decimal(Math.Log(2)), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.LN10", Ln10, _ => StandardLibrarySupport.Decimal(Math.Log(10)), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.LOG2E", Log2E, _ => StandardLibrarySupport.Decimal(Math.Log2(Math.E)), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.LOG10E", Log10E, _ => StandardLibrarySupport.Decimal(Math.Log10(Math.E)), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.PI", Pi, _ => StandardLibrarySupport.Decimal(Math.PI), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.SQRT1_2", Sqrt1_2, _ => StandardLibrarySupport.Decimal(Math.Sqrt(0.5)), null, null, "decimal", false);
        host.RegisterLibraryProperty("Math.SQRT2", Sqrt2, _ => StandardLibrarySupport.Decimal(Math.Sqrt(2)), null, null, "decimal", false);
        host.RegisterLibraryFunction("Math.abs", Abs, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Abs, "Math.abs"));
        host.RegisterLibraryFunction("Math.ceil", Ceil, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Ceiling, "Math.ceil"));
        host.RegisterLibraryFunction("Math.floor", Floor, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Floor, "Math.floor"));
        host.RegisterLibraryFunction("Math.max", Max, arguments => StandardLibrarySupport.AggregateMath(arguments, Math.Max, "Math.max", double.MinValue));
        host.RegisterLibraryFunction("Math.min", Min, arguments => StandardLibrarySupport.AggregateMath(arguments, Math.Min, "Math.min", double.MaxValue));
        host.RegisterLibraryFunction("Math.pow", Pow, arguments => StandardLibrarySupport.BinaryMath(arguments, Math.Pow, "Math.pow"));
        host.RegisterLibraryFunction("Math.random", Random, _ => StandardLibrarySupport.Decimal(System.Random.Shared.NextDouble()));
        host.RegisterLibraryFunction("Math.round", Round, arguments => StandardLibrarySupport.UnaryMath(arguments, value => Math.Floor(value + 0.5), "Math.round"));
        host.RegisterLibraryFunction("Math.sign", Sign, arguments => StandardLibrarySupport.UnaryMath(arguments, value => Math.Sign(value), "Math.sign"));
        host.RegisterLibraryFunction("Math.sqrt", Sqrt, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Sqrt, "Math.sqrt"));
        host.RegisterLibraryFunction("Math.trunc", Trunc, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Truncate, "Math.trunc"));
        host.RegisterLibraryFunction("Math.cbrt", Cbrt, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Cbrt, "Math.cbrt"));
        host.RegisterLibraryFunction("Math.exp", Exp, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Exp, "Math.exp"));
        host.RegisterLibraryFunction("Math.log", Log, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Log, "Math.log"));
        host.RegisterLibraryFunction("Math.log10", Log10, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Log10, "Math.log10"));
        host.RegisterLibraryFunction("Math.log2", Log2, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Log2, "Math.log2"));
        host.RegisterLibraryFunction("Math.sin", Sin, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Sin, "Math.sin"));
        host.RegisterLibraryFunction("Math.cos", Cos, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Cos, "Math.cos"));
        host.RegisterLibraryFunction("Math.tan", Tan, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Tan, "Math.tan"));
        host.RegisterLibraryFunction("Math.asin", Asin, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Asin, "Math.asin"));
        host.RegisterLibraryFunction("Math.acos", Acos, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Acos, "Math.acos"));
        host.RegisterLibraryFunction("Math.atan", Atan, arguments => StandardLibrarySupport.UnaryMath(arguments, Math.Atan, "Math.atan"));
        host.RegisterLibraryFunction("Math.atan2", Atan2, arguments => StandardLibrarySupport.BinaryMath(arguments, Math.Atan2, "Math.atan2"));
        host.RegisterLibraryFunction("Math.hypot", Hypot, HypotValue);
        host.RegisterLibraryFunction("Math.imul", Imul, ImulValue, "int");
        host.RegisterLibraryFunction("Math.clz32", Clz32, Clz32Value, "int");
        host.RegisterLibraryFunction("Math.fround", Fround, arguments => StandardLibrarySupport.Decimal((double)(float)StandardLibrarySupport.Number(arguments, 0, "Math.fround").AsDecimal()));
    }

    private static FluidValue HypotValue(IReadOnlyList<FluidValue> arguments)
    {
        if (arguments.Count == 0)
            throw new StandardLibraryException("Math.hypot requires at least one argument in FluidScript.");
        var sum = 0d;
        foreach (var argument in arguments)
        {
            var number = (double)StandardLibrarySupport.Number(argument, "Math.hypot").AsDecimal();
            sum += number * number;
        }
        return StandardLibrarySupport.Decimal(Math.Sqrt(sum));
    }

    private static FluidValue ImulValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 2, 2, "Math.imul");
        var left = unchecked((int)StandardLibrarySupport.RequireInt(arguments[0], "Math.imul"));
        var right = unchecked((int)StandardLibrarySupport.RequireInt(arguments[1], "Math.imul"));
        return StandardLibrarySupport.Int(unchecked((int)((long)left * right)));
    }

    private static FluidValue Clz32Value(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 1, "Math.clz32");
        var value = unchecked((uint)StandardLibrarySupport.RequireInt(arguments[0], "Math.clz32"));
        return StandardLibrarySupport.Int(System.Numerics.BitOperations.LeadingZeroCount(value));
    }
}

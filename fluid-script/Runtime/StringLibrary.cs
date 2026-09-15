using System.Globalization;
using System.Text;

namespace FluidScript.Runtime;

/// <summary>Registers JavaScript-inspired string functions.</summary>
public sealed class StringLibrary : IRegister
{
    private const int Constructor = -10;
    private const int FromCharCode = -11;
    private const int FromCodePoint = -12;
    private const int Length = -13;
    private const int At = -14;
    private const int CharAt = -15;
    private const int CharCodeAt = -16;
    private const int CodePointAt = -17;
    private const int Concat = -18;
    private const int EndsWith = -19;
    private const int Includes = -20;
    private const int IndexOf = -21;
    private const int LastIndexOf = -22;
    private const int LocaleCompare = -23;
    private const int PadEnd = -24;
    private const int PadStart = -25;
    private const int Repeat = -26;
    private const int Replace = -27;
    private const int ReplaceAll = -28;
    private const int Search = -29;
    private const int Slice = -30;
    private const int Split = -31;
    private const int StartsWith = -32;
    private const int Substr = -33;
    private const int Substring = -34;
    private const int ToLowerCase = -35;
    private const int ToUpperCase = -36;
    private const int Trim = -37;
    private const int TrimStart = -38;
    private const int TrimEnd = -39;
    private const int Match = -40;
    private const int MatchAll = -41;
    private const int ValueOf = -42;
    private const int ToStringId = -43;
    private const int Raw = -44;
    private const int Normalize = -45;
    private const int IsWellFormed = -46;
    private const int ToWellFormed = -47;
    private const int TrimLeft = -48;
    private const int TrimRight = -49;

    public void Register(FluidScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.RegisterLibraryFunction("String", Constructor, ConstructorValue, "string");
        host.RegisterLibraryFunction("String.fromCharCode", FromCharCode, FromCharCodeValue, "string");
        host.RegisterLibraryFunction("String.fromCodePoint", FromCodePoint, FromCodePointValue, "string");
        host.RegisterLibraryFunction("String.raw", Raw, RawValue, "string");
        host.RegisterLibraryProperty("string.length", Length, arguments => FluidValue.From((long)StandardLibrarySupport.StringAt(arguments, 0, "String.length").Length), null, null, "int", false);

        host.RegisterLibraryFunction("string.at", At, AtValue, "string");
        host.RegisterLibraryFunction("string.charAt", CharAt, CharAtValue, "string");
        host.RegisterLibraryFunction("string.charCodeAt", CharCodeAt, CharCodeAtValue, "int");
        host.RegisterLibraryFunction("string.codePointAt", CodePointAt, CodePointAtValue, "int");
        host.RegisterLibraryFunction("string.concat", Concat, ConcatValue, "string");
        host.RegisterLibraryFunction("string.endsWith", EndsWith, arguments => Bool(StringAt(arguments, 0, "String.endsWith").EndsWith(StringAt(arguments, 1, "String.endsWith"), StringComparison.Ordinal)), "bool");
        host.RegisterLibraryFunction("string.includes", Includes, arguments => Bool(StringAt(arguments, 0, "String.includes").Contains(StringAt(arguments, 1, "String.includes"), StringComparison.Ordinal)), "bool");
        host.RegisterLibraryFunction("string.indexOf", IndexOf, IndexOfValue, "int");
        host.RegisterLibraryFunction("string.lastIndexOf", LastIndexOf, LastIndexOfValue, "int");
        host.RegisterLibraryFunction("string.localeCompare", LocaleCompare, arguments => Int(string.Compare(StringAt(arguments, 0, "String.localeCompare"), StringAt(arguments, 1, "String.localeCompare"), StringComparison.Ordinal)), "int");
        host.RegisterLibraryFunction("string.padEnd", PadEnd, arguments => PadValue(arguments, true), "string");
        host.RegisterLibraryFunction("string.padStart", PadStart, arguments => PadValue(arguments, false), "string");
        host.RegisterLibraryFunction("string.repeat", Repeat, RepeatValue, "string");
        host.RegisterLibraryFunction("string.replace", Replace, arguments => ReplaceValue(arguments, false), "string");
        host.RegisterLibraryFunction("string.replaceAll", ReplaceAll, arguments => ReplaceValue(arguments, true), "string");
        host.RegisterLibraryFunction("string.search", Search, SearchValue, "int");
        host.RegisterLibraryFunction("string.slice", Slice, SliceValue, "string");
        host.RegisterLibraryFunction("string.split", Split, SplitValue, "any[]");
        host.RegisterLibraryFunction("string.startsWith", StartsWith, arguments => Bool(StringAt(arguments, 0, "String.startsWith").StartsWith(StringAt(arguments, 1, "String.startsWith"), StringComparison.Ordinal)), "bool");
        host.RegisterLibraryFunction("string.substr", Substr, SubstrValue, "string");
        host.RegisterLibraryFunction("string.substring", Substring, SubstringValue, "string");
        host.RegisterLibraryFunction("string.toLowerCase", ToLowerCase, arguments => FluidValue.From(StringAt(arguments, 0, "String.toLowerCase").ToLowerInvariant()), "string");
        host.RegisterLibraryFunction("string.toUpperCase", ToUpperCase, arguments => FluidValue.From(StringAt(arguments, 0, "String.toUpperCase").ToUpperInvariant()), "string");
        host.RegisterLibraryFunction("string.trim", Trim, arguments => FluidValue.From(StringAt(arguments, 0, "String.trim").Trim()), "string");
        host.RegisterLibraryFunction("string.trimStart", TrimStart, arguments => FluidValue.From(StringAt(arguments, 0, "String.trimStart").TrimStart()), "string");
        host.RegisterLibraryFunction("string.trimEnd", TrimEnd, arguments => FluidValue.From(StringAt(arguments, 0, "String.trimEnd").TrimEnd()), "string");
        host.RegisterLibraryFunction("string.trimLeft", TrimLeft, arguments => FluidValue.From(StringAt(arguments, 0, "String.trimLeft").TrimStart()), "string");
        host.RegisterLibraryFunction("string.trimRight", TrimRight, arguments => FluidValue.From(StringAt(arguments, 0, "String.trimRight").TrimEnd()), "string");
        host.RegisterLibraryFunction("string.normalize", Normalize, NormalizeValue, "string");
        host.RegisterLibraryFunction("string.isWellFormed", IsWellFormed, arguments => StandardLibrarySupport.Bool(IsWellFormedValue(StringAt(arguments, 0, "String.isWellFormed"))), "bool");
        host.RegisterLibraryFunction("string.toWellFormed", ToWellFormed, arguments => FluidValue.From(ToWellFormedValue(StringAt(arguments, 0, "String.toWellFormed"))), "string");
        host.RegisterLibraryFunction("string.match", Match, arguments => MatchValue(arguments, false), "any[]");
        host.RegisterLibraryFunction("string.matchAll", MatchAll, arguments => MatchValue(arguments, true), "any[]");
        host.RegisterLibraryFunction("string.valueOf", ValueOf, arguments => FluidValue.From(StringAt(arguments, 0, "String.valueOf")), "string");
        host.RegisterLibraryFunction("string.toString", ToStringId, arguments => FluidValue.From(StringAt(arguments, 0, "String.toString")), "string");
    }

    private static FluidValue ConstructorValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 0, 1, "String");
        return FluidValue.From(arguments.Count == 0 ? string.Empty : StandardLibrarySupport.ToJsString(arguments[0]));
    }

    private static FluidValue FromCharCodeValue(IReadOnlyList<FluidValue> arguments)
    {
        var builder = new StringBuilder(arguments.Count);
        foreach (var argument in arguments)
            builder.Append((char)(StandardLibrarySupport.RequireInt(argument, "String.fromCharCode") & 0xFFFF));
        return FluidValue.From(builder.ToString());
    }

    private static FluidValue FromCodePointValue(IReadOnlyList<FluidValue> arguments)
    {
        var builder = new StringBuilder(arguments.Count);
        foreach (var argument in arguments)
        {
            var value = StandardLibrarySupport.RequireInt(argument, "String.fromCodePoint");
            if (value is < 0 or > 0x10FFFF)
                throw new StandardLibraryException("String.fromCodePoint received an invalid code point.");
            builder.Append(char.ConvertFromUtf32((int)value));
        }
        return FluidValue.From(builder.ToString());
    }

    private static FluidValue RawValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 1, "String.raw");
        return FluidValue.From(StandardLibrarySupport.ToJsString(arguments[0]));
    }

    private static FluidValue AtValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.at");
        var index = StandardLibrarySupport.RequireInt(arguments.Count > 1 ? arguments[1] : throw new StandardLibraryException("String.at is missing an argument."), "String.at");
        index = index < 0 ? value.Length + index : index;
        return index is < 0 or >= int.MaxValue || index >= value.Length ? FluidValue.Null : FluidValue.From(value[(int)index].ToString());
    }

    private static FluidValue CharAtValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.charAt");
        var index = StandardLibrarySupport.OptionalInt(arguments, 1, 0, "String.charAt");
        return index < 0 || index >= value.Length ? FluidValue.From(string.Empty) : FluidValue.From(value[(int)index].ToString());
    }

    private static FluidValue CharCodeAtValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.charCodeAt");
        var index = StandardLibrarySupport.OptionalInt(arguments, 1, 0, "String.charCodeAt");
        return index < 0 || index >= value.Length ? FluidValue.Null : FluidValue.From((long)value[(int)index]);
    }

    private static FluidValue CodePointAtValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.codePointAt");
        var index = StandardLibrarySupport.OptionalInt(arguments, 1, 0, "String.codePointAt");
        if (index < 0 || index >= value.Length)
            return FluidValue.Null;
        return FluidValue.From((long)char.ConvertToUtf32(value, (int)index));
    }

    private static FluidValue ConcatValue(IReadOnlyList<FluidValue> arguments)
    {
        if (arguments.Count == 0)
            throw new StandardLibraryException("String.concat is missing a receiver.");
        var builder = new StringBuilder(StringAt(arguments, 0, "String.concat"));
        for (var index = 1; index < arguments.Count; index++)
            builder.Append(StandardLibrarySupport.ToJsString(arguments[index]));
        return FluidValue.From(builder.ToString());
    }

    private static FluidValue IndexOfValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.indexOf");
        var search = StringAt(arguments, 1, "String.indexOf");
        var start = Math.Clamp(StandardLibrarySupport.OptionalInt(arguments, 2, 0, "String.indexOf"), 0, value.Length);
        return Int(value.IndexOf(search, (int)start, StringComparison.Ordinal));
    }

    private static FluidValue LastIndexOfValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.lastIndexOf");
        var search = StringAt(arguments, 1, "String.lastIndexOf");
        var position = Math.Clamp(StandardLibrarySupport.OptionalInt(arguments, 2, value.Length, "String.lastIndexOf"), -1, value.Length);
        return Int(position < 0 ? -1 : value.LastIndexOf(search, (int)position, StringComparison.Ordinal));
    }

    private static FluidValue PadValue(IReadOnlyList<FluidValue> arguments, bool end)
    {
        var name = end ? "String.padEnd" : "String.padStart";
        var value = StringAt(arguments, 0, name);
        var targetLength = StandardLibrarySupport.OptionalInt(arguments, 1, 0, name);
        if (targetLength <= value.Length)
            return FluidValue.From(value);
        var fill = arguments.Count > 2 ? StringAt(arguments, 2, name) : " ";
        var padding = StandardLibrarySupport.RepeatToLength(fill, checked((int)Math.Min(targetLength - value.Length, int.MaxValue)));
        return FluidValue.From(end ? value + padding : padding + value);
    }

    private static FluidValue RepeatValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 2, 2, "String.repeat");
        var value = StringAt(arguments, 0, "String.repeat");
        var count = StandardLibrarySupport.RequireInt(arguments[1], "String.repeat");
        if (count < 0 || count > int.MaxValue)
            throw new StandardLibraryException("String.repeat count is invalid.");
        return FluidValue.From(string.Concat(Enumerable.Repeat(value, (int)count)));
    }

    private static FluidValue ReplaceValue(IReadOnlyList<FluidValue> arguments, bool replaceAll)
    {
        var name = replaceAll ? "String.replaceAll" : "String.replace";
        StandardLibrarySupport.RequireRange(arguments, 3, 3, name);
        var input = StringAt(arguments, 0, name);
        var replacement = StringAt(arguments, 2, name);
        if (RegExpLibrary.TryGetRegex(arguments[1], out var regex))
        {
            if (replaceAll && !regex.Global)
                throw new StandardLibraryException("String.replaceAll requires a global regular expression.");
            return FluidValue.From(regex.Regex.Replace(input, replacement.Replace("$&", "$0", StringComparison.Ordinal), replaceAll || regex.Global ? 0 : 1));
        }
        var search = StandardLibrarySupport.ToJsString(arguments[1]);
        if (replaceAll)
            return FluidValue.From(input.Replace(search, replacement, StringComparison.Ordinal));
        var index = input.IndexOf(search, StringComparison.Ordinal);
        return FluidValue.From(index < 0 ? input : input[..index] + replacement + input[(index + search.Length)..]);
    }

    private static FluidValue SearchValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 2, 2, "String.search");
        var input = StringAt(arguments, 0, "String.search");
        var regex = RegExpLibrary.ToRegex(arguments[1]);
        var match = regex.Regex.Match(input);
        return Int(match.Success ? match.Index : -1);
    }

    private static FluidValue SliceValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.slice");
        var start = StandardLibrarySupport.NormalizeIndex(StandardLibrarySupport.OptionalInt(arguments, 1, 0, "String.slice"), value.Length);
        var end = StandardLibrarySupport.NormalizeIndex(StandardLibrarySupport.OptionalInt(arguments, 2, value.Length, "String.slice"), value.Length);
        return FluidValue.From(start >= end ? string.Empty : value[(int)start..(int)end]);
    }

    private static FluidValue SplitValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.split");
        if (arguments.Count < 2 || arguments[1].Kind == FluidValueKind.Null)
            return FluidValue.FromArray(new[] { FluidValue.From(value) });
        var separator = StandardLibrarySupport.ToJsString(arguments[1]);
        var parts = separator.Length == 0 ? value.Select(character => character.ToString()).ToArray() : value.Split(separator, StringSplitOptions.None);
        if (arguments.Count < 3)
            return FluidValue.FromArray(parts.Select(FluidValue.From).ToArray());
        var limit = StandardLibrarySupport.RequireInt(arguments[2], "String.split");
        if (limit <= 0)
            return FluidValue.FromArray(Array.Empty<FluidValue>());
        return FluidValue.FromArray(parts.Take((int)Math.Min(limit, int.MaxValue)).Select(FluidValue.From).ToArray());
    }

    private static FluidValue SubstrValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.substr");
        var start = StandardLibrarySupport.NormalizeIndex(StandardLibrarySupport.OptionalInt(arguments, 1, 0, "String.substr"), value.Length);
        if (start >= value.Length)
            return FluidValue.From(string.Empty);
        var length = StandardLibrarySupport.OptionalInt(arguments, 2, value.Length - start, "String.substr");
        return FluidValue.From(length <= 0 ? string.Empty : value[(int)start..(int)Math.Min(value.Length, start + length)]);
    }

    private static FluidValue SubstringValue(IReadOnlyList<FluidValue> arguments)
    {
        var value = StringAt(arguments, 0, "String.substring");
        var start = Math.Clamp(StandardLibrarySupport.OptionalInt(arguments, 1, 0, "String.substring"), 0, value.Length);
        var end = Math.Clamp(StandardLibrarySupport.OptionalInt(arguments, 2, value.Length, "String.substring"), 0, value.Length);
        if (start > end)
            (start, end) = (end, start);
        return FluidValue.From(value[(int)start..(int)end]);
    }

    private static FluidValue NormalizeValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 2, "String.normalize");
        var value = StringAt(arguments, 0, "String.normalize");
        var form = arguments.Count == 1 ? "NFC" : StringAt(arguments, 1, "String.normalize").ToUpperInvariant();
        var normalization = form switch
        {
            "NFC" => NormalizationForm.FormC,
            "NFD" => NormalizationForm.FormD,
            "NFKC" => NormalizationForm.FormKC,
            "NFKD" => NormalizationForm.FormKD,
            _ => throw new StandardLibraryException("String.normalize form is invalid.")
        };
        return FluidValue.From(value.Normalize(normalization));
    }

    private static FluidValue MatchValue(IReadOnlyList<FluidValue> arguments, bool all)
    {
        var name = all ? "String.matchAll" : "String.match";
        StandardLibrarySupport.RequireRange(arguments, 2, 2, name);
        var input = StringAt(arguments, 0, name);
        var regex = RegExpLibrary.ToRegex(arguments[1]);
        if (all && !regex.Global)
            throw new StandardLibraryException("String.matchAll requires a global regular expression.");
        if (all || regex.Global)
            return FluidValue.FromArray(regex.Regex.Matches(input).Select(match => FluidValue.From(match.Value)).ToArray());
        var match = regex.Regex.Match(input);
        if (!match.Success)
            return FluidValue.Null;
        return FluidValue.FromArray(Enumerable.Range(0, match.Groups.Count)
            .Select(index => match.Groups[index].Success ? FluidValue.From(match.Groups[index].Value) : FluidValue.Null)
            .ToArray());
    }

    private static bool IsWellFormedValue(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return false;
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
                return false;
        }
        return true;
    }

    private static string ToWellFormedValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    builder.Append(value[index++]).Append(value[index]);
                else
                    builder.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(value[index]))
                builder.Append('\uFFFD');
            else
                builder.Append(value[index]);
        }
        return builder.ToString();
    }

    private static FluidValue Bool(bool value) => StandardLibrarySupport.Bool(value);
    private static FluidValue Int(long value) => StandardLibrarySupport.Int(value);
    private static string StringAt(IReadOnlyList<FluidValue> arguments, int index, string name) => StandardLibrarySupport.StringAt(arguments, index, name);
}

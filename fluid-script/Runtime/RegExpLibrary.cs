using System.Text.RegularExpressions;

namespace FluidScript.Runtime;

/// <summary>Registers JavaScript-inspired regular-expression functions.</summary>
public sealed class RegExpLibrary : IRegister
{
    private const int Constructor = -50;
    private const int Escape = -51;

    public void Register(FluidScriptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.RegisterLibraryType("RegExp", typeof(FluidRegExp));
        host.RegisterLibraryFunction("RegExp", Constructor, arguments => ConstructorValue(arguments, host), "RegExp");
        host.RegisterLibraryFunction("RegExp.escape", Escape, EscapeValue, "string");
    }

    internal static FluidRegExp ToRegex(FluidValue value) => TryGetRegex(value, out var regex)
        ? regex
        : new FluidRegExp(StandardLibrarySupport.ToJsString(value));

    internal static bool TryGetRegex(FluidValue value, out FluidRegExp regex)
    {
        if (value.Kind == FluidValueKind.HostObject && value.AsHostObject().Instance is FluidRegExp hostRegex)
        {
            regex = hostRegex;
            return true;
        }
        regex = null!;
        return false;
    }

    private static FluidValue ConstructorValue(IReadOnlyList<FluidValue> arguments, FluidScriptHost host)
    {
        StandardLibrarySupport.RequireRange(arguments, 0, 2, "RegExp");
        var pattern = arguments.Count == 0 || arguments[0].Kind == FluidValueKind.Null ? string.Empty : StandardLibrarySupport.ToJsString(arguments[0]);
        var flags = arguments.Count < 2 || arguments[1].Kind == FluidValueKind.Null ? string.Empty : StandardLibrarySupport.StringAt(arguments, 1, "RegExp");
        return host.Wrap(new FluidRegExp(pattern, flags));
    }

    private static FluidValue EscapeValue(IReadOnlyList<FluidValue> arguments)
    {
        StandardLibrarySupport.RequireRange(arguments, 1, 1, "RegExp.escape");
        return FluidValue.From(Regex.Escape(StandardLibrarySupport.ToJsString(arguments[0])));
    }

}

/// <summary>A regular-expression value created by the FluidScript standard library.</summary>
public sealed class FluidRegExp
{
    private static readonly HashSet<char> SupportedFlags = new("dgimsuvy");

    public FluidRegExp(string source, string flags = "")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(flags);
        if (flags.Distinct().Count() != flags.Length || flags.Any(flag => !SupportedFlags.Contains(flag)))
            throw new ArgumentException("RegExp flags are invalid or duplicated.", nameof(flags));
        Source = source;
        Flags = new string(flags.OrderBy(flag => "dgimsuvy".IndexOf(flag)).ToArray());
        var options = RegexOptions.CultureInvariant;
        if (IgnoreCase) options |= RegexOptions.IgnoreCase;
        if (Multiline) options |= RegexOptions.Multiline;
        if (DotAll) options |= RegexOptions.Singleline;
        try
        {
            Regex = new Regex(source, options);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"The regular expression is invalid: {exception.Message}", nameof(source), exception);
        }
    }

    public string Source { get; }
    public string Flags { get; }
    private int _lastIndex;
    public int LastIndex
    {
        get => _lastIndex;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "RegExp.lastIndex cannot be negative.");
            _lastIndex = value;
        }
    }
    public bool Global => Flags.Contains('g');
    public bool IgnoreCase => Flags.Contains('i');
    public bool Multiline => Flags.Contains('m');
    public bool DotAll => Flags.Contains('s');
    public bool Unicode => Flags.Contains('u') || Flags.Contains('v');
    public bool Sticky => Flags.Contains('y');
    internal Regex Regex { get; }

    // Lowercase aliases are the JavaScript-facing member names. The CLR-style
    // properties above remain available to C# callers.
    public string source => Source;
    public string flags => Flags;
    public int lastIndex { get => LastIndex; set => LastIndex = value; }
    public bool global => Global;
    public bool ignoreCase => IgnoreCase;
    public bool multiline => Multiline;
    public bool dotAll => DotAll;
    public bool unicode => Unicode;
    public bool sticky => Sticky;

    public bool Test(string input) => Find(input) is not null;

    public FluidValue Exec(string input)
    {
        var match = Find(input);
        return match is null ? FluidValue.Null : MatchArray(match);
    }

    public bool test(string input) => Test(input);
    public FluidValue exec(string input) => Exec(input);
    public string toString() => ToString();

    public override string ToString() => $"/{Source}/{Flags}";

    private Match? Find(string input)
    {
        var stateful = Global || Sticky;
        var start = stateful ? LastIndex : 0;
        if (start < 0 || start > input.Length)
        {
            if (stateful)
                LastIndex = 0;
            return null;
        }
        var match = Regex.Match(input, start);
        if (Sticky && match.Success && match.Index != start)
            match = Match.Empty;
        if (stateful)
            LastIndex = match.Success ? match.Index + match.Length : 0;
        return match.Success ? match : null;
    }

    private static FluidValue MatchArray(Match match) => FluidValue.FromArray(Enumerable.Range(0, match.Groups.Count)
        .Select(index => match.Groups[index].Success ? FluidValue.From(match.Groups[index].Value) : FluidValue.Null)
        .ToArray());
}

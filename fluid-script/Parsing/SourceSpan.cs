namespace FluidScript.Parsing;

public readonly record struct SourceSpan(int Line, int Column, int Length)
{
    public static readonly SourceSpan None = new(0, 0, 0);

    public override string ToString() => Line <= 0 ? "<unknown>" : $"{Line}:{Column + 1}";
}

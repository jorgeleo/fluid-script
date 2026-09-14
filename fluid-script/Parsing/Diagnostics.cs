namespace FluidScript.Parsing;

public enum DiagnosticSeverity
{
    Error,
    Warning
}

public sealed record Diagnostic(
    string Code,
    string Message,
    SourceSpan Span,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error)
{
    public override string ToString() => $"{Code} {Span}: {Message}";
}

using FluidScript.Parsing;

namespace FluidScript.Runtime;

public sealed class RuntimeFaultException : Exception
{
    public RuntimeFaultException(string code, string message, SourceSpan span, string? functionName = null, FluidValue? value = null)
        : base(message)
    {
        Code = code;
        Span = span;
        FunctionName = functionName;
        Value = value;
    }

    public string Code { get; }
    public SourceSpan Span { get; }
    public string? FunctionName { get; }
    public FluidValue? Value { get; }

    public override string ToString() =>
        $"{Code} {Span}: {Message}" + (FunctionName is null ? string.Empty : $" (in {FunctionName})");
}

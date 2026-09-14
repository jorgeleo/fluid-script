using FluidScript.Compilation;
using FluidScript.Parsing;
using FluidScript.Runtime;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/run", (RunRequest request) =>
{
    if (request is null || request.Source is null)
        return Results.BadRequest(new { message = "A source field is required." });

    var compilation = FluidScriptCompiler.Compile(request.Source);
    if (!compilation.Success)
    {
        return Results.Ok(new RunResponse(
            false,
            compilation.Diagnostics.Select(DiagnosticDto.From).ToArray(),
            Array.Empty<string>(),
            null));
    }

    var output = new List<string>();
    try
    {
        var value = compilation.Execute(output.Add);
        return Results.Ok(new RunResponse(true, Array.Empty<DiagnosticDto>(), output, value.ToString()));
    }
    catch (RuntimeFaultException fault)
    {
        var diagnostic = new DiagnosticDto(
            fault.Code,
            fault.Message,
            fault.Span.Line,
            fault.Span.Column,
            fault.Span.Length,
            fault.FunctionName);
        return Results.Ok(new RunResponse(false, new[] { diagnostic }, output, null));
    }
});

app.MapFallbackToFile("index.html");
app.Run();

public sealed record RunRequest(string? Source);

public sealed record RunResponse(
    bool Success,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    IReadOnlyList<string> Output,
    string? Result);

public sealed record DiagnosticDto(
    string Code,
    string Message,
    int Line,
    int Column,
    int Length,
    string? FunctionName = null)
{
    public static DiagnosticDto From(Diagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Span.Line,
        diagnostic.Span.Column,
        diagnostic.Span.Length);
}

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

app.MapPost("/api/debug/start", (DebugStartRequest request) =>
{
    if (request is null || request.Source is null)
        return Results.BadRequest(new { message = "A source field is required." });
    var compilation = FluidScriptCompiler.Compile(request.Source);
    if (!compilation.Success)
        return Results.Ok(DebugRunResponse.CompilationFailed(compilation));

    return RunDebug(compilation, (machine, writeOutput) => machine.RunDebug(
        compilation.Module!, request.BreakLines ?? Array.Empty<int>(), new FluidScriptExecutionContext(output: writeOutput), 1_000_000));
});

app.MapPost("/api/debug/continue", (DebugContinueRequest request) =>
{
    if (request is null || request.Source is null || string.IsNullOrWhiteSpace(request.State))
        return Results.BadRequest(new { message = "Source and state fields are required." });
    var compilation = FluidScriptCompiler.Compile(request.Source);
    if (!compilation.Success)
        return Results.Ok(DebugRunResponse.CompilationFailed(compilation));

    try
    {
        var state = PCodeDebugStateJson.Deserialize(request.State);
        ApplyEdits(compilation.Module!, state, request.Edits ?? Array.Empty<DebugVariableEdit>());
        return RunDebug(compilation, (machine, writeOutput) => machine.RunFromDebugState(
            compilation.Module!, state, new FluidScriptExecutionContext(output: writeOutput), 1_000_000));
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidDataException or System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult RunDebug(CompilationResult compilation, Func<VirtualMachine, Action<string>, DebugExecutionResult> execute)
{
    var output = new List<string>();
    try
    {
        var machine = new VirtualMachine();
        DebugExecutionResult execution;
        // The callback belongs to the request, never to a retained debug session.
        execution = execute(machine, output.Add);
        return Results.Ok(DebugRunResponse.From(execution, compilation.Module!, output));
    }
    catch (RuntimeFaultException fault)
    {
        return Results.Ok(new DebugRunResponse(false, false, new[] { DiagnosticDto.From(fault) }, output, null, null, null, Array.Empty<DebugVariableDto>(), Array.Empty<DebugFrameDto>()));
    }
    catch (InvalidDataException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
}

static void ApplyEdits(PCodeModule module, PCodeDebugState state, IReadOnlyList<DebugVariableEdit> edits)
{
    foreach (var edit in edits)
    {
        if (string.IsNullOrWhiteSpace(edit.Name) || edit.ValueJson is null)
            throw new ArgumentException("A debug variable edit is invalid.");
        var value = FluidJson.Deserialize(edit.ValueJson);
        if (string.Equals(edit.Scope, "global", StringComparison.Ordinal))
        {
            var globalIndex = Enumerable.Range(0, module.GlobalNames.Count).FirstOrDefault(index =>
                string.Equals(module.GlobalNames[index], edit.Name, StringComparison.Ordinal));
            if (globalIndex < 0 || !string.Equals(module.GlobalNames[globalIndex], edit.Name, StringComparison.Ordinal))
                throw new ArgumentException($"The global debug variable '{edit.Name}' was not found.");
            state.SetGlobal(globalIndex, value);
            continue;
        }
        if (edit.FrameIndex < 0 || edit.FrameIndex >= state.Frames.Count)
            throw new ArgumentException("A debug variable edit is invalid.");
        var frame = state.Frames[edit.FrameIndex];
        var function = module.Functions[frame.FunctionId];
        var localIndex = Enumerable.Range(0, frame.Locals.Count).FirstOrDefault(index =>
            string.Equals(GetLocalName(function, index), edit.Name, StringComparison.Ordinal));
        if (localIndex >= 0 && string.Equals(GetLocalName(function, localIndex), edit.Name, StringComparison.Ordinal))
        {
            frame.Locals[localIndex].Value = value;
            continue;
        }
        var captureName = edit.Name.StartsWith("$capture:", StringComparison.Ordinal) ? edit.Name[9..] : edit.Name;
        var captureIndex = Enumerable.Range(0, frame.Captures.Count).FirstOrDefault(index =>
            string.Equals(GetCaptureName(function, index), captureName, StringComparison.Ordinal));
        if (captureIndex >= 0 && string.Equals(GetCaptureName(function, captureIndex), captureName, StringComparison.Ordinal))
        {
            frame.Captures[captureIndex].Value = value;
            continue;
        }
        throw new ArgumentException($"The debug variable '{edit.Name}' was not found.");
    }
}

static string GetLocalName(PCodeFunction function, int index) =>
    function.LocalNames.TryGetValue(index, out var name) ? name : $"$local{index}";

static string GetCaptureName(PCodeFunction function, int index) =>
    index < function.CaptureNames.Count ? function.CaptureNames[index] : $"$capture{index}";

public sealed record RunRequest(string? Source);

public sealed record DebugStartRequest(string? Source, IReadOnlyList<int>? BreakLines);

public sealed record DebugContinueRequest(string? Source, string? State, IReadOnlyList<DebugVariableEdit>? Edits);

public sealed record DebugVariableEdit(int FrameIndex, string? Name, string? ValueJson, string? Scope = null);

public sealed record RunResponse(
    bool Success,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    IReadOnlyList<string> Output,
    string? Result);

public sealed record DebugRunResponse(
    bool Success,
    bool Stopped,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    IReadOnlyList<string> Output,
    string? Result,
    string? State,
    int? Line,
    IReadOnlyList<DebugVariableDto> Globals,
    IReadOnlyList<DebugFrameDto> Frames)
{
    public static DebugRunResponse CompilationFailed(CompilationResult compilation) => new(
        false, false, compilation.Diagnostics.Select(DiagnosticDto.From).ToArray(), Array.Empty<string>(), null, null, null, Array.Empty<DebugVariableDto>(), Array.Empty<DebugFrameDto>());

    public static DebugRunResponse From(DebugExecutionResult execution, PCodeModule module, IReadOnlyList<string> output)
    {
        var state = execution.State;
        return new DebugRunResponse(
            true,
            execution.IsStopped,
            Array.Empty<DiagnosticDto>(),
            output,
            execution.Value?.ToString(),
            state is null ? null : PCodeDebugStateJson.Serialize(state),
            state?.Line,
            state is null ? Array.Empty<DebugVariableDto>() : module.GlobalNames.Select((name, index) => DebugVariableDto.From(name, state.Globals[index])).ToArray(),
            state is null ? Array.Empty<DebugFrameDto>() : CreateFrames(module, state));
    }

    private static IReadOnlyList<DebugFrameDto> CreateFrames(PCodeModule module, PCodeDebugState state) => state.Frames
        .Select((frame, index) => new DebugFrameDto(
            index,
            module.Functions[frame.FunctionId].Name,
            frame.InstructionPointer,
            frame.Variables.Select(variable => DebugVariableDto.From(variable.Key, variable.Value)).ToArray()))
        .ToArray();
}

public sealed record DebugFrameDto(int Index, string FunctionName, int InstructionPointer, IReadOnlyList<DebugVariableDto> Variables);

public sealed record DebugVariableDto(string Name, string DisplayValue, string? JsonValue, bool Editable)
{
    public static DebugVariableDto From(string name, FluidValue value)
    {
        try
        {
            return new DebugVariableDto(name, value.ToString(), FluidJson.Serialize(value), true);
        }
        catch (InvalidOperationException)
        {
            return new DebugVariableDto(name, value.ToString(), null, false);
        }
    }
}

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

    public static DiagnosticDto From(RuntimeFaultException fault) => new(
        fault.Code,
        fault.Message,
        fault.Span.Line,
        fault.Span.Column,
        fault.Span.Length,
        fault.FunctionName);
}

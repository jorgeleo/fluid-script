using FluidScript.Parsing;

namespace FluidScript.Runtime;

/// <summary>The outcome of a debug execution request.</summary>
public sealed class DebugExecutionResult
{
    internal DebugExecutionResult(FluidValue? value, PCodeDebugState? state)
    {
        Value = value;
        State = state;
    }

    /// <summary>The final result when execution completed; otherwise null.</summary>
    public FluidValue? Value { get; }
    /// <summary>A complete, detached continuation state when execution stopped; otherwise null.</summary>
    public PCodeDebugState? State { get; }
    public bool IsStopped => State is not null;
}

/// <summary>
/// All VM state required to continue debug P-code without retaining a VM session.
/// The instruction pointer, rather than the display line alone, identifies the exact
/// next instruction when a source line has more than one instruction.
/// </summary>
public sealed class PCodeDebugState
{
    private readonly FluidValue[] globals;

    public PCodeDebugState(
        ReadOnlyMemory<byte> pCodeHash,
        int line,
        IReadOnlyList<PCodeDebugFrame> frames,
        IReadOnlyList<FluidValue> operandStack,
        IReadOnlyList<FluidValue> globals,
        int instructionsExecuted = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(operandStack);
        ArgumentNullException.ThrowIfNull(globals);
        PCodeHash = pCodeHash.ToArray();
        Line = line;
        Frames = frames.ToArray();
        OperandStack = operandStack.ToArray();
        this.globals = globals.ToArray();
        Globals = this.globals;
        InstructionsExecuted = instructionsExecuted;
    }

    /// <summary>SHA-256 of the deterministic debug P-code payload.</summary>
    public ReadOnlyMemory<byte> PCodeHash { get; }
    /// <summary>The source line of the next instruction to execute.</summary>
    public int Line { get; }
    /// <summary>Call frames from entry to the currently executing frame.</summary>
    public IReadOnlyList<PCodeDebugFrame> Frames { get; }
    /// <summary>The complete VM operand stack.</summary>
    public IReadOnlyList<FluidValue> OperandStack { get; }
    /// <summary>Global-slot values in the module's deterministic global-name order.</summary>
    public IReadOnlyList<FluidValue> Globals { get; }
    /// <summary>Instructions already executed in this logical run.</summary>
    public int InstructionsExecuted { get; }

    /// <summary>Replaces one global-slot value before detached execution resumes.</summary>
    public void SetGlobal(int slot, FluidValue value)
    {
        if (slot < 0 || slot >= globals.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));
        globals[slot] = value;
    }
}

/// <summary>One call frame in a detached debug state.</summary>
public sealed class PCodeDebugFrame
{
    public PCodeDebugFrame(
        int functionId,
        int instructionPointer,
        int stackBase,
        IReadOnlyList<FluidCell> locals,
        IReadOnlyList<FluidCell>? captures = null,
        IReadOnlyList<PCodeDebugExceptionHandler>? handlers = null,
        RuntimeFaultException? pendingFault = null,
        SourceSpan currentSpan = default,
        IReadOnlyDictionary<string, FluidValue>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(locals);
        FunctionId = functionId;
        InstructionPointer = instructionPointer;
        StackBase = stackBase;
        Locals = locals.ToArray();
        Captures = captures?.ToArray() ?? Array.Empty<FluidCell>();
        Handlers = handlers?.ToArray() ?? Array.Empty<PCodeDebugExceptionHandler>();
        PendingFault = pendingFault;
        CurrentSpan = currentSpan;
        Variables = variables is null
            ? new Dictionary<string, FluidValue>(StringComparer.Ordinal)
            : new Dictionary<string, FluidValue>(variables, StringComparer.Ordinal);
    }

    public int FunctionId { get; }
    /// <summary>Zero-based offset of the next instruction to execute.</summary>
    public int InstructionPointer { get; }
    public int StackBase { get; }
    /// <summary>Local cells, retained to preserve aliases held by closures.</summary>
    public IReadOnlyList<FluidCell> Locals { get; }
    /// <summary>Captured cells, retained to preserve aliases held by closures.</summary>
    public IReadOnlyList<FluidCell> Captures { get; }
    public IReadOnlyList<PCodeDebugExceptionHandler> Handlers { get; }
    public RuntimeFaultException? PendingFault { get; }
    public SourceSpan CurrentSpan { get; }
    /// <summary>Named local and captured values for debugger display.</summary>
    public IReadOnlyDictionary<string, FluidValue> Variables { get; }
}

/// <summary>Exception-handler state required to resume a frame faithfully.</summary>
public readonly record struct PCodeDebugExceptionHandler(int Target, int StackDepth, int FilterKind);

using FluidScript.Parsing;

namespace FluidScript.Runtime;

public enum OpCode
{
    Const,
    Null,
    LoadLocal,
    LoadCell,
    StoreLocal,
    LoadCapture,
    LoadCaptureCell,
    StoreCapture,
    LoadGlobal,
    StoreGlobal,
    Dup,
    Dup2,
    Swap,
    Pop,
    Add,
    ToText,
    Sub,
    Mul,
    Div,
    Mod,
    Neg,
    Not,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Jump,
    JumpIfFalse,
    Call,
    CallIndirect,
    CallNative,
    MakeClosure,
    NewObject,
    FieldGet,
    FieldSet,
    EnterHandler,
    LeaveHandler,
    Throw,
    Rethrow,
    Return,
    ReturnVoid,
    MakeArray,
    IndexGet,
    IndexSet,
    ForCheckLocal,
    ForIncrementLocal,
    ForCheckGlobal,
    ForIncrementGlobal,
    Halt
}

public readonly record struct Instruction(
    OpCode OpCode,
    int OperandA = 0,
    int OperandB = 0,
    int OperandC = 0,
    SourceSpan Span = default,
    int OperandD = 0);

public sealed class PCodeFunction
{
    public PCodeFunction(
        string name,
        int arity,
        int localCount,
        IReadOnlyList<Instruction> instructions,
        IReadOnlyDictionary<int, string>? localNames = null,
        IReadOnlyList<string>? captureNames = null)
    {
        Name = name;
        Arity = arity;
        LocalCount = localCount;
        Instructions = instructions;
        LocalNames = localNames ?? new Dictionary<int, string>();
        CaptureNames = captureNames ?? Array.Empty<string>();
    }

    public string Name { get; }
    public int Arity { get; }
    public int LocalCount { get; }
    public IReadOnlyList<Instruction> Instructions { get; }
    public IReadOnlyDictionary<int, string> LocalNames { get; }
    public IReadOnlyList<string> CaptureNames { get; }
}

public sealed class PCodeModule
{
    public PCodeModule(
        IReadOnlyList<FluidValue> constants,
        IReadOnlyList<PCodeFunction> functions,
        int entryFunction = 0)
    {
        Constants = constants;
        Functions = functions;
        EntryFunction = entryFunction;
    }

    public IReadOnlyList<FluidValue> Constants { get; }
    public IReadOnlyList<PCodeFunction> Functions { get; }
    public int EntryFunction { get; }
    public int GlobalCount { get; internal set; }
    public IReadOnlyList<string> GlobalNames { get; internal set; } = Array.Empty<string>();
    public IReadOnlyList<PCodeType> Types { get; internal set; } = Array.Empty<PCodeType>();
    /// <summary>SHA-256 of the exact UTF-8 source text used to compile this module.</summary>
    public ReadOnlyMemory<byte> SourceHash { get; internal set; }
}

public sealed class PCodeType
{
    public PCodeType(string name, IReadOnlyList<string> fieldNames, IReadOnlySet<string>? constantFields = null)
    {
        Name = name;
        FieldNames = fieldNames;
        ConstantFields = constantFields ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public string Name { get; }
    public IReadOnlyList<string> FieldNames { get; }
    public IReadOnlySet<string> ConstantFields { get; }
}

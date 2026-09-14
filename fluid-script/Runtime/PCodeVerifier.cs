using FluidScript.Parsing;

namespace FluidScript.Runtime;

public static class PCodeVerifier
{
    public static IReadOnlyList<Diagnostic> Verify(PCodeModule module)
    {
        var diagnostics = new List<Diagnostic>();
        if (module.Functions.Count == 0)
        {
            diagnostics.Add(new Diagnostic("FS4000", "A P-code module must contain an entry function.", SourceSpan.None));
            return diagnostics;
        }

        if (module.EntryFunction < 0 || module.EntryFunction >= module.Functions.Count)
            diagnostics.Add(new Diagnostic("FS4001", "The module entry function is invalid.", SourceSpan.None));
        if (module.GlobalCount < 0 || module.GlobalCount != module.GlobalNames.Count)
            diagnostics.Add(new Diagnostic("FS4001", "Global slot metadata is inconsistent.", SourceSpan.None));

        for (var functionId = 0; functionId < module.Functions.Count; functionId++)
            VerifyFunction(module, functionId, diagnostics);
        return diagnostics;
    }

    private static void VerifyFunction(PCodeModule module, int functionId, ICollection<Diagnostic> diagnostics)
    {
        var function = module.Functions[functionId];
        if (function.Arity < 0 || function.Arity > function.LocalCount)
            diagnostics.Add(new Diagnostic("FS4001", "Function arity is outside its local-slot range.", SourceSpan.None));
        if (function.Instructions.Count == 0)
        {
            diagnostics.Add(new Diagnostic("FS4002", "A function must contain at least one instruction.", SourceSpan.None));
            return;
        }

        // Check metadata and branch targets even when an instruction is unreachable.
        for (var ip = 0; ip < function.Instructions.Count; ip++)
        {
            var instruction = function.Instructions[ip];
            if (instruction.OpCode is OpCode.Jump or OpCode.JumpIfFalse)
                ValidateTarget(instruction.OperandA, function, instruction, diagnostics);
            if (instruction.OpCode is OpCode.ForCheckLocal or OpCode.ForCheckGlobal)
                ValidateTarget(instruction.OperandD, function, instruction, diagnostics);
            if (instruction.OpCode == OpCode.EnterHandler)
                ValidateTarget(instruction.OperandA, function, instruction, diagnostics);
            ValidateSlots(module, function, instruction, diagnostics);
        }

        var depths = new Dictionary<int, int> { [0] = 0 };
        var work = new Queue<int>(new[] { 0 });
        while (work.Count > 0)
        {
            var ip = work.Dequeue();
            if (ip < 0 || ip >= function.Instructions.Count)
            {
                diagnostics.Add(new Diagnostic("FS4002", "Control flow targets an invalid instruction.", SourceSpan.None));
                continue;
            }

            var instruction = function.Instructions[ip];
            var depth = depths[ip];
            if (!TryApply(instruction, depth, module, out var nextDepth, out var error))
            {
                diagnostics.Add(new Diagnostic("FS4004", error!, instruction.Span));
                continue;
            }

            if (instruction.OpCode == OpCode.Return && depth != 1)
                diagnostics.Add(new Diagnostic("FS4006", "Return must have exactly one value on the operand stack.", instruction.Span));
            if (instruction.OpCode == OpCode.ReturnVoid && depth != 0)
                diagnostics.Add(new Diagnostic("FS4006", "ReturnVoid requires an empty operand stack.", instruction.Span));
            if (instruction.OpCode == OpCode.Halt && depth > 1)
                diagnostics.Add(new Diagnostic("FS4006", "Halt cannot leave more than one value on the operand stack.", instruction.Span));

            foreach (var successor in Successors(function, ip, instruction, nextDepth))
            {
                if (successor.Ip < 0 || successor.Ip >= function.Instructions.Count)
                {
                    diagnostics.Add(new Diagnostic("FS4002", "Control flow targets an invalid instruction.", instruction.Span));
                    continue;
                }
                if (depths.TryGetValue(successor.Ip, out var existing))
                {
                    if (existing != successor.Depth)
                        diagnostics.Add(new Diagnostic("FS4005", "Stack depth differs at a control-flow join.", function.Instructions[successor.Ip].Span));
                }
                else
                {
                    depths[successor.Ip] = successor.Depth;
                    work.Enqueue(successor.Ip);
                }
            }
        }
    }

    private static void ValidateTarget(int target, PCodeFunction function, Instruction instruction, ICollection<Diagnostic> diagnostics)
    {
        if (target < 0 || target >= function.Instructions.Count)
            diagnostics.Add(new Diagnostic("FS4002", "Control flow targets an invalid instruction.", instruction.Span));
    }

    private static void ValidateSlots(PCodeModule module, PCodeFunction function, Instruction instruction, ICollection<Diagnostic> diagnostics)
    {
        var local = instruction.OpCode is OpCode.LoadLocal or OpCode.LoadCell or OpCode.StoreLocal or OpCode.ForCheckLocal or OpCode.ForIncrementLocal;
        var global = instruction.OpCode is OpCode.LoadGlobal or OpCode.StoreGlobal or OpCode.ForCheckGlobal or OpCode.ForIncrementGlobal;
        var slots = local ? function.LocalCount : module.GlobalCount;
        if (local && (instruction.OperandA < 0 || instruction.OperandA >= slots))
            diagnostics.Add(new Diagnostic("FS4003", "Instruction references an invalid local slot.", instruction.Span));
        if (global && (instruction.OperandA < 0 || instruction.OperandA >= slots))
            diagnostics.Add(new Diagnostic("FS4003", "Instruction references an invalid global slot.", instruction.Span));
        if (instruction.OpCode is OpCode.LoadCapture or OpCode.LoadCaptureCell or OpCode.StoreCapture)
            if (instruction.OperandA < 0 || instruction.OperandA >= function.CaptureNames.Count)
                diagnostics.Add(new Diagnostic("FS4003", "Instruction references an invalid capture slot.", instruction.Span));

        if (instruction.OpCode is OpCode.ForCheckLocal or OpCode.ForCheckGlobal)
        {
            if (instruction.OperandB < 0 || instruction.OperandB >= slots || instruction.OperandC < 0 || instruction.OperandC >= slots)
                diagnostics.Add(new Diagnostic("FS4003", "for instruction references an invalid bound or step slot.", instruction.Span));
        }
        else if (instruction.OpCode is OpCode.ForIncrementLocal or OpCode.ForIncrementGlobal)
        {
            if (instruction.OperandB < 0 || instruction.OperandB >= slots)
                diagnostics.Add(new Diagnostic("FS4003", "for increment references an invalid step slot.", instruction.Span));
        }
    }

    private static IEnumerable<Successor> Successors(PCodeFunction function, int ip, Instruction instruction, int nextDepth)
    {
        switch (instruction.OpCode)
        {
            case OpCode.Jump:
                yield return new Successor(instruction.OperandA, nextDepth);
                yield break;
            case OpCode.JumpIfFalse:
                yield return new Successor(instruction.OperandA, nextDepth);
                if (ip + 1 < function.Instructions.Count)
                    yield return new Successor(ip + 1, nextDepth);
                yield break;
            case OpCode.EnterHandler:
                if (ip + 1 < function.Instructions.Count)
                    yield return new Successor(ip + 1, nextDepth);
                yield return new Successor(instruction.OperandA, nextDepth + 1);
                yield break;
            case OpCode.Return:
            case OpCode.ReturnVoid:
            case OpCode.Throw:
            case OpCode.Rethrow:
            case OpCode.Halt:
                yield break;
            default:
                if (ip + 1 < function.Instructions.Count)
                    yield return new Successor(ip + 1, nextDepth);
                yield break;
        }
    }

    private readonly record struct Successor(int Ip, int Depth);

    private static bool TryApply(
        Instruction instruction,
        int depth,
        PCodeModule module,
        out int nextDepth,
        out string? error)
    {
        error = null;
        var delta = instruction.OpCode switch
        {
            OpCode.Const or OpCode.Null or OpCode.LoadLocal or OpCode.LoadGlobal or OpCode.LoadCell or OpCode.LoadCapture or OpCode.LoadCaptureCell or OpCode.Dup => 1,
            OpCode.Dup2 => 2,
            OpCode.Swap => 0,
            OpCode.StoreLocal or OpCode.StoreGlobal or OpCode.Pop => -1,
            OpCode.StoreCapture => -1,
            OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.Mod or
                OpCode.Equal or OpCode.NotEqual or OpCode.Less or OpCode.LessOrEqual or
                OpCode.Greater or OpCode.GreaterOrEqual => -1,
            OpCode.MakeArray => 1 - instruction.OperandA,
            OpCode.MakeDictionary => instruction.OperandA is >= 0 and <= int.MaxValue / 2
                ? 1 - (2 * instruction.OperandA)
                : 0,
            OpCode.IndexGet => -1,
            OpCode.IndexSet => -3,
            OpCode.NewObject => 1 - instruction.OperandB,
            OpCode.FieldGet => 0,
            OpCode.FieldSet => -2,
            OpCode.MakeClosure => 1 - instruction.OperandB,
            OpCode.ToText or OpCode.Neg or OpCode.Not or OpCode.Jump or OpCode.JumpIfFalse or
                OpCode.EnterHandler or OpCode.LeaveHandler or
                OpCode.ForCheckLocal or OpCode.ForIncrementLocal or OpCode.ForCheckGlobal or
                OpCode.ForIncrementGlobal => instruction.OpCode == OpCode.JumpIfFalse ? -1 : 0,
            OpCode.Call or OpCode.CallNative => 1 - instruction.OperandB,
            OpCode.CallIndirect => -instruction.OperandB,
            OpCode.Return or OpCode.Throw or OpCode.Rethrow => -1,
            OpCode.ReturnVoid or OpCode.Halt => 0,
            _ => 0
        };

        if (instruction.OpCode == OpCode.Call &&
            (instruction.OperandB < 0 || instruction.OperandA < 0 || instruction.OperandA >= module.Functions.Count ||
             module.Functions[instruction.OperandA].Arity != instruction.OperandB))
            error = "Call instruction has an invalid function or arity.";
        if (instruction.OpCode == OpCode.CallNative && instruction.OperandB < 0)
            error = "Native call has an invalid argument count.";
        if (instruction.OpCode == OpCode.CallNative && instruction.OperandA == FluidScriptHost.JsonDeserializeAsBuiltinId &&
            (instruction.OperandB != 1 || instruction.OperandC < 0 || instruction.OperandC >= module.Types.Count))
            error = "jsonDeserializeAs has an invalid argument count or object type.";
        if (instruction.OpCode == OpCode.CallIndirect && instruction.OperandB < 0)
            error = "Indirect call has an invalid argument count.";
        if (instruction.OpCode == OpCode.MakeClosure &&
            (instruction.OperandA < 0 || instruction.OperandA >= module.Functions.Count))
            error = "Closure instruction has an invalid function index.";
        if (instruction.OpCode == OpCode.MakeArray && instruction.OperandA < 0)
            error = "Array instruction has an invalid element count.";
        if (instruction.OpCode == OpCode.MakeDictionary && instruction.OperandA is < 0 or > int.MaxValue / 2)
            error = "Dictionary instruction has an invalid entry count.";
        if (instruction.OpCode == OpCode.NewObject &&
            (instruction.OperandA < 0 || instruction.OperandA >= module.Types.Count || instruction.OperandB < 0 ||
             instruction.OperandB != module.Types[instruction.OperandA].FieldNames.Count))
            error = "Object instruction has an invalid type or field count.";
        if (instruction.OpCode is (OpCode.FieldGet or OpCode.FieldSet) && instruction.OperandA < 0)
            error = "Object member instruction has an invalid field index.";
        if (instruction.OpCode == OpCode.Const &&
            (instruction.OperandA < 0 || instruction.OperandA >= module.Constants.Count))
            error = "Constant instruction has an invalid index.";
        if (instruction.OpCode is (OpCode.Jump or OpCode.JumpIfFalse) && instruction.OperandA < 0)
            error = "Conditional instruction has an invalid operand.";
        if (instruction.OpCode is (OpCode.ForCheckLocal or OpCode.ForCheckGlobal) &&
            (instruction.OperandA < 0 || instruction.OperandB < 0 || instruction.OperandC < 0 || instruction.OperandD < 0))
            error = "for instruction has an invalid slot or target.";

        var requiredDepth = RequiredDepth(instruction);
        if (error is null && depth < requiredDepth)
            error = $"Instruction requires at least {requiredDepth} value(s) on the operand stack.";
        nextDepth = depth + delta;
        if (error is null && nextDepth < 0)
            error = "Instruction consumes more values than are available on the stack.";
        return error is null;
    }

    private static int RequiredDepth(Instruction instruction) => instruction.OpCode switch
    {
        OpCode.Dup2 or OpCode.Swap => 2,
        OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.Mod or
            OpCode.Equal or OpCode.NotEqual or OpCode.Less or OpCode.LessOrEqual or
            OpCode.Greater or OpCode.GreaterOrEqual => 2,
        OpCode.IndexSet => 3,
        OpCode.MakeArray => Math.Max(0, instruction.OperandA),
        OpCode.MakeDictionary => instruction.OperandA is >= 0 and <= int.MaxValue / 2
            ? 2 * instruction.OperandA
            : int.MaxValue,
        OpCode.NewObject => Math.Max(0, instruction.OperandB),
        OpCode.Call or OpCode.CallNative => Math.Max(0, instruction.OperandB),
        OpCode.CallIndirect => Math.Max(0, instruction.OperandB + 1),
        OpCode.MakeClosure => Math.Max(0, instruction.OperandB),
        OpCode.Const or OpCode.Null or OpCode.LoadLocal or OpCode.LoadGlobal or OpCode.LoadCell or
            OpCode.LoadCapture or OpCode.LoadCaptureCell => 0,
        OpCode.StoreLocal or OpCode.StoreGlobal or OpCode.StoreCapture or OpCode.Pop or OpCode.Neg or
            OpCode.Not or OpCode.JumpIfFalse or OpCode.ToText or OpCode.IndexGet or OpCode.FieldGet or
            OpCode.Return or OpCode.Throw or OpCode.Rethrow => 1,
        OpCode.FieldSet => 2,
        _ => 0
    };
}

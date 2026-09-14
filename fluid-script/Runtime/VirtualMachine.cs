using System.Globalization;
using FluidScript.Parsing;

namespace FluidScript.Runtime;

public delegate FluidValue NativeFunction(IReadOnlyList<FluidValue> arguments);

public sealed class VirtualMachine
{
    private const int PrintBuiltin = 0;
    private const int MaxStack = 100_000;
    private const int MaxCallDepth = 1_024;

    public FluidValue Run(PCodeModule module, Action<string>? output = null, int instructionLimit = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module.EntryFunction < 0 || module.EntryFunction >= module.Functions.Count)
            throw new ArgumentException("The P-code entry function is invalid.", nameof(module));

        var globals = Enumerable.Repeat(FluidValue.Null, module.GlobalCount).ToArray();
        var stack = new List<FluidValue>();
        var frames = new List<Frame>
        {
            new(module.Functions[module.EntryFunction], module.Functions[module.EntryFunction].LocalCount, 0, 0, null)
        };
        var steps = 0;

        while (frames.Count > 0)
        {
            if (++steps > instructionLimit)
                Fault("FS5004", "The instruction limit was exceeded.", frames[^1].CurrentSpan, frames[^1].Function.Name);

            var frame = frames[^1];
            if (frame.Ip < 0 || frame.Ip >= frame.Function.Instructions.Count)
                Fault("FS4002", "Instruction pointer is outside the function.", frame.CurrentSpan, frame.Function.Name);

            var instruction = frame.Function.Instructions[frame.Ip++];
            frame.CurrentSpan = instruction.Span;

            try
            {
                switch (instruction.OpCode)
                {
                case OpCode.Const:
                    Push(stack, GetConstant(module, instruction.OperandA), frame);
                    break;
                case OpCode.Null:
                    Push(stack, FluidValue.Null, frame);
                    break;
                case OpCode.LoadLocal:
                    Push(stack, GetLocal(frame, instruction.OperandA), frame);
                    break;
                case OpCode.LoadCell:
                    Push(stack, FluidValue.FromCell(GetLocalCell(frame, instruction.OperandA)), frame);
                    break;
                case OpCode.StoreLocal:
                    SetLocal(frame, instruction.OperandA, Pop(stack, frame));
                    break;
                case OpCode.LoadCapture:
                    Push(stack, GetCapture(frame, instruction.OperandA).Value, frame);
                    break;
                case OpCode.LoadCaptureCell:
                    Push(stack, FluidValue.FromCell(GetCapture(frame, instruction.OperandA)), frame);
                    break;
                case OpCode.StoreCapture:
                    GetCapture(frame, instruction.OperandA).Value = Pop(stack, frame);
                    break;
                case OpCode.LoadGlobal:
                    Push(stack, GetGlobal(globals, instruction.OperandA), frame);
                    break;
                case OpCode.StoreGlobal:
                    SetGlobal(globals, instruction.OperandA, Pop(stack, frame));
                    break;
                case OpCode.Dup:
                    Push(stack, Peek(stack, frame), frame);
                    break;
                case OpCode.Dup2:
                    Dup2(stack, frame);
                    break;
                case OpCode.Swap:
                    Swap(stack, frame);
                    break;
                case OpCode.Pop:
                    Pop(stack, frame);
                    break;
                case OpCode.Add:
                    Push(stack, Add(Pop(stack, frame), Pop(stack, frame), frame), frame);
                    break;
                case OpCode.ToText:
                    Push(stack, FluidValue.From(Pop(stack, frame).ToString()), frame);
                    break;
                case OpCode.Sub:
                    Push(stack, Numeric(Pop(stack, frame), Pop(stack, frame), (left, right) => left - right, frame), frame);
                    break;
                case OpCode.Mul:
                    Push(stack, Numeric(Pop(stack, frame), Pop(stack, frame), (left, right) => left * right, frame), frame);
                    break;
                case OpCode.Div:
                    Push(stack, Divide(Pop(stack, frame), Pop(stack, frame), frame), frame);
                    break;
                case OpCode.Mod:
                    Push(stack, Modulo(Pop(stack, frame), Pop(stack, frame), frame), frame);
                    break;
                case OpCode.Neg:
                    Push(stack, Negate(Pop(stack, frame), frame), frame);
                    break;
                case OpCode.Not:
                    Push(stack, FluidValue.From(!RequireBool(Pop(stack, frame), frame)), frame);
                    break;
                case OpCode.Equal:
                    Push(stack, FluidValue.From(Equals(Pop(stack, frame), Pop(stack, frame))), frame);
                    break;
                case OpCode.NotEqual:
                    Push(stack, FluidValue.From(!Equals(Pop(stack, frame), Pop(stack, frame))), frame);
                    break;
                case OpCode.Less:
                    Push(stack, Comparison(Pop(stack, frame), Pop(stack, frame), (left, right) => left < right, frame), frame);
                    break;
                case OpCode.LessOrEqual:
                    Push(stack, Comparison(Pop(stack, frame), Pop(stack, frame), (left, right) => left <= right, frame), frame);
                    break;
                case OpCode.Greater:
                    Push(stack, Comparison(Pop(stack, frame), Pop(stack, frame), (left, right) => left > right, frame), frame);
                    break;
                case OpCode.GreaterOrEqual:
                    Push(stack, Comparison(Pop(stack, frame), Pop(stack, frame), (left, right) => left >= right, frame), frame);
                    break;
                case OpCode.Jump:
                    frame.Ip = instruction.OperandA;
                    break;
                case OpCode.JumpIfFalse:
                    if (!RequireBool(Pop(stack, frame), frame))
                        frame.Ip = instruction.OperandA;
                    break;
                case OpCode.Call:
                    Call(module, instruction, stack, frames, frame);
                    break;
                case OpCode.CallIndirect:
                    CallIndirect(module, instruction, stack, frames, frame);
                    break;
                case OpCode.CallNative:
                    CallNative(instruction, stack, frame, output);
                    break;
                case OpCode.MakeClosure:
                    if (instruction.OperandA < 0 || instruction.OperandA >= module.Functions.Count)
                        Fault("FS2004", "Function index is invalid.", instruction.Span, frame.Function.Name);
                    var captures = new FluidCell[instruction.OperandB];
                    for (var captureIndex = captures.Length - 1; captureIndex >= 0; captureIndex--)
                    {
                        var capture = Pop(stack, frame);
                        if (capture.Kind != FluidValueKind.Cell)
                            Fault("FS5009", "A closure capture is not a cell.", instruction.Span, frame.Function.Name);
                        captures[captureIndex] = capture.AsCell();
                    }
                    Push(stack, FluidValue.FromFunction(instruction.OperandA, captures), frame);
                    break;
                case OpCode.NewObject:
                    NewObject(module, instruction, stack, frame);
                    break;
                case OpCode.FieldGet:
                    FieldGet(module, instruction, stack, frame);
                    break;
                case OpCode.FieldSet:
                    FieldSet(module, instruction, stack, frame);
                    break;
                case OpCode.Return:
                    Return(stack, frames, Pop(stack, frame));
                    break;
                case OpCode.ReturnVoid:
                    Return(stack, frames, FluidValue.Null);
                    break;
                case OpCode.MakeArray:
                    MakeArray(instruction, stack, frame);
                    break;
                case OpCode.IndexGet:
                    IndexGet(stack, frame);
                    break;
                case OpCode.IndexSet:
                    IndexSet(stack, frame);
                    break;
                case OpCode.ForCheckLocal:
                    ForCheck(frame, instruction);
                    break;
                case OpCode.ForIncrementLocal:
                    ForIncrement(frame, instruction);
                    break;
                case OpCode.ForCheckGlobal:
                    ForCheckGlobal(globals, frame, instruction);
                    break;
                case OpCode.ForIncrementGlobal:
                    ForIncrementGlobal(globals, frame, instruction);
                    break;
                case OpCode.Halt:
                    return stack.Count == 0 ? FluidValue.Null : Pop(stack, frame);
                    case OpCode.EnterHandler:
                        if (instruction.OperandA < 0 || instruction.OperandA >= frame.Function.Instructions.Count)
                            Fault("FS4007", "Exception handler target is invalid.", instruction.Span, frame.Function.Name);
                        frame.Handlers.Add(new Handler(instruction.OperandA, stack.Count, instruction.OperandB));
                        break;
                    case OpCode.LeaveHandler:
                        if (frame.Handlers.Count == 0)
                            Fault("FS4008", "Exception handler stack underflow.", instruction.Span, frame.Function.Name);
                        frame.Handlers.RemoveAt(frame.Handlers.Count - 1);
                        break;
                    case OpCode.Throw:
                        var thrown = Pop(stack, frame);
                        throw new RuntimeFaultException("FS5001", $"Unhandled value: {thrown}", instruction.Span, frame.Function.Name, thrown);
                    case OpCode.Rethrow:
                        if (frame.PendingFault is { } pending)
                        {
                            frame.PendingFault = null;
                            throw pending;
                        }
                        var rethrown = Pop(stack, frame);
                        throw new RuntimeFaultException("FS5001", $"Unhandled value: {rethrown}", instruction.Span, frame.Function.Name, rethrown);
                    default:
                        Fault("FS4001", $"Unknown opcode {instruction.OpCode}.", instruction.Span, frame.Function.Name);
                        break;
                }
            }
            catch (RuntimeFaultException fault) when (HandleFault(fault, frames, stack))
            {
                // The handler has transferred control to a catch block.
            }
        }

        return FluidValue.Null;
    }

    private static bool HandleFault(RuntimeFaultException fault, List<Frame> frames, List<FluidValue> stack)
    {
        for (var index = frames.Count - 1; index >= 0; index--)
        {
            var frame = frames[index];
            if (frame.Handlers.Count == 0)
            {
                if (stack.Count > frame.StackBase)
                    stack.RemoveRange(frame.StackBase, stack.Count - frame.StackBase);
                frames.RemoveAt(index);
                continue;
            }

            while (frames.Count - 1 > index)
            {
                var abandoned = frames[^1];
                if (stack.Count > abandoned.StackBase)
                    stack.RemoveRange(abandoned.StackBase, stack.Count - abandoned.StackBase);
                frames.RemoveAt(frames.Count - 1);
            }

            var handler = frame.Handlers[^1];
            frame.Handlers.RemoveAt(frame.Handlers.Count - 1);
            var candidate = fault.Value ?? FluidValue.From(fault.Message);
            if (handler.FilterKind != 0 && handler.FilterKind != (int)candidate.Kind + 1)
                continue;
            if (stack.Count > handler.StackDepth)
                stack.RemoveRange(handler.StackDepth, stack.Count - handler.StackDepth);
            frame.Ip = handler.Target;
            frame.PendingFault = fault;
            stack.Add(fault.Value ?? FluidValue.From(fault.Message));
            return true;
        }
        return false;
    }

    private static void Call(
        PCodeModule module,
        Instruction instruction,
        List<FluidValue> stack,
        List<Frame> frames,
        Frame caller)
    {
        if (instruction.OperandA < 0 || instruction.OperandA >= module.Functions.Count)
            Fault("FS2004", "Function index is invalid.", instruction.Span, caller.Function.Name);

        var function = module.Functions[instruction.OperandA];
        if (instruction.OperandB != function.Arity)
            Fault("FS2005", "Call arity does not match the function signature.", instruction.Span, caller.Function.Name);
        if (frames.Count >= MaxCallDepth)
            Fault("FS5005", "The call-depth limit was exceeded.", instruction.Span, caller.Function.Name);

        var arguments = new FluidValue[instruction.OperandB];
        for (var i = arguments.Length - 1; i >= 0; i--)
            arguments[i] = Pop(stack, caller);

        var frame = new Frame(function, function.LocalCount, 0, stack.Count, null);
        for (var index = 0; index < arguments.Length; index++)
            frame.Locals[index].Value = arguments[index];
        frames.Add(frame);
    }

    private static void CallIndirect(
        PCodeModule module,
        Instruction instruction,
        List<FluidValue> stack,
        List<Frame> frames,
        Frame caller)
    {
        if (instruction.OperandB < 0)
            Fault("FS2005", "Call arity is invalid.", instruction.Span, caller.Function.Name);
        var arguments = new FluidValue[instruction.OperandB];
        for (var i = arguments.Length - 1; i >= 0; i--)
            arguments[i] = Pop(stack, caller);
        var target = Pop(stack, caller);
        if (target.Kind != FluidValueKind.Function)
            Fault("FS5007", "The value is not callable.", instruction.Span, caller.Function.Name);
        var functionId = ((FunctionHandle)target.Raw!).FunctionId;
        if (functionId < 0 || functionId >= module.Functions.Count)
            Fault("FS2004", "Function index is invalid.", instruction.Span, caller.Function.Name);
        var function = module.Functions[functionId];
        if (instruction.OperandB != function.Arity)
            Fault("FS2005", "Call arity does not match the function signature.", instruction.Span, caller.Function.Name);
        var handle = (FunctionHandle)target.Raw!;
        if (handle.Captures is null || handle.Captures.Count != function.CaptureNames.Count)
            Fault("FS5009", "Closure capture count does not match the function signature.", instruction.Span, caller.Function.Name);
        if (frames.Count >= MaxCallDepth)
            Fault("FS5005", "The call-depth limit was exceeded.", instruction.Span, caller.Function.Name);

        var frame = new Frame(function, function.LocalCount, 0, stack.Count, handle.Captures);
        for (var index = 0; index < arguments.Length; index++)
            frame.Locals[index].Value = arguments[index];
        frames.Add(frame);
    }

    private static void CallNative(Instruction instruction, List<FluidValue> stack, Frame frame, Action<string>? output)
    {
        var arguments = new FluidValue[instruction.OperandB];
        for (var i = arguments.Length - 1; i >= 0; i--)
            arguments[i] = Pop(stack, frame);

        if (instruction.OperandA != PrintBuiltin)
            Fault("FS2006", "The native builtin is not registered.", instruction.Span, frame.Function.Name);
        if (arguments.Length != 1)
            Fault("FS2007", "print expects exactly one argument.", instruction.Span, frame.Function.Name);

        output?.Invoke(arguments[0].ToString());
        Push(stack, FluidValue.Null, frame);
    }

    private static void Return(List<FluidValue> stack, List<Frame> frames, FluidValue value)
    {
        var frame = frames[^1];
        if (stack.Count > frame.StackBase)
            stack.RemoveRange(frame.StackBase, stack.Count - frame.StackBase);
        frames.RemoveAt(frames.Count - 1);
        if (frames.Count > 0)
            stack.Add(value);
    }

    private static FluidValue Add(FluidValue right, FluidValue left, Frame frame)
    {
        if (left.Kind == FluidValueKind.String && right.Kind == FluidValueKind.String)
            return FluidValue.From(left.AsString() + right.AsString());
        return Numeric(right, left, (a, b) => a + b, frame);
    }

    private static FluidValue Numeric(FluidValue right, FluidValue left, Func<decimal, decimal, decimal> operation, Frame frame)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
            Fault("FS5002", "Numeric operands are required.", frame.CurrentSpan, frame.Function.Name);

        try
        {
            var result = operation(left.AsDecimal(), right.AsDecimal());
            return left.Kind == FluidValueKind.Int && right.Kind == FluidValueKind.Int && result == decimal.Truncate(result)
                ? FluidValue.From(decimal.ToInt64(result))
                : FluidValue.From(result);
        }
        catch (OverflowException)
        {
            Fault("FS5008", "Numeric overflow.", frame.CurrentSpan, frame.Function.Name);
            return FluidValue.Null;
        }
    }

    private static FluidValue Divide(FluidValue right, FluidValue left, Frame frame)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
            Fault("FS5002", "Numeric operands are required.", frame.CurrentSpan, frame.Function.Name);
        if (right.AsDecimal() == 0)
            Fault("FS5003", "Division by zero.", frame.CurrentSpan, frame.Function.Name);

        var result = left.AsDecimal() / right.AsDecimal();
        return left.Kind == FluidValueKind.Int && right.Kind == FluidValueKind.Int && result == decimal.Truncate(result)
            ? FluidValue.From(decimal.ToInt64(result))
            : FluidValue.From(result);
    }

    private static FluidValue Modulo(FluidValue right, FluidValue left, Frame frame)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
            Fault("FS5002", "Numeric operands are required.", frame.CurrentSpan, frame.Function.Name);
        if (right.AsDecimal() == 0)
            Fault("FS5003", "Modulo by zero.", frame.CurrentSpan, frame.Function.Name);

        var result = left.AsDecimal() % right.AsDecimal();
        return left.Kind == FluidValueKind.Int && right.Kind == FluidValueKind.Int
            ? FluidValue.From(decimal.ToInt64(result))
            : FluidValue.From(result);
    }

    private static FluidValue Negate(FluidValue value, Frame frame)
    {
        if (!IsNumeric(value))
            Fault("FS5002", "Numeric operands are required.", frame.CurrentSpan, frame.Function.Name);
        return value.Kind == FluidValueKind.Int ? FluidValue.From(-value.AsInt()) : FluidValue.From(-value.AsDecimal());
    }

    private static bool RequireBool(FluidValue value, Frame frame)
    {
        if (value.Kind != FluidValueKind.Bool)
            Fault("FS5002", "A boolean value is required.", frame.CurrentSpan, frame.Function.Name);
        return value.AsBool();
    }

    private static FluidValue Comparison(FluidValue right, FluidValue left, Func<decimal, decimal, bool> operation, Frame frame)
    {
        if (IsNumeric(left) && IsNumeric(right))
            return FluidValue.From(operation(left.AsDecimal(), right.AsDecimal()));
        if (left.Kind == FluidValueKind.String && right.Kind == FluidValueKind.String)
            return FluidValue.From(operation(
                string.CompareOrdinal(left.AsString(), right.AsString()), 0));
        Fault("FS5002", "Comparable operands are required.", frame.CurrentSpan, frame.Function.Name);
        return FluidValue.Null;
    }

    private static void ForCheck(Frame frame, Instruction instruction)
    {
        var counter = GetLocal(frame, instruction.OperandA);
        var end = GetLocal(frame, instruction.OperandB);
        var step = GetLocal(frame, instruction.OperandC);
        if (!IsNumeric(counter) || !IsNumeric(end) || !IsNumeric(step))
            Fault("FS3003", "for bounds and step must be numeric.", instruction.Span, frame.Function.Name);
        if (step.AsDecimal() == 0)
            Fault("FS3004", "for step cannot be zero.", instruction.Span, frame.Function.Name);

        var continueLoop = step.AsDecimal() > 0
            ? counter.AsDecimal() <= end.AsDecimal()
            : counter.AsDecimal() >= end.AsDecimal();
        if (!continueLoop)
            frame.Ip = instruction.OperandD;
    }

    private static void MakeArray(Instruction instruction, List<FluidValue> stack, Frame frame)
    {
        if (instruction.OperandA < 0)
            Fault("FS4005", "Array element count is invalid.", instruction.Span, frame.Function.Name);
        var elements = new FluidValue[instruction.OperandA];
        for (var index = elements.Length - 1; index >= 0; index--)
            elements[index] = Pop(stack, frame);
        Push(stack, FluidValue.FromArray(elements.ToList()), frame);
    }

    private static void IndexGet(List<FluidValue> stack, Frame frame)
    {
        var index = Pop(stack, frame);
        var target = Pop(stack, frame);
        if (target.Kind != FluidValueKind.Array)
            Fault("FS5010", "Indexing requires an array.", frame.CurrentSpan, frame.Function.Name);
        if (index.Kind != FluidValueKind.Int)
            Fault("FS5011", "Array indexes must be integers.", frame.CurrentSpan, frame.Function.Name);
        var items = target.AsArray();
        var position = index.AsInt();
        if (position < 0 || position >= items.Count)
            Fault("FS5012", "Array index is outside the valid range.", frame.CurrentSpan, frame.Function.Name);
        Push(stack, items[(int)position], frame);
    }

    private static void IndexSet(List<FluidValue> stack, Frame frame)
    {
        var value = Pop(stack, frame);
        var index = Pop(stack, frame);
        var target = Pop(stack, frame);
        if (target.Kind != FluidValueKind.Array)
            Fault("FS5010", "Indexing requires an array.", frame.CurrentSpan, frame.Function.Name);
        if (index.Kind != FluidValueKind.Int)
            Fault("FS5011", "Array indexes must be integers.", frame.CurrentSpan, frame.Function.Name);
        var items = target.AsArray();
        var position = index.AsInt();
        if (position < 0 || position >= items.Count)
            Fault("FS5012", "Array index is outside the valid range.", frame.CurrentSpan, frame.Function.Name);
        if (items is not IList<FluidValue> mutable)
        {
            Fault("FS5013", "The array is not mutable.", frame.CurrentSpan, frame.Function.Name);
            return;
        }
        mutable[(int)position] = value;
    }

    private static void NewObject(PCodeModule module, Instruction instruction, List<FluidValue> stack, Frame frame)
    {
        if (instruction.OperandA < 0 || instruction.OperandA >= module.Types.Count)
            Fault("FS2204", "Object type index is invalid.", instruction.Span, frame.Function.Name);
        var type = module.Types[instruction.OperandA];
        if (instruction.OperandB != type.FieldNames.Count)
            Fault("FS2205", "Object field count does not match its type.", instruction.Span, frame.Function.Name);
        var fields = new Dictionary<string, FluidValue>(StringComparer.Ordinal);
        for (var index = type.FieldNames.Count - 1; index >= 0; index--)
            fields[type.FieldNames[index]] = Pop(stack, frame);
        Push(stack, FluidValue.FromObject(new FluidObject(type.Name, fields, type.ConstantFields)), frame);
    }

    private static void FieldGet(PCodeModule module, Instruction instruction, List<FluidValue> stack, Frame frame)
    {
        var target = Pop(stack, frame);
        if (target.Kind != FluidValueKind.Object)
            Fault("FS5020", "Member access requires an object.", instruction.Span, frame.Function.Name);
        var value = target.AsObject();
        if (instruction.OperandA < 0 || !module.Types.Any(type => type.Name == value.TypeName))
            Fault("FS5021", "Object member metadata is invalid.", instruction.Span, frame.Function.Name);
        var type = module.Types.First(type => type.Name == value.TypeName);
        if (instruction.OperandA >= type.FieldNames.Count)
            Fault("FS5022", "Object member index is invalid.", instruction.Span, frame.Function.Name);
        var name = type.FieldNames[instruction.OperandA];
        if (!value.Fields.TryGetValue(name, out var field))
            Fault("FS5023", $"Object member '{name}' is not initialized.", instruction.Span, frame.Function.Name);
        Push(stack, field, frame);
    }

    private static void FieldSet(PCodeModule module, Instruction instruction, List<FluidValue> stack, Frame frame)
    {
        var value = Pop(stack, frame);
        var target = Pop(stack, frame);
        if (target.Kind != FluidValueKind.Object)
            Fault("FS5020", "Member access requires an object.", instruction.Span, frame.Function.Name);
        var obj = target.AsObject();
        var type = module.Types.FirstOrDefault(type => type.Name == obj.TypeName);
        if (type is null || instruction.OperandA < 0 || instruction.OperandA >= type.FieldNames.Count)
            Fault("FS5022", "Object member index is invalid.", instruction.Span, frame.Function.Name);
        var name = type!.FieldNames[instruction.OperandA];
        if (obj.ConstantFields.Contains(name))
            Fault("FS5024", $"Cannot assign to constant member '{name}'.", instruction.Span, frame.Function.Name);
        obj.Fields[name] = value;
    }

    private static void Dup2(List<FluidValue> stack, Frame frame)
    {
        if (stack.Count - frame.StackBase < 2)
            Fault("FS4004", "Operand stack underflow.", frame.CurrentSpan, frame.Function.Name);
        var first = stack[^2];
        var second = stack[^1];
        Push(stack, first, frame);
        Push(stack, second, frame);
    }

    private static void Swap(List<FluidValue> stack, Frame frame)
    {
        if (stack.Count - frame.StackBase < 2)
            Fault("FS4004", "Operand stack underflow.", frame.CurrentSpan, frame.Function.Name);
        (stack[^1], stack[^2]) = (stack[^2], stack[^1]);
    }

    private static void ForIncrement(Frame frame, Instruction instruction)
    {
        var counter = GetLocal(frame, instruction.OperandA);
        var step = GetLocal(frame, instruction.OperandB);
        SetLocal(frame, instruction.OperandA, Numeric(step, counter, (left, right) => left + right, frame));
    }

    private static void ForCheckGlobal(FluidValue[] globals, Frame frame, Instruction instruction)
    {
        var counter = GetGlobal(globals, instruction.OperandA);
        var end = GetGlobal(globals, instruction.OperandB);
        var step = GetGlobal(globals, instruction.OperandC);
        if (!IsNumeric(counter) || !IsNumeric(end) || !IsNumeric(step))
            Fault("FS3003", "for bounds and step must be numeric.", instruction.Span, frame.Function.Name);
        if (step.AsDecimal() == 0)
            Fault("FS3004", "for step cannot be zero.", instruction.Span, frame.Function.Name);

        var continueLoop = step.AsDecimal() > 0
            ? counter.AsDecimal() <= end.AsDecimal()
            : counter.AsDecimal() >= end.AsDecimal();
        if (!continueLoop)
            frame.Ip = instruction.OperandD;
    }

    private static void ForIncrementGlobal(FluidValue[] globals, Frame frame, Instruction instruction)
    {
        var counter = GetGlobal(globals, instruction.OperandA);
        var step = GetGlobal(globals, instruction.OperandB);
        globals[instruction.OperandA] = Numeric(step, counter, (left, right) => left + right, frame);
    }

    private static bool IsNumeric(FluidValue value) =>
        value.Kind is FluidValueKind.Int or FluidValueKind.Decimal;

    private static FluidValue GetConstant(PCodeModule module, int index)
    {
        if (index < 0 || index >= module.Constants.Count)
            throw new InvalidOperationException("P-code constant index is invalid.");
        return module.Constants[index];
    }

    private static FluidValue GetLocal(Frame frame, int slot)
    {
        if (slot < 0 || slot >= frame.Locals.Length)
            Fault("FS4003", "Local slot is invalid.", frame.CurrentSpan, frame.Function.Name);
        return frame.Locals[slot].Value;
    }

    private static FluidCell GetLocalCell(Frame frame, int slot)
    {
        if (slot < 0 || slot >= frame.Locals.Length)
            Fault("FS4003", "Local slot is invalid.", frame.CurrentSpan, frame.Function.Name);
        return frame.Locals[slot];
    }

    private static void SetLocal(Frame frame, int slot, FluidValue value)
    {
        if (slot < 0 || slot >= frame.Locals.Length)
            Fault("FS4003", "Local slot is invalid.", frame.CurrentSpan, frame.Function.Name);
        frame.Locals[slot].Value = value;
    }

    private static FluidCell GetCapture(Frame frame, int slot)
    {
        if (slot < 0 || slot >= frame.Captures.Length)
            Fault("FS4003", "Capture slot is invalid.", frame.CurrentSpan, frame.Function.Name);
        return frame.Captures[slot];
    }

    private static FluidValue GetGlobal(FluidValue[] globals, int slot)
    {
        if (slot < 0 || slot >= globals.Length)
            throw new InvalidOperationException("Global slot is invalid.");
        return globals[slot];
    }

    private static void SetGlobal(FluidValue[] globals, int slot, FluidValue value)
    {
        if (slot < 0 || slot >= globals.Length)
            throw new InvalidOperationException("Global slot is invalid.");
        globals[slot] = value;
    }

    private static FluidValue Pop(List<FluidValue> stack, Frame frame)
    {
        if (stack.Count <= frame.StackBase)
            Fault("FS4004", "Operand stack underflow.", frame.CurrentSpan, frame.Function.Name);
        var index = stack.Count - 1;
        var value = stack[index];
        stack.RemoveAt(index);
        return value;
    }

    private static FluidValue Peek(List<FluidValue> stack, Frame frame)
    {
        if (stack.Count <= frame.StackBase)
            Fault("FS4004", "Operand stack underflow.", frame.CurrentSpan, frame.Function.Name);
        return stack[^1];
    }

    private static void Push(List<FluidValue> stack, FluidValue value, Frame frame)
    {
        if (stack.Count >= MaxStack)
            Fault("FS5006", "The operand-stack limit was exceeded.", frame.CurrentSpan, frame.Function.Name);
        stack.Add(value);
    }

    private static void Fault(string code, string message, SourceSpan span, string functionName) =>
        throw new RuntimeFaultException(code, message, span, functionName);

    private sealed class Frame(PCodeFunction function, int localCount, int ip, int stackBase, IReadOnlyList<FluidCell>? captures)
    {
        public PCodeFunction Function { get; } = function;
        public FluidCell[] Locals { get; } = Enumerable.Range(0, localCount).Select(_ => new FluidCell(FluidValue.Null)).ToArray();
        public FluidCell[] Captures { get; } = captures?.ToArray() ?? Array.Empty<FluidCell>();
        public int Ip { get; set; } = ip;
        public int StackBase { get; } = stackBase;
        public SourceSpan CurrentSpan { get; set; } = SourceSpan.None;
        public List<Handler> Handlers { get; } = new();
        public RuntimeFaultException? PendingFault { get; set; }
    }

    private readonly record struct Handler(int Target, int StackDepth, int FilterKind);
}

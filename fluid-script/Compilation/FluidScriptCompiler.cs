using FluidScript.Parsing;
using FluidScript.Runtime;

namespace FluidScript.Compilation;

public sealed class CompilationResult
{
    internal CompilationResult(PCodeModule? module, IReadOnlyList<Diagnostic> diagnostics, FluidScriptHost? host = null)
    {
        Module = module;
        Diagnostics = diagnostics;
        Host = host;
    }

    public PCodeModule? Module { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>The host registry used to bind host function names, if any.</summary>
    public FluidScriptHost? Host { get; }
    public bool Success => Module is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public FluidValue Execute(Action<string>? output = null, int instructionLimit = 1_000_000)
    {
        if (!Success)
            throw new InvalidOperationException("The source did not compile: " + string.Join("; ", Diagnostics));
        return new VirtualMachine().Run(Module!, new FluidScriptExecutionContext(Host, output: output), instructionLimit);
    }

    public FluidValue Execute(FluidScriptExecutionContext context, int instructionLimit = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Success)
            throw new InvalidOperationException("The source did not compile: " + string.Join("; ", Diagnostics));
        return new VirtualMachine().Run(Module!, context, instructionLimit);
    }
}

public static class FluidScriptCompiler
{
    public static CompilationResult Compile(string source, IFluidModuleResolver? resolver = null)
        => CompileCore(source, resolver, null, new ModuleCompilationSession());

    public static CompilationResult Compile(string source, FluidScriptHost host, IFluidModuleResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        return CompileCore(source, resolver, host, new ModuleCompilationSession());
    }

    internal static CompilationResult CompileCore(string source, IFluidModuleResolver? resolver, ModuleCompilationSession session)
        => CompileCore(source, resolver, null, session);

    internal static CompilationResult CompileCore(string source, IFluidModuleResolver? resolver, FluidScriptHost? host, ModuleCompilationSession session)
    {
        var parse = FluidScriptFrontEnd.Parse(source);
        if (!parse.Success || parse.Program is null)
            return new CompilationResult(null, parse.Diagnostics, host);

        var builder = new SyntaxTreeBuilder();
        var script = builder.Build(parse.Program);
        if (builder.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new CompilationResult(null, builder.Diagnostics, host);

        var engine = new CompilerEngine(resolver, session, host);
        var module = engine.Compile(script);
        module.SourceHash = PCodeSerializer.ComputeSourceHash(source);
        var diagnostics = parse.Diagnostics.Concat(builder.Diagnostics).Concat(engine.Diagnostics).ToArray();
        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? new CompilationResult(null, diagnostics, host)
            : new CompilationResult(module, diagnostics, host);
    }
}

internal sealed class CompilerEngine
{
    private readonly IFluidModuleResolver? resolver;
    private readonly ModuleCompilationSession session;
    private readonly FluidScriptHost? host;
    private readonly List<Diagnostic> diagnostics = new();
    private readonly Dictionary<string, int> globalSlots = new(StringComparer.Ordinal);
    private readonly HashSet<string> globalConstants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> globalTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> functionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> functionReturnTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionNode> functionDeclarations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> methodIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TypeNode Type, FunctionNode Method)> methodDeclarations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> typeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeNode> typeDeclarations = new(StringComparer.Ordinal);
    private readonly List<PCodeType> types = new();
    private readonly Dictionary<ForNode, (int End, int Step)> globalForSlots = new();
    private readonly List<PCodeFunction> functions = new();
    private readonly List<FluidValue> constants = new();
    private readonly List<string> globalNames = new();

    public CompilerEngine(IFluidModuleResolver? resolver = null, ModuleCompilationSession? session = null, FluidScriptHost? host = null)
    {
        this.resolver = resolver;
        this.session = session ?? new ModuleCompilationSession();
        this.host = host;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

    public PCodeModule Compile(ScriptNode script)
    {
        var topLevelFunctions = script.Statements.OfType<FunctionNode>().ToArray();
        foreach (var type in script.Statements.OfType<TypeNode>())
        {
            if (FluidScriptHost.IsJsonBuiltinName(type.Name))
            {
                Error("FS2200", $"The builtin name '{type.Name}' cannot be used as a type.", type.Span);
                continue;
            }
            if (typeIds.ContainsKey(type.Name))
            {
                Error("FS2200", $"The type '{type.Name}' is declared more than once.", type.Span);
                continue;
            }
            var fieldNames = new List<string>();
            var constants = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in type.Fields)
            {
                if (!fieldNames.Contains(field.Name, StringComparer.Ordinal))
                    fieldNames.Add(field.Name);
                else
                    Error("FS2201", $"The member '{field.Name}' is declared more than once.", field.Span);
                if (field.IsConstant)
                    constants.Add(field.Name);
                ValidateType(field.TypeName, field.Span);
            }
            typeIds[type.Name] = types.Count;
            typeDeclarations[type.Name] = type;
            types.Add(new PCodeType(type.Name, fieldNames, constants));
        }
        functionIds["__main"] = 0;
        foreach (var function in topLevelFunctions)
        {
            if (FluidScriptHost.IsJsonBuiltinName(function.Name))
            {
                Error("FS2007", $"The builtin name '{function.Name}' cannot be used as a function.", function.Span);
                continue;
            }
            if (functionIds.ContainsKey(function.Name))
            {
                Error("FS2007", $"The function '{function.Name}' is declared more than once.", function.Span);
                continue;
            }
            functionIds[function.Name] = functionIds.Count;
            functionReturnTypes[function.Name] = ValidateType(function.ReturnType, function.Span);
            functionDeclarations[function.Name] = function;
        }
        foreach (var type in typeDeclarations.Values)
            foreach (var method in type.Methods)
            {
                var key = MethodKey(type.Name, method.Name);
                if (methodIds.ContainsKey(key))
                {
                    Error("FS2206", $"The method '{method.Name}' is declared more than once on '{type.Name}'.", method.Span);
                    continue;
                }
                methodIds[key] = functionIds.Count;
                functionIds[key] = functionIds.Count;
                methodDeclarations[key] = (type, method);
                functionReturnTypes[key] = ValidateType(method.ReturnType, method.Span);
            }

        foreach (var statement in script.Statements.Where(statement => statement is not FunctionNode))
            DeclareGlobals(statement);

        // Reserve stable IDs for declared functions before compiling the entry
        // function. Synthetic lambda functions can then be appended safely.
        functions.Add(new PCodeFunction("__main", 0, 0, new[] { new Instruction(OpCode.Halt) }));
        foreach (var function in functionIds.OrderBy(pair => pair.Value).Where(pair => pair.Key != "__main"))
        {
            var arity = functionDeclarations.TryGetValue(function.Key, out var declaration)
                ? declaration.Parameters.Count
                : methodDeclarations.TryGetValue(function.Key, out var method) ? method.Method.Parameters.Count + 1 : 0;
            functions.Add(new PCodeFunction(function.Key, arity, 0, new[] { new Instruction(OpCode.ReturnVoid) }));
        }

        var mainStatements = script.Statements.Where(statement => statement is not FunctionNode and not TypeNode).ToArray();
        var mainContext = new FunctionContext("__main", 0, isMain: true, globalSlots, globalConstants, globalTypes);
        DeclareCatchLocals(mainStatements, mainContext);
        var mainBuilder = new CodeBuilder();
        CompileStatements(mainStatements, mainContext, mainBuilder);
        mainBuilder.Emit(OpCode.Halt, span: script.Span);
        functions[0] = new PCodeFunction("__main", 0, mainContext.LocalCount, mainBuilder.Instructions, mainContext.LocalNames);

        foreach (var function in topLevelFunctions)
            if (functionIds.TryGetValue(function.Name, out var functionId))
                functions[functionId] = CompileFunction(function);
        foreach (var method in methodDeclarations)
            functions[methodIds[method.Key]] = CompileMethod(method.Value.Type, method.Value.Method);

        var module = new PCodeModule(constants, functions, 0)
        {
            GlobalCount = globalSlots.Count,
            GlobalNames = globalNames.ToArray(),
            Types = types.ToArray()
        };
        diagnostics.AddRange(PCodeVerifier.Verify(module));
        return module;
    }

    private PCodeFunction CompileFunction(FunctionNode function)
    {
        var context = new FunctionContext(function.Name, function.Parameters.Count, false, globalSlots, globalConstants, globalTypes);
        foreach (var parameter in function.Parameters)
        {
            var parameterType = ValidateType(parameter.TypeName, parameter.Span);
            if (parameter.DefaultValue is not null)
                CheckAssignable(parameterType, InferType(parameter.DefaultValue, context), parameter.Span);
            context.AddLocal(parameter.Name, parameter.Span, isConstant: false, parameterType);
        }
        var returnType = ValidateType(function.ReturnType, function.Span);
        context.ReturnType = returnType;
        var defaultSeen = false;
        foreach (var parameter in function.Parameters)
        {
            if (parameter.DefaultValue is not null)
                defaultSeen = true;
            else if (defaultSeen)
                Error("FS2102", "A required parameter cannot follow a default parameter.", parameter.Span);
        }
        DeclareLocals(function.Body.Statements, context);

        var builder = new CodeBuilder();
        CompileStatements(function.Body.Statements, context, builder);
        if (returnType != "any" && returnType != "null" && !ContainsReturn(function.Body))
            Error("FS2103", $"Function '{function.Name}' does not return on every path.", function.Span);
        if (builder.Instructions.Count == 0 || builder.Instructions[^1].OpCode is not (OpCode.Return or OpCode.ReturnVoid))
            builder.Emit(OpCode.ReturnVoid, span: function.Span);
        return new PCodeFunction(function.Name, function.Parameters.Count, context.LocalCount, builder.Instructions, context.LocalNames);
    }

    private PCodeFunction CompileMethod(TypeNode type, FunctionNode method)
    {
        var context = new FunctionContext($"{type.Name}.{method.Name}", method.Parameters.Count + 1, false, globalSlots, globalConstants, globalTypes, type.Name);
        context.AddLocal("self", method.Span, false, type.Name);
        foreach (var parameter in method.Parameters)
        {
            var parameterType = ValidateType(parameter.TypeName, parameter.Span);
            context.AddLocal(parameter.Name, parameter.Span, false, parameterType);
        }
        context.ReturnType = ValidateType(method.ReturnType, method.Span);
        DeclareLocals(method.Body.Statements, context);
        var builder = new CodeBuilder();
        CompileStatements(method.Body.Statements, context, builder);
        if (builder.Instructions.Count == 0 || builder.Instructions[^1].OpCode is not (OpCode.Return or OpCode.ReturnVoid))
            builder.Emit(OpCode.ReturnVoid, span: method.Span);
        return new PCodeFunction($"{type.Name}.{method.Name}", method.Parameters.Count + 1, context.LocalCount, builder.Instructions, context.LocalNames);
    }

    private void DeclareGlobals(StatementNode statement)
    {
        switch (statement)
        {
            case VariableNode variable:
                AddGlobal(variable.Name, variable.IsConstant, variable.Span);
                globalTypes[variable.Name] = ValidateType(variable.TypeName, variable.Span);
                break;
            case TypeNode:
                break;
            case ForNode loop:
                AddGlobal(loop.Name, false, loop.Span);
                DeclareGlobals(loop.Body.Statements);
                var end = AddGlobal($"$for_end_{loop.Span.Line}_{loop.Span.Column}", false, loop.Span);
                var step = AddGlobal($"$for_step_{loop.Span.Line}_{loop.Span.Column}", false, loop.Span);
                globalForSlots[loop] = (end, step);
                break;
            case SwitchNode switchNode:
                foreach (var switchCase in switchNode.Cases)
                    DeclareGlobals(switchCase.Body.Statements);
                if (switchNode.Otherwise is not null)
                    DeclareGlobals(switchNode.Otherwise.Statements);
                break;
            case IfNode conditional:
                DeclareGlobals(conditional.Then.Statements);
                if (conditional.Else is not null)
                    DeclareGlobals(conditional.Else.Statements);
                break;
            case WhileNode loop:
                DeclareGlobals(loop.Body.Statements);
                break;
            case TryNode tryNode:
                DeclareGlobals(tryNode.Body.Statements);
                if (tryNode.Catch is { } catchNode)
                    DeclareGlobals(catchNode.Body.Statements);
                if (tryNode.Finally is { } finallyNode)
                    DeclareGlobals(finallyNode.Statements);
                break;
        }
    }

    private void DeclareGlobals(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
            DeclareGlobals(statement);
    }

    private void DeclareLocals(StatementNode statement, FunctionContext context)
    {
        switch (statement)
        {
            case VariableNode variable:
                context.AddLocal(variable.Name, variable.Span, variable.IsConstant, ValidateType(variable.TypeName, variable.Span));
                break;
            case TypeNode:
                break;
            case ForNode loop:
                context.AddLocal(loop.Name, loop.Span, false);
                context.AddHiddenForSlots(loop);
                DeclareLocals(loop.Body.Statements, context);
                break;
            case SwitchNode switchNode:
                foreach (var switchCase in switchNode.Cases)
                    DeclareLocals(switchCase.Body.Statements, context);
                if (switchNode.Otherwise is not null)
                    DeclareLocals(switchNode.Otherwise.Statements, context);
                break;
            case IfNode conditional:
                DeclareLocals(conditional.Then.Statements, context);
                if (conditional.Else is not null)
                    DeclareLocals(conditional.Else.Statements, context);
                break;
            case WhileNode loop:
                DeclareLocals(loop.Body.Statements, context);
                break;
            case TryNode tryNode:
                DeclareLocals(tryNode.Body.Statements, context);
                if (tryNode.Catch is { } catchNode)
                {
                    if (catchNode.Name is not null)
                        context.AddLocal(catchNode.Name, catchNode.Span, false, ValidateType(catchNode.TypeName, catchNode.Span));
                    DeclareLocals(catchNode.Body.Statements, context);
                }
                if (tryNode.Finally is { } finallyNode)
                    DeclareLocals(finallyNode.Statements, context);
                break;
        }
    }

    private void DeclareLocals(IEnumerable<StatementNode> statements, FunctionContext context)
    {
        foreach (var statement in statements)
            DeclareLocals(statement, context);
    }

    private void DeclareCatchLocals(IEnumerable<StatementNode> statements, FunctionContext context)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case TryNode tryNode:
                    if (tryNode.Catch?.Name is { } name)
                        context.AddLocal(name, tryNode.Catch.Span, false, ValidateType(tryNode.Catch.TypeName, tryNode.Catch.Span));
                    DeclareCatchLocals(tryNode.Body.Statements, context);
                    if (tryNode.Catch is { } catchNode)
                        DeclareCatchLocals(catchNode.Body.Statements, context);
                    if (tryNode.Finally is { } finallyNode)
                        DeclareCatchLocals(finallyNode.Statements, context);
                    break;
                case IfNode conditional:
                    DeclareCatchLocals(conditional.Then.Statements, context);
                    if (conditional.Else is { } elseBlock)
                        DeclareCatchLocals(elseBlock.Statements, context);
                    break;
                case WhileNode loop:
                    DeclareCatchLocals(loop.Body.Statements, context);
                    break;
                case ForNode loop:
                    DeclareCatchLocals(loop.Body.Statements, context);
                    break;
                case SwitchNode switchNode:
                    foreach (var switchCase in switchNode.Cases)
                        DeclareCatchLocals(switchCase.Body.Statements, context);
                    if (switchNode.Otherwise is { } otherwise)
                        DeclareCatchLocals(otherwise.Statements, context);
                    break;
            }
        }
    }

    private void CompileStatements(IEnumerable<StatementNode> statements, FunctionContext context, CodeBuilder builder)
    {
        foreach (var statement in statements)
            CompileStatement(statement, context, builder);
    }

    private void CompileStatement(StatementNode statement, FunctionContext context, CodeBuilder builder)
    {
        switch (statement)
        {
            case VariableNode variable:
                if (variable.Initializer is null)
                {
                    // Main-scope globals are already initialized to null by the VM.
                    // Leaving the slot untouched also permits an embedding host to
                    // provide an input value through FluidScriptExecutionContext.
                    if (!context.IsMain)
                        builder.Emit(OpCode.Null, span: variable.Span);
                    else
                        return;
                }
                else
                {
                    var inferredType = InferType(variable.Initializer, context);
                    if (variable.TypeName is null && typeIds.ContainsKey(inferredType))
                    {
                        if (context.IsMain)
                            globalTypes[variable.Name] = inferredType;
                        else
                            context.SetType(variable.Name, inferredType);
                    }
                    CheckAssignable(context.ResolveType(variable.Name), inferredType, variable.Span);
                    CompileExpression(variable.Initializer, context, builder);
                }
                Store(context, builder, variable.Name, variable.Span);
                break;
            case AssignmentNode assignment:
                CompileAssignment(assignment, context, builder);
                break;
            case ExpressionStatementNode expression:
                CompileExpression(expression.Expression, context, builder);
                builder.Emit(OpCode.Pop, span: expression.Span);
                break;
            case IfNode conditional:
                CompileIf(conditional, context, builder);
                break;
            case WhileNode loop:
                CompileWhile(loop, context, builder);
                break;
            case ForNode loop:
                CompileFor(loop, context, builder);
                break;
            case SwitchNode switchNode:
                CompileSwitch(switchNode, context, builder);
                break;
            case ReturnNode returnStatement:
                if (context.IsMain)
                {
                    Error("FS2104", "return is only valid inside a function.", returnStatement.Span);
                    return;
                }
                if (returnStatement.Value is null)
                {
                    if (context.ReturnType is not null && context.ReturnType != "any" && context.ReturnType != "null")
                        Error("FS2101", $"Function '{context.Name}' must return a value of type {context.ReturnType}.", returnStatement.Span);
                    builder.Emit(OpCode.ReturnVoid, span: returnStatement.Span);
                }
                else
                {
                    CheckAssignable(context.ReturnType, InferType(returnStatement.Value, context), returnStatement.Span);
                    CompileExpression(returnStatement.Value, context, builder);
                    builder.Emit(OpCode.Return, span: returnStatement.Span);
                }
                break;
            case BreakNode breakStatement:
                EmitLoopControl(builder, context, breakStatement.Span, isContinue: false);
                break;
            case ContinueNode continueStatement:
                EmitLoopControl(builder, context, continueStatement.Span, isContinue: true);
                break;
            case ThrowNode throwStatement:
                CompileExpression(throwStatement.Value, context, builder);
                builder.Emit(OpCode.Throw, span: throwStatement.Span);
                break;
            case TryNode tryNode:
                CompileTry(tryNode, context, builder);
                break;
            case TypeNode:
                break;
            case ImportNode import:
                CompileImport(import);
                break;
            case FunctionNode nested:
                Error("FS3001", $"Nested function '{nested.Name}' is not implemented yet.", nested.Span);
                break;
            default:
                Error("FS3001", "This statement is not implemented yet.", statement.Span);
                break;
        }
    }

    private void CompileAssignment(AssignmentNode assignment, FunctionContext context, CodeBuilder builder)
    {
        if (assignment.Target is not NameNode name)
        {
            if (assignment.Target is IndexNode index)
            {
                CompileIndexAssignment(index, assignment.Operator, assignment.Value, context, builder, assignment.Span);
                return;
            }
            if (assignment.Target is MemberNode member)
            {
                CompileMemberAssignment(member, assignment.Operator, assignment.Value, context, builder, assignment.Span);
                return;
            }
            Error("FS2009", "The assignment target is not writable.", assignment.Span);
            return;
        }

        if (context.IsConstant(name.Name) || globalConstants.Contains(name.Name) ||
            (context.OwnerType is not null && TryGetField(context.OwnerType, name.Name, out _, out _, out var ownerField) && ownerField.IsConstant))
        {
            Error("FS2008", $"Cannot assign to constant '{name.Name}'.", assignment.Span);
            return;
        }

        if (assignment.Operator == "=")
        {
            CheckAssignable(context.ResolveType(name.Name), InferType(assignment.Value, context), assignment.Span);
            CompileExpression(assignment.Value, context, builder);
        }
        else
        {
            Load(context, builder, name.Name, assignment.Span);
            CheckAssignable(context.ResolveType(name.Name), InferType(assignment.Value, context), assignment.Span);
            CompileExpression(assignment.Value, context, builder);
            builder.Emit(BinaryOpcode(assignment.Operator[..^1]), span: assignment.Span);
        }
        Store(context, builder, name.Name, assignment.Span);
    }

    private void CompileIndexAssignment(IndexNode target, string operatorText, ExpressionNode value, FunctionContext context, CodeBuilder builder, SourceSpan span)
    {
        // Dup2 preserves the receiver and index while IndexGet obtains the old value.
        CompileExpression(target.Target, context, builder);
        CompileExpression(target.Index, context, builder);
        if (operatorText == "=")
            CompileExpression(value, context, builder);
        else
        {
            builder.Emit(OpCode.Dup2, span: target.Span);
            builder.Emit(OpCode.IndexGet, span: target.Span);
            CompileExpression(value, context, builder);
            builder.Emit(BinaryOpcode(operatorText[..^1]), span: span);
        }
        builder.Emit(OpCode.IndexSet, span: span);
    }

    private void CompileMemberAssignment(MemberNode target, string operatorText, ExpressionNode value, FunctionContext context, CodeBuilder builder, SourceSpan span)
    {
        var typeName = InferType(target.Target, context);
        if (!TryGetField(typeName, target.Name, out var type, out var fieldIndex, out var field))
        {
            Error("FS2202", $"Type '{typeName}' has no member '{target.Name}'.", target.Span);
            return;
        }
        if (field.IsConstant)
        {
            Error("FS2203", $"Cannot assign to constant member '{target.Name}'.", target.Span);
            return;
        }
        CompileExpression(target.Target, context, builder);
        if (operatorText == "=")
            CompileExpression(value, context, builder);
        else
        {
            builder.Emit(OpCode.Dup, span: target.Span);
            builder.Emit(OpCode.FieldGet, fieldIndex, span: target.Span);
            CompileExpression(value, context, builder);
            builder.Emit(BinaryOpcode(operatorText[..^1]), span: span);
        }
        builder.Emit(OpCode.FieldSet, fieldIndex, span: span);
    }

    private void CompileIf(IfNode conditional, FunctionContext context, CodeBuilder builder)
    {
        CompileExpression(conditional.Condition, context, builder);
        var falseJump = builder.EmitJump(OpCode.JumpIfFalse, conditional.Condition.Span);
        CompileStatements(conditional.Then.Statements, context, builder);
        if (conditional.Else is null)
        {
            builder.Patch(falseJump, builder.Count);
            return;
        }

        var endJump = builder.EmitJump(OpCode.Jump, conditional.Span);
        builder.Patch(falseJump, builder.Count);
        CompileStatements(conditional.Else.Statements, context, builder);
        builder.Patch(endJump, builder.Count);
    }

    private void CompileWhile(WhileNode loop, FunctionContext context, CodeBuilder builder)
    {
        var conditionIp = builder.Count;
        CompileExpression(loop.Condition, context, builder);
        var exitJump = builder.EmitJump(OpCode.JumpIfFalse, loop.Condition.Span);
        var loopContext = context.EnterLoop(conditionIp);
        CompileStatements(loop.Body.Statements, context, builder);
        builder.Emit(OpCode.Jump, conditionIp, span: loop.Span);
        builder.Patch(exitJump, builder.Count);
        loopContext.PatchBreaks(builder, builder.Count);
        loopContext.PatchContinues(builder, conditionIp);
        context.ExitLoop(loopContext);
    }

    private void CompileFor(ForNode loop, FunctionContext context, CodeBuilder builder)
    {
        var counter = context.ResolveLocal(loop.Name);
        var useLocal = counter is not null;
        if (counter is null && !globalSlots.ContainsKey(loop.Name))
        {
            Error("FS2001", $"The loop variable '{loop.Name}' is not declared.", loop.Span);
            return;
        }

        CompileExpression(loop.Start, context, builder);
        Store(context, builder, loop.Name, loop.Span);
        var endSlot = useLocal ? context.GetForEndSlot(loop) : globalForSlots[loop].End;
        var stepSlot = useLocal ? context.GetForStepSlot(loop) : globalForSlots[loop].Step;
        CompileExpression(loop.End, context, builder);
        StoreSlot(builder, endSlot, useLocal, loop.Span);
        if (loop.Step is null)
            builder.Emit(OpCode.Const, AddConstant(FluidValue.From((long)1)), span: loop.Span);
        else
            CompileExpression(loop.Step, context, builder);
        StoreSlot(builder, stepSlot, useLocal, loop.Span);

        var loopIp = builder.Count;
        var check = useLocal
            ? builder.Emit(new Instruction(OpCode.ForCheckLocal, counter!.Value, endSlot, stepSlot, loop.Span, builder.Count))
            : builder.Emit(new Instruction(OpCode.ForCheckGlobal, globalSlots[loop.Name], endSlot, stepSlot, loop.Span, builder.Count));
        var loopContext = context.EnterLoop(continueTarget: -1);
        CompileStatements(loop.Body.Statements, context, builder);
        var incrementIp = builder.Count;
        loopContext.PatchContinues(builder, incrementIp);
        if (useLocal)
            builder.Emit(new Instruction(OpCode.ForIncrementLocal, counter!.Value, stepSlot, 0, loop.Span));
        else
            builder.Emit(new Instruction(OpCode.ForIncrementGlobal, globalSlots[loop.Name], stepSlot, 0, loop.Span));
        builder.Emit(OpCode.Jump, loopIp, span: loop.Span);
        builder.Patch(check, builder.Count);
        loopContext.PatchBreaks(builder, builder.Count);
        context.ExitLoop(loopContext);
    }

    private void CompileSwitch(SwitchNode switchNode, FunctionContext context, CodeBuilder builder)
    {
        CompileExpression(switchNode.Value, context, builder);
        var endJumps = new List<int>();
        foreach (var switchCase in switchNode.Cases)
        {
            builder.Emit(OpCode.Dup, span: switchCase.Span);
            CompileExpression(switchCase.Value, context, builder);
            builder.Emit(OpCode.Equal, span: switchCase.Span);
            var nextCase = builder.EmitJump(OpCode.JumpIfFalse, switchCase.Span);
            builder.Emit(OpCode.Pop, span: switchCase.Span);
            CompileStatements(switchCase.Body.Statements, context, builder);
            endJumps.Add(builder.EmitJump(OpCode.Jump, switchNode.Span));
            builder.Patch(nextCase, builder.Count);
        }

        builder.Emit(OpCode.Pop, span: switchNode.Span);
        if (switchNode.Otherwise is not null)
            CompileStatements(switchNode.Otherwise.Statements, context, builder);
        foreach (var endJump in endJumps)
            builder.Patch(endJump, builder.Count);
    }

    private void CompileTry(TryNode tryNode, FunctionContext context, CodeBuilder builder)
    {
        if (tryNode.Catch is null && tryNode.Finally is not null)
        {
            var finallyHandler = builder.Emit(new Instruction(OpCode.EnterHandler, 0, 0, 0, tryNode.Span));
            CompileStatements(tryNode.Body.Statements, context, builder);
            builder.Emit(OpCode.LeaveHandler, span: tryNode.Span);
            var normalPath = builder.EmitJump(OpCode.Jump, tryNode.Span);
            var normalFinally = builder.Count;
            CompileStatements(tryNode.Finally.Statements, context, builder);
            var end = builder.EmitJump(OpCode.Jump, tryNode.Span);
            var faultPath = builder.Count;
            builder.Patch(finallyHandler, faultPath);
            CompileStatements(tryNode.Finally.Statements, context, builder);
            builder.Emit(OpCode.Rethrow, span: tryNode.Span);
            builder.Patch(normalPath, normalFinally);
            builder.Patch(end, builder.Count);
            return;
        }

        var handler = tryNode.Catch is not null
            ? builder.Emit(new Instruction(OpCode.EnterHandler, 0, CatchFilterCode(tryNode.Catch.TypeName), 0, tryNode.Span))
            : -1;

        CompileStatements(tryNode.Body.Statements, context, builder);
        if (tryNode.Catch is not null)
            builder.Emit(OpCode.LeaveHandler, span: tryNode.Span);
        var afterBody = builder.EmitJump(OpCode.Jump, tryNode.Span);

        if (tryNode.Catch is { } catchNode)
        {
            var catchTarget = builder.Count;
            builder.Patch(handler, catchTarget);
            if (catchNode.Name is null)
                builder.Emit(OpCode.Pop, span: catchNode.Span);
            else
                Store(context, builder, catchNode.Name, catchNode.Span);
            CompileStatements(catchNode.Body.Statements, context, builder);
        }

        var finallyTarget = builder.Count;
        builder.Patch(afterBody, finallyTarget);
        if (tryNode.Finally is { } finallyNode)
            CompileStatements(finallyNode.Statements, context, builder);
    }

    private void CompileImport(ImportNode import)
    {
        if (resolver is null)
        {
            Error("FS2301", $"No module resolver is configured for '{import.Name}'.", import.Span);
            return;
        }
        if (!resolver.TryResolve(import.Name, out var source))
        {
            Error("FS2302", $"Module '{import.Name}' could not be resolved.", import.Span);
            return;
        }
        if (!session.TryEnter(import.Name, out var cached))
        {
            if (!cached)
                Error("FS2303", $"Module import cycle detected at '{import.Name}'.", import.Span);
            return;
        }
        var imported = FluidScriptCompiler.CompileCore(source, resolver, host, session);
        session.Exit(import.Name, imported.Success);
        foreach (var diagnostic in imported.Diagnostics)
            diagnostics.Add(diagnostic with { Code = diagnostic.Code == "FS2301" ? "FS2303" : diagnostic.Code });
    }

    private static int CatchFilterCode(string? typeName) => typeName switch
    {
        null or "any" => 0,
        "null" => (int)FluidValueKind.Null + 1,
        "bool" => (int)FluidValueKind.Bool + 1,
        "int" => (int)FluidValueKind.Int + 1,
        "decimal" => (int)FluidValueKind.Decimal + 1,
        "string" => (int)FluidValueKind.String + 1,
        "datetime" => (int)FluidValueKind.DateTime + 1,
        "guid" => (int)FluidValueKind.Guid + 1,
        "byte" => (int)FluidValueKind.Byte + 1,
        "dict" => (int)FluidValueKind.Dictionary + 1,
        _ => (int)FluidValueKind.Object + 1
    };

    private void CompileExpression(ExpressionNode expression, FunctionContext context, CodeBuilder builder)
    {
        switch (expression)
        {
            case LiteralNode literal:
                builder.Emit(OpCode.Const, AddConstant(literal.Value), span: literal.Span);
                break;
            case NameNode name:
                Load(context, builder, name.Name, name.Span);
                break;
            case UnaryNode unary:
                CompileExpression(unary.Operand, context, builder);
                if (unary.Operator is "!" or "-")
                    builder.Emit(unary.Operator == "!" ? OpCode.Not : OpCode.Neg, span: unary.Span);
                break;
            case BinaryNode binary:
                CompileBinary(binary, context, builder);
                break;
            case CallNode call:
                CompileCall(call, context, builder);
                break;
            case ArrayNode array:
                foreach (var element in array.Elements)
                    CompileExpression(element, context, builder);
                builder.Emit(OpCode.MakeArray, array.Elements.Count, span: array.Span);
                break;
            case DictionaryNode dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    CompileExpression(entry.Key, context, builder);
                    CompileExpression(entry.Value, context, builder);
                }
                builder.Emit(OpCode.MakeDictionary, dictionary.Entries.Count, span: dictionary.Span);
                break;
            case IndexNode index:
                CompileExpression(index.Target, context, builder);
                CompileExpression(index.Index, context, builder);
                builder.Emit(OpCode.IndexGet, span: index.Span);
                break;
            case MemberNode member:
                var memberType = InferType(member.Target, context);
                if (!TryGetField(memberType, member.Name, out _, out var memberIndex, out _))
                {
                    Error("FS2202", $"Type '{memberType}' has no member '{member.Name}'.", member.Span);
                    builder.Emit(OpCode.Null, span: member.Span);
                    break;
                }
                CompileExpression(member.Target, context, builder);
                builder.Emit(OpCode.FieldGet, memberIndex, span: member.Span);
                break;
            case LambdaNode lambda:
                var lambdaCompilation = CompileLambda(lambda, context);
                foreach (var capture in lambdaCompilation.Captures)
                    EmitCaptureCell(capture, context, builder, lambda.Span);
                builder.Emit(OpCode.MakeClosure, lambdaCompilation.FunctionId, lambdaCompilation.Captures.Count, span: lambda.Span);
                break;
            case InterpolatedStringNode interpolated:
                CompileInterpolatedString(interpolated, context, builder);
                break;
            default:
                Error("FS3001", "This expression is not implemented yet.", expression.Span);
                builder.Emit(OpCode.Null, span: expression.Span);
                break;
        }
    }

    private void CompileBinary(BinaryNode binary, FunctionContext context, CodeBuilder builder)
    {
        if (binary.Operator is "&&" or "||")
        {
            CompileExpression(binary.Left, context, builder);
            var branch = builder.EmitJump(OpCode.JumpIfFalse, binary.Left.Span);
            if (binary.Operator == "&&")
            {
                CompileExpression(binary.Right, context, builder);
                var endAnd = builder.EmitJump(OpCode.Jump, binary.Span);
                builder.Patch(branch, builder.Count);
                builder.Emit(OpCode.Const, AddConstant(FluidValue.From(false)), span: binary.Span);
                builder.Patch(endAnd, builder.Count);
            }
            else
            {
                builder.Emit(OpCode.Const, AddConstant(FluidValue.From(true)), span: binary.Span);
                var endOr = builder.EmitJump(OpCode.Jump, binary.Span);
                builder.Patch(branch, builder.Count);
                CompileExpression(binary.Right, context, builder);
                builder.Patch(endOr, builder.Count);
            }
            return;
        }

        CompileExpression(binary.Left, context, builder);
        CompileExpression(binary.Right, context, builder);
        builder.Emit(BinaryOpcode(binary.Operator), span: binary.Span);
    }

    private void CompileInterpolatedString(InterpolatedStringNode interpolated, FunctionContext context, CodeBuilder builder)
    {
        var emitted = false;
        foreach (var part in interpolated.Parts)
        {
            if (part.Text is not null)
                builder.Emit(OpCode.Const, AddConstant(FluidValue.From(part.Text)), span: interpolated.Span);
            else
            {
                CompileExpression(part.Expression!, context, builder);
                builder.Emit(OpCode.ToText, span: interpolated.Span);
            }
            if (emitted)
                builder.Emit(OpCode.Add, span: interpolated.Span);
            emitted = true;
        }
        if (!emitted)
            builder.Emit(OpCode.Const, AddConstant(FluidValue.From(string.Empty)), span: interpolated.Span);
    }

    private void CompileCall(CallNode call, FunctionContext context, CodeBuilder builder)
    {
        if (call.Target is MemberNode member)
        {
            var ownerType = InferType(member.Target, context);
            if (!TryGetMethod(ownerType, member.Name, out var methodId, out var method))
            {
                Error("FS2202", $"Type '{ownerType}' has no method '{member.Name}'.", member.Span);
                builder.Emit(OpCode.Null, span: call.Span);
                return;
            }
            CompileExpression(member.Target, context, builder);
            var orderedMethodArguments = BindArguments(method, call.Arguments, call.Span);
            foreach (var argument in orderedMethodArguments)
            {
                CheckAssignable(ValidateType(method.Parameters[argument.ParameterIndex].TypeName, method.Parameters[argument.ParameterIndex].Span), InferType(argument.Value, context), argument.Value.Span);
                CompileExpression(argument.Value, context, builder);
            }
            builder.Emit(OpCode.Call, methodId, orderedMethodArguments.Count + 1, span: call.Span);
            return;
        }
        if (call.Target is not NameNode target)
        {
            Error("FS2009", "Only named function calls are supported yet.", call.Span);
            builder.Emit(OpCode.Null, span: call.Span);
            return;
        }
        if (target.Name == "jsonSerialize")
        {
            CompileBuiltinCall(call, context, builder, FluidScriptHost.JsonSerializeBuiltinId);
        }
        else if (target.Name == "jsonDeserialize")
        {
            CompileBuiltinCall(call, context, builder, FluidScriptHost.JsonDeserializeBuiltinId);
        }
        else if (target.Name == "jsonDeserializeAs")
        {
            CompileJsonDeserializeAsCall(call, context, builder);
        }
        else if (functionIds.TryGetValue(target.Name, out var functionId))
        {
            var function = functionDeclarations[target.Name];
            var ordered = BindArguments(function, call.Arguments, call.Span);
            foreach (var argument in ordered)
            {
                CheckAssignable(ValidateType(function.Parameters[argument.ParameterIndex].TypeName, function.Parameters[argument.ParameterIndex].Span), InferType(argument.Value, context), argument.Value.Span);
                CompileExpression(argument.Value, context, builder);
            }
            builder.Emit(OpCode.Call, functionId, ordered.Count, span: call.Span);
        }
        else if (typeIds.TryGetValue(target.Name, out var typeId))
        {
            var type = typeDeclarations[target.Name];
            var ordered = BindFieldArguments(type, call.Arguments, call.Span);
            for (var fieldIndex = 0; fieldIndex < ordered.Count; fieldIndex++)
            {
                var argument = ordered[fieldIndex];
                CheckAssignable(ValidateType(type.Fields[fieldIndex].TypeName, type.Fields[fieldIndex].Span), InferType(argument, context), argument.Span);
                CompileExpression(argument, context, builder);
            }
            builder.Emit(OpCode.NewObject, typeId, ordered.Count, span: call.Span);
        }
        else if (target.Name == "print")
        {
            foreach (var argument in call.Arguments)
                CompileExpression(argument.Value, context, builder);
            builder.Emit(OpCode.CallNative, 0, call.Arguments.Count, span: call.Span);
        }
        else if (host is not null && host.TryGetFunctionId(target.Name, out var nativeId))
        {
            foreach (var argument in call.Arguments)
                CompileExpression(argument.Value, context, builder);
            builder.Emit(OpCode.CallNative, nativeId, call.Arguments.Count, span: call.Span);
        }
        else
        {
            if (context.ResolveType(target.Name) is null && !globalTypes.ContainsKey(target.Name))
            {
                Error("FS2010", $"Unknown function '{target.Name}'.", call.Span);
                builder.Emit(OpCode.Null, span: call.Span);
                return;
            }
            CompileExpression(call.Target, context, builder);
            foreach (var argument in call.Arguments)
                CompileExpression(argument.Value, context, builder);
            builder.Emit(OpCode.CallIndirect, 0, call.Arguments.Count, span: call.Span);
        }
    }

    private void CompileBuiltinCall(CallNode call, FunctionContext context, CodeBuilder builder, int builtinId)
    {
        foreach (var argument in call.Arguments)
            CompileExpression(argument.Value, context, builder);
        builder.Emit(OpCode.CallNative, builtinId, call.Arguments.Count, span: call.Span);
    }

    private void CompileJsonDeserializeAsCall(CallNode call, FunctionContext context, CodeBuilder builder)
    {
        if (call.Arguments.Count != 2 || call.Arguments.Any(argument => argument.Name is not null) ||
            call.Arguments[0].Value is not NameNode typeName || !typeIds.TryGetValue(typeName.Name, out var typeId))
        {
            Error("FS2105", "jsonDeserializeAs expects a declared type and a JSON string.", call.Span);
            builder.Emit(OpCode.Null, span: call.Span);
            return;
        }

        CompileExpression(call.Arguments[1].Value, context, builder);
        builder.Emit(new Instruction(
            OpCode.CallNative,
            FluidScriptHost.JsonDeserializeAsBuiltinId,
            1,
            typeId,
            call.Span));
    }

    private LambdaCompilation CompileLambda(LambdaNode lambda, FunctionContext parent)
    {
        var name = $"$lambda{functions.Count}";
        var captures = FindCaptures(lambda, parent);
        var context = new FunctionContext(name, lambda.Parameters.Count, false, globalSlots, globalConstants, globalTypes, parent.OwnerType);
        foreach (var capture in captures)
            context.AddCapture(capture);
        foreach (var parameter in lambda.Parameters)
            context.AddLocal(parameter.Name, parameter.Span, false, ValidateType(parameter.TypeName, parameter.Span));
        var builder = new CodeBuilder();
        if (lambda.ExpressionBody is not null)
        {
            CheckAssignable(ValidateType(lambda.ReturnType, lambda.Span), InferType(lambda.ExpressionBody, context), lambda.ExpressionBody.Span);
            CompileExpression(lambda.ExpressionBody, context, builder);
            builder.Emit(OpCode.Return, span: lambda.Span);
        }
        else if (lambda.BlockBody is not null)
        {
            context.ReturnType = ValidateType(lambda.ReturnType, lambda.Span);
            DeclareLocals(lambda.BlockBody.Statements, context);
            CompileStatements(lambda.BlockBody.Statements, context, builder);
            if (builder.Instructions.Count == 0 || builder.Instructions[^1].OpCode is not (OpCode.Return or OpCode.ReturnVoid))
                builder.Emit(OpCode.ReturnVoid, span: lambda.Span);
        }
        var functionId = functions.Count;
        functions.Add(new PCodeFunction(name, lambda.Parameters.Count, context.LocalCount, builder.Instructions, context.LocalNames, captures));
        return new LambdaCompilation(functionId, captures);
    }

    private void EmitCaptureCell(string name, FunctionContext parent, CodeBuilder builder, SourceSpan span)
    {
        if (parent.ResolveLocal(name) is { } local)
            builder.Emit(OpCode.LoadCell, local, span: span);
        else if (parent.ResolveCapture(name) is { } capture)
            builder.Emit(OpCode.LoadCaptureCell, capture, span: span);
        else
            builder.Emit(OpCode.Null, span: span);
    }

    private IReadOnlyList<string> FindCaptures(LambdaNode lambda, FunctionContext parent)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (lambda.ExpressionBody is not null)
            CollectNames(lambda.ExpressionBody, names);
        if (lambda.BlockBody is not null)
            CollectNames(lambda.BlockBody.Statements, names);
        foreach (var parameter in lambda.Parameters)
            names.Remove(parameter.Name);
        if (lambda.BlockBody is not null)
            foreach (var local in CollectDeclaredNames(lambda.BlockBody.Statements))
                names.Remove(local);
        return names.Where(name => parent.ResolveLocal(name) is not null || parent.ResolveCapture(name) is not null ||
            (parent.OwnerType is not null && TryGetField(parent.OwnerType, name, out _, out _, out _)))
            .Select(name => parent.OwnerType is not null && parent.ResolveLocal(name) is null && parent.ResolveCapture(name) is null &&
                TryGetField(parent.OwnerType, name, out _, out _, out _) ? "self" : name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static HashSet<string> CollectDeclaredNames(IEnumerable<StatementNode> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            if (statement is VariableNode variable)
                names.Add(variable.Name);
            else if (statement is ForNode loop)
                names.Add(loop.Name);
        }
        return names;
    }

    private static void CollectNames(IEnumerable<StatementNode> statements, ISet<string> names)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case VariableNode variable when variable.Initializer is not null:
                    CollectNames(variable.Initializer, names); break;
                case AssignmentNode assignment:
                    CollectNames(assignment.Target, names); CollectNames(assignment.Value, names); break;
                case ExpressionStatementNode expression:
                    CollectNames(expression.Expression, names); break;
                case IfNode conditional:
                    CollectNames(conditional.Condition, names); CollectNames(conditional.Then.Statements, names);
                    if (conditional.Else is not null) CollectNames(conditional.Else.Statements, names); break;
                case WhileNode loop:
                    CollectNames(loop.Condition, names); CollectNames(loop.Body.Statements, names); break;
                case ForNode loop:
                    CollectNames(loop.Start, names); CollectNames(loop.End, names);
                    if (loop.Step is not null) CollectNames(loop.Step, names);
                    CollectNames(loop.Body.Statements, names); break;
                case SwitchNode switchNode:
                    CollectNames(switchNode.Value, names);
                    foreach (var item in switchNode.Cases) { CollectNames(item.Value, names); CollectNames(item.Body.Statements, names); }
                    if (switchNode.Otherwise is not null) CollectNames(switchNode.Otherwise.Statements, names); break;
                case ReturnNode returnNode when returnNode.Value is not null:
                    CollectNames(returnNode.Value, names); break;
                case ThrowNode throwNode:
                    CollectNames(throwNode.Value, names); break;
                case TryNode tryNode:
                    CollectNames(tryNode.Body.Statements, names);
                    if (tryNode.Catch is not null) CollectNames(tryNode.Catch.Body.Statements, names);
                    if (tryNode.Finally is not null) CollectNames(tryNode.Finally.Statements, names); break;
            }
        }
    }

    private static void CollectNames(ExpressionNode expression, ISet<string> names)
    {
        switch (expression)
        {
            case NameNode name: names.Add(name.Name); break;
            case UnaryNode unary: CollectNames(unary.Operand, names); break;
            case BinaryNode binary: CollectNames(binary.Left, names); CollectNames(binary.Right, names); break;
            case CallNode call:
                CollectNames(call.Target, names); foreach (var argument in call.Arguments) CollectNames(argument.Value, names); break;
            case ArrayNode array: foreach (var item in array.Elements) CollectNames(item, names); break;
            case DictionaryNode dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    CollectNames(entry.Key, names);
                    CollectNames(entry.Value, names);
                }
                break;
            case IndexNode index: CollectNames(index.Target, names); CollectNames(index.Index, names); break;
            case MemberNode member: CollectNames(member.Target, names); break;
            case InterpolatedStringNode interpolated:
                foreach (var part in interpolated.Parts) if (part.Expression is not null) CollectNames(part.Expression, names); break;
            case LambdaNode lambda when lambda.ExpressionBody is not null: CollectNames(lambda.ExpressionBody, names); break;
        }
    }

    private sealed record LambdaCompilation(int FunctionId, IReadOnlyList<string> Captures);

    private IReadOnlyList<ExpressionNode> BindFieldArguments(TypeNode type, IReadOnlyList<ArgumentNode> arguments, SourceSpan span)
    {
        var fields = type.Fields;
        var byName = fields.Select((field, index) => (field.Name, index)).ToDictionary(item => item.Name, item => item.index, StringComparer.Ordinal);
        var bound = new ExpressionNode?[fields.Count];
        var position = 0;
        var named = false;
        foreach (var argument in arguments)
        {
            int index;
            if (argument.Name is null)
            {
                if (named)
                    Error("FS2105", "Positional arguments must precede named arguments.", argument.Span);
                index = position++;
            }
            else
            {
                named = true;
                if (!byName.TryGetValue(argument.Name, out index))
                {
                    Error("FS2106", $"Unknown named argument '{argument.Name}'.", argument.Span);
                    continue;
                }
            }
            if (index < 0 || index >= bound.Length)
            {
                Error("FS2107", $"Too many constructor arguments for '{type.Name}'.", argument.Span);
                continue;
            }
            if (bound[index] is not null)
            {
                Error("FS2108", $"Member '{fields[index].Name}' was provided more than once.", argument.Span);
                continue;
            }
            bound[index] = argument.Value;
        }

        var result = new List<ExpressionNode>(fields.Count);
        for (var index = 0; index < fields.Count; index++)
        {
            if (bound[index] is not null)
                result.Add(bound[index]!);
            else if (fields[index].Initializer is not null)
                result.Add(fields[index].Initializer!);
            else
                result.Add(new LiteralNode(FluidValue.Null, span));
        }
        return result;
    }

    private bool TryGetField(string typeName, string fieldName, out PCodeType type, out int fieldIndex, out VariableNode field)
    {
        type = null!;
        field = null!;
        fieldIndex = -1;
        if (!typeIds.TryGetValue(typeName, out var typeId))
            return false;
        type = types[typeId];
        for (var index = 0; index < type.FieldNames.Count; index++)
            if (string.Equals(type.FieldNames[index], fieldName, StringComparison.Ordinal))
            {
                fieldIndex = index;
                break;
            }
        if (fieldIndex < 0)
            return false;
        field = typeDeclarations[typeName].Fields[fieldIndex];
        return true;
    }

    private bool TryGetMethod(string typeName, string methodName, out int functionId, out FunctionNode method)
    {
        if (methodIds.TryGetValue(MethodKey(typeName, methodName), out functionId) &&
            methodDeclarations.TryGetValue(MethodKey(typeName, methodName), out var declaration))
        {
            method = declaration.Method;
            return true;
        }
        functionId = -1;
        method = null!;
        return false;
    }

    private static string MethodKey(string typeName, string methodName) => typeName + "." + methodName;

    private IReadOnlyList<BoundArgument> BindArguments(FunctionNode function, IReadOnlyList<ArgumentNode> arguments, SourceSpan span)
    {
        var byName = function.Parameters.Select((parameter, index) => (parameter.Name, index))
            .ToDictionary(item => item.Name, item => item.index, StringComparer.Ordinal);
        var bound = new BoundArgument?[function.Parameters.Count];
        var positionalIndex = 0;
        var sawNamed = false;
        foreach (var argument in arguments)
        {
            int parameterIndex;
            if (argument.Name is null)
            {
                if (sawNamed)
                    Error("FS2105", "Positional arguments must precede named arguments.", argument.Span);
                while (positionalIndex < bound.Length && bound[positionalIndex] is not null)
                    positionalIndex++;
                parameterIndex = positionalIndex++;
            }
            else
            {
                sawNamed = true;
                if (!byName.TryGetValue(argument.Name, out parameterIndex))
                {
                    Error("FS2106", $"Unknown named argument '{argument.Name}'.", argument.Span);
                    continue;
                }
            }

            if (parameterIndex < 0 || parameterIndex >= bound.Length)
            {
                Error("FS2107", $"Too many arguments for '{function.Name}'.", argument.Span);
                continue;
            }
            if (bound[parameterIndex] is not null)
            {
                Error("FS2108", $"Argument '{function.Parameters[parameterIndex].Name}' was provided more than once.", argument.Span);
                continue;
            }
            bound[parameterIndex] = new BoundArgument(parameterIndex, argument.Value);
        }

        var result = new List<BoundArgument>(bound.Length);
        for (var index = 0; index < bound.Length; index++)
        {
            if (bound[index] is { } provided)
            {
                result.Add(provided);
                continue;
            }
            if (function.Parameters[index].DefaultValue is { } defaultValue)
            {
                result.Add(new BoundArgument(index, defaultValue));
                continue;
            }
            Error("FS2109", $"Missing required argument '{function.Parameters[index].Name}'.", span);
            result.Add(new BoundArgument(index, new LiteralNode(FluidValue.Null, span)));
        }
        return result;
    }

    private static bool ContainsReturn(BlockNode block) => block.Statements.Any(statement => statement switch
    {
        ReturnNode => true,
        IfNode conditional => ContainsReturn(conditional.Then) || (conditional.Else is not null && ContainsReturn(conditional.Else)),
        WhileNode loop => ContainsReturn(loop.Body),
        ForNode loop => ContainsReturn(loop.Body),
        SwitchNode switchNode => switchNode.Cases.Any(item => ContainsReturn(item.Body)) || (switchNode.Otherwise is not null && ContainsReturn(switchNode.Otherwise)),
        _ => false
    });

    private void EmitLoopControl(CodeBuilder builder, FunctionContext context, SourceSpan span, bool isContinue)
    {
        var loop = context.CurrentLoop;
        if (loop is null)
        {
            Error("FS2011", isContinue ? "continue is only valid inside a loop." : "break is only valid inside a loop.", span);
            return;
        }
        var jump = builder.EmitJump(OpCode.Jump, span);
        (isContinue ? loop.ContinueJumps : loop.BreakJumps).Add(jump);
    }

    private void Load(FunctionContext context, CodeBuilder builder, string name, SourceSpan span)
    {
        if (context.ResolveLocal(name) is { } local)
            builder.Emit(OpCode.LoadLocal, local, span: span);
        else if (context.ResolveCapture(name) is { } capture)
            builder.Emit(OpCode.LoadCapture, capture, span: span);
        else if (globalSlots.TryGetValue(name, out var global))
            builder.Emit(OpCode.LoadGlobal, global, span: span);
        else if (context.OwnerType is not null && TryGetField(context.OwnerType, name, out _, out var fieldIndex, out _))
        {
            if (context.ResolveLocal("self") is { } selfLocal)
                builder.Emit(OpCode.LoadLocal, selfLocal, span: span);
            else if (context.ResolveCapture("self") is { } selfCapture)
                builder.Emit(OpCode.LoadCapture, selfCapture, span: span);
            else
                builder.Emit(OpCode.Null, span: span);
            builder.Emit(OpCode.FieldGet, fieldIndex, span: span);
        }
        else
        {
            Error("FS2001", $"The name '{name}' is not declared.", span);
            builder.Emit(OpCode.Null, span: span);
        }
    }

    private void Store(FunctionContext context, CodeBuilder builder, string name, SourceSpan span)
    {
        if (context.ResolveLocal(name) is { } local)
            builder.Emit(OpCode.StoreLocal, local, span: span);
        else if (context.ResolveCapture(name) is { } capture)
            builder.Emit(OpCode.StoreCapture, capture, span: span);
        else if (globalSlots.TryGetValue(name, out var global))
            builder.Emit(OpCode.StoreGlobal, global, span: span);
        else if (context.OwnerType is not null && TryGetField(context.OwnerType, name, out _, out var fieldIndex, out _))
        {
            if (context.ResolveLocal("self") is { } selfLocal)
                builder.Emit(OpCode.LoadLocal, selfLocal, span: span);
            else if (context.ResolveCapture("self") is { } selfCapture)
                builder.Emit(OpCode.LoadCapture, selfCapture, span: span);
            else
                builder.Emit(OpCode.Null, span: span);
            builder.Emit(OpCode.Swap, span: span);
            builder.Emit(OpCode.FieldSet, fieldIndex, span: span);
        }
        else
            Error("FS2001", $"The name '{name}' is not declared.", span);
    }

    private static void StoreSlot(CodeBuilder builder, int slot, bool local, SourceSpan span) =>
        builder.Emit(local ? OpCode.StoreLocal : OpCode.StoreGlobal, slot, span: span);

    private int AddGlobal(string name, bool isConstant, SourceSpan span)
    {
        if (globalSlots.TryGetValue(name, out var existing))
        {
            Error("FS2006", $"The name '{name}' is declared more than once.", span);
            return existing;
        }
        var slot = globalSlots.Count;
        globalSlots.Add(name, slot);
        globalNames.Add(name);
        if (isConstant)
            globalConstants.Add(name);
        return slot;
    }

    private int AddConstant(FluidValue value)
    {
        var existing = constants.IndexOf(value);
        if (existing >= 0)
            return existing;
        constants.Add(value);
        return constants.Count - 1;
    }

    private static OpCode BinaryOpcode(string operatorText) => operatorText switch
    {
        "+" => OpCode.Add,
        "-" => OpCode.Sub,
        "*" => OpCode.Mul,
        "/" => OpCode.Div,
        "%" => OpCode.Mod,
        "==" => OpCode.Equal,
        "!=" => OpCode.NotEqual,
        "<" => OpCode.Less,
        "<=" => OpCode.LessOrEqual,
        ">" => OpCode.Greater,
        ">=" => OpCode.GreaterOrEqual,
        _ => throw new InvalidOperationException($"Unknown binary operator '{operatorText}'.")
    };

    private string ValidateType(string? typeName, SourceSpan span)
    {
        var normalized = string.IsNullOrWhiteSpace(typeName) ? "any" : typeName!;
        if (normalized.Contains("=>", StringComparison.Ordinal))
        {
            var arrow = normalized.IndexOf("=>", StringComparison.Ordinal);
            var parameterText = normalized[..arrow].Trim().Trim('(', ')');
            if (parameterText.Length > 0)
                foreach (var parameter in parameterText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    ValidateType(parameter, span);
            ValidateType(normalized[(arrow + 2)..].Trim(), span);
            return "fn";
        }
        var baseName = normalized.TrimEnd('[', ']');
        var isArray = normalized.EndsWith("[]", StringComparison.Ordinal);
        if (baseName is not ("any" or "null" or "bool" or "int" or "decimal" or "string" or "datetime" or "guid" or "byte" or "dict") &&
            !typeIds.ContainsKey(baseName))
            Error("FS2100", $"Unknown type '{baseName}'.", span);
        return isArray ? baseName + "[]" : baseName;
    }

    private void CheckAssignable(string? expected, string actual, SourceSpan span)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected == "any" || actual == "any" || actual == "null")
            return;
        if (expected == "decimal" && actual == "int")
            return;
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            Error("FS2101", $"Cannot assign a value of type {actual} to {expected}.", span);
    }

    private string InferType(ExpressionNode expression, FunctionContext context) => expression switch
    {
        LiteralNode literal => literal.Value.Kind switch
        {
            FluidValueKind.Null => "null",
            FluidValueKind.Bool => "bool",
            FluidValueKind.Int => "int",
            FluidValueKind.Decimal => "decimal",
            FluidValueKind.String => "string",
            FluidValueKind.DateTime => "datetime",
            FluidValueKind.Guid => "guid",
            FluidValueKind.Byte => "byte",
            _ => "any"
        },
        NameNode name => context.ResolveType(name.Name) ?? (globalTypes.TryGetValue(name.Name, out var globalType) ? globalType : "any"),
        ArrayNode array => array.Elements.Count == 0 ? "any[]" : InferArrayType(array.Elements, context),
        DictionaryNode => "dict",
        IndexNode index => InferIndexedType(InferType(index.Target, context)),
        MemberNode member => InferMemberType(member, context),
        UnaryNode unary when unary.Operator == "!" => "bool",
        UnaryNode unary => InferType(unary.Operand, context),
        BinaryNode binary when binary.Operator is "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||" => "bool",
        BinaryNode binary when binary.Operator == "+" && InferType(binary.Left, context) == "string" && InferType(binary.Right, context) == "string" => "string",
        BinaryNode binary => InferNumericType(InferType(binary.Left, context), InferType(binary.Right, context)),
        CallNode call when call.Target is NameNode name && functionReturnTypes.TryGetValue(name.Name, out var returnType) => returnType,
        CallNode call when call.Target is NameNode { Name: "print" } => "null",
        CallNode call when call.Target is NameNode { Name: "jsonSerialize" } => "string",
        CallNode call when call.Target is NameNode { Name: "jsonDeserialize" } => "any",
        CallNode call when call.Target is NameNode { Name: "jsonDeserializeAs" } &&
            call.Arguments.Count > 0 && call.Arguments[0].Value is NameNode typeName && typeIds.ContainsKey(typeName.Name) => typeName.Name,
        CallNode call when call.Target is NameNode name && typeIds.ContainsKey(name.Name) => name.Name,
        CallNode call when call.Target is MemberNode member && functionReturnTypes.TryGetValue(MethodKey(InferType(member.Target, context), member.Name), out var methodReturnType) => methodReturnType,
        LambdaNode => "fn",
        InterpolatedStringNode => "string",
        _ => "any"
    };

    private string InferArrayType(IReadOnlyList<ExpressionNode> elements, FunctionContext context)
    {
        var first = InferType(elements[0], context);
        return elements.Skip(1).Any(element => InferType(element, context) != first) ? "any[]" : first + "[]";
    }

    private static string InferIndexedType(string typeName) => typeName.EndsWith("[]", StringComparison.Ordinal)
        ? typeName[..^2]
        : "any";

    private static string InferNumericType(string left, string right) => left == "int" && right == "int" ? "int" : "decimal";

    private string InferMemberType(MemberNode member, FunctionContext context)
    {
        var ownerType = InferType(member.Target, context);
        return TryGetField(ownerType, member.Name, out _, out _, out var field)
            ? ValidateType(field.TypeName, field.Span)
            : "any";
    }

    private sealed record BoundArgument(int ParameterIndex, ExpressionNode Value);

    private void Error(string code, string message, SourceSpan span) =>
        diagnostics.Add(new Diagnostic(code, message, span));

    private sealed class CodeBuilder
    {
        private readonly List<Instruction> instructions = new();
        public IReadOnlyList<Instruction> Instructions => instructions;
        public int Count => instructions.Count;

        public int Emit(OpCode opcode, int operandA = 0, int operandB = 0, int operandC = 0, SourceSpan span = default) =>
            Emit(new Instruction(opcode, operandA, operandB, operandC, span));

        public int Emit(Instruction instruction)
        {
            instructions.Add(instruction);
            return instructions.Count - 1;
        }

        public int EmitJump(OpCode opcode, SourceSpan span = default) => Emit(opcode, 0, span: span);

        public void Patch(int instructionIndex, int target)
        {
            var instruction = instructions[instructionIndex];
            instructions[instructionIndex] = instruction.OpCode is OpCode.ForCheckLocal or OpCode.ForCheckGlobal
                ? instruction with { OperandD = target }
                : instruction with { OperandA = target };
        }
    }

    private sealed class FunctionContext(
        string name,
        int arity,
        bool isMain,
        IReadOnlyDictionary<string, int> globals,
        IReadOnlySet<string> globalConstants,
        IReadOnlyDictionary<string, string> globalTypes,
        string? ownerType = null)
    {
        private readonly Dictionary<string, int> locals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> captures = new(StringComparer.Ordinal);
        private readonly HashSet<string> localConstants = new(StringComparer.Ordinal);
        private readonly Dictionary<ForNode, (int End, int Step)> forSlots = new();
        private readonly IReadOnlyDictionary<string, int> globals = globals;
        private readonly IReadOnlySet<string> globalConstants = globalConstants;
        private readonly IReadOnlyDictionary<string, string> globalTypes = globalTypes;
        public string? OwnerType { get; } = ownerType;
        private int nextSlot;

        public string Name { get; } = name;
        public int Arity { get; } = arity;
        public bool IsMain { get; } = isMain;
        public int LocalCount => nextSlot;
        public Dictionary<int, string> LocalNames { get; } = new();
        public IReadOnlyList<string> CaptureNames => captures.OrderBy(item => item.Value).Select(item => item.Key).ToArray();
        public LoopContext? CurrentLoop { get; private set; }
        public string ReturnType { get; set; } = "any";

        public int AddLocal(string name, SourceSpan span, bool isConstant, string typeName = "any")
        {
            if (locals.TryGetValue(name, out var existing))
                return existing;
            var slot = nextSlot++;
            locals.Add(name, slot);
            LocalNames[slot] = name;
            if (isConstant)
                localConstants.Add(name);
            localTypes[name] = typeName;
            return slot;
        }

        public void AddHiddenForSlots(ForNode loop)
        {
            if (!forSlots.ContainsKey(loop))
                forSlots.Add(loop, (nextSlot++, nextSlot++));
        }

        public int? ResolveLocal(string name) => locals.TryGetValue(name, out var slot) ? slot : null;
        public int? ResolveCapture(string name) => captures.TryGetValue(name, out var slot) ? slot : null;
        public void AddCapture(string name)
        {
            if (!captures.ContainsKey(name))
                captures.Add(name, captures.Count);
        }
        private readonly Dictionary<string, string> localTypes = new(StringComparer.Ordinal);
        public string? ResolveType(string name) => localTypes.TryGetValue(name, out var localType)
            ? localType
            : globalTypes.TryGetValue(name, out var globalType) ? globalType : null;
        public void SetType(string name, string typeName)
        {
            if (locals.ContainsKey(name))
                localTypes[name] = typeName;
        }
        public bool IsConstant(string name) => localConstants.Contains(name);
        public int GetForEndSlot(ForNode loop) => forSlots.TryGetValue(loop, out var slots) ? slots.End : AddForSlots(loop).End;
        public int GetForStepSlot(ForNode loop) => forSlots.TryGetValue(loop, out var slots) ? slots.Step : AddForSlots(loop).Step;

        public LoopContext EnterLoop(int continueTarget)
        {
            var loop = new LoopContext(CurrentLoop, continueTarget);
            CurrentLoop = loop;
            return loop;
        }

        public void ExitLoop(LoopContext loop)
        {
            if (ReferenceEquals(CurrentLoop, loop))
                CurrentLoop = loop.Parent;
        }

        private (int End, int Step) AddForSlots(ForNode loop)
        {
            var slots = (nextSlot++, nextSlot++);
            forSlots.Add(loop, slots);
            return slots;
        }

        public sealed class LoopContext(LoopContext? parent, int continueTarget)
        {
            public List<int> BreakJumps { get; } = new();
            public List<int> ContinueJumps { get; } = new();
            public int ContinueTarget { get; } = continueTarget;
            public LoopContext? Parent { get; } = parent;

            public void PatchBreaks(CodeBuilder builder, int target)
            {
                foreach (var jump in BreakJumps)
                    builder.Patch(jump, target);
            }

            public void PatchContinues(CodeBuilder builder, int target)
            {
                foreach (var jump in ContinueJumps)
                    builder.Patch(jump, target);
            }
        }
    }
}

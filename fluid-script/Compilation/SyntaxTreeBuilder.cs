using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using FluidScript.Generated;
using FluidScript.Parsing;
using FluidScript.Runtime;

namespace FluidScript.Compilation;

internal sealed class SyntaxTreeBuilder
{
    private readonly List<Diagnostic> diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

    public ScriptNode Build(FluidScriptParser.ProgramContext program)
    {
        var statements = program.statementList()?.statement()
            .Select(BuildStatement)
            .Where(statement => statement is not null)
            .Cast<StatementNode>()
            .ToArray() ?? Array.Empty<StatementNode>();

        return new ScriptNode(statements, FluidScriptFrontEnd.Span(program));
    }

    private StatementNode? BuildStatement(FluidScriptParser.StatementContext context)
    {
        if (context.variableDeclaration() is { } variable)
            return BuildVariable(variable);
        if (context.constantDeclaration() is { } constant)
            return BuildConstant(constant);
        if (context.assignmentStatement() is { } assignment)
            return BuildAssignment(assignment);
        if (context.ifStatement() is { } conditional)
            return BuildIf(conditional);
        if (context.whileStatement() is { } loop)
            return BuildWhile(loop);
        if (context.forStatement() is { } forStatement)
            return BuildFor(forStatement);
        if (context.switchStatement() is { } switchStatement)
            return BuildSwitch(switchStatement);
        if (context.functionDeclaration() is { } function)
            return BuildFunction(function);
        if (context.typeDeclaration() is { } type)
            return BuildType(type);
        if (context.returnStatement() is { } returnStatement)
            return BuildReturn(returnStatement);
        if (context.breakStatement() is not null)
            return new BreakNode(FluidScriptFrontEnd.Span(context));
        if (context.continueStatement() is not null)
            return new ContinueNode(FluidScriptFrontEnd.Span(context));
        if (context.tryStatement() is { } tryStatement)
            return BuildTry(tryStatement);
        if (context.throwStatement() is { } throwStatement)
            return BuildThrow(throwStatement);
        if (context.importStatement() is { } importStatement)
            return BuildImport(importStatement);
        if (context.expressionStatement() is { } expressionStatement &&
            BuildExpression(expressionStatement.expression()) is { } expression)
            return new ExpressionStatementNode(expression, FluidScriptFrontEnd.Span(context));

        AddUnsupported(context, "This statement is not implemented yet.");
        return null;
    }

    private TryNode BuildTry(FluidScriptParser.TryStatementContext context)
    {
        var catchClause = context.catchClause();
        var catchNode = catchClause is null
            ? null
            : new CatchNode(
                catchClause.IDENTIFIER()?.GetText(),
                TypeName(catchClause.typeHint()),
                BuildBlock(catchClause.block()),
                FluidScriptFrontEnd.Span(catchClause));
        var finallyNode = context.finallyClause() is { } finallyClause
            ? BuildBlock(finallyClause.block())
            : null;
        return new TryNode(BuildBlock(context.block()), catchNode, finallyNode, FluidScriptFrontEnd.Span(context));
    }

    private ThrowNode? BuildThrow(FluidScriptParser.ThrowStatementContext context) =>
        BuildExpression(context.expression()) is { } value
            ? new ThrowNode(value, FluidScriptFrontEnd.Span(context))
            : null;

    private ImportNode BuildImport(FluidScriptParser.ImportStatementContext context)
    {
        if (context.STRING() is { } path)
            return new ImportNode(Unescape(path.GetText()), true, FluidScriptFrontEnd.Span(context));
        var qualified = context.qualifiedName();
        return new ImportNode(qualified?.GetText() ?? string.Empty, false, FluidScriptFrontEnd.Span(context));
    }

    private VariableNode? BuildVariable(FluidScriptParser.VariableDeclarationContext context) =>
        new(
            context.IDENTIFIER().GetText(),
            TypeName(context.typeHint()),
            context.expression() is { } expression ? BuildExpression(expression) : null,
            false,
            FluidScriptFrontEnd.Span(context));

    private VariableNode? BuildConstant(FluidScriptParser.ConstantDeclarationContext context) =>
        new(
            context.IDENTIFIER().GetText(),
            TypeName(context.typeHint()),
            BuildExpression(context.expression()),
            true,
            FluidScriptFrontEnd.Span(context));

    private AssignmentNode? BuildAssignment(FluidScriptParser.AssignmentStatementContext context)
    {
        var assignable = context.assignable();
        ExpressionNode target = new NameNode(assignable.IDENTIFIER().GetText(), FluidScriptFrontEnd.Span(assignable.IDENTIFIER().Symbol));
        foreach (var part in assignable.assignablePart())
        {
            if (part.DOT() is not null)
            {
                if (part.IDENTIFIER() is not { } member)
                    return null;
                target = new MemberNode(target, member.GetText(), FluidScriptFrontEnd.Span(part));
                continue;
            }
            if (part.expression() is not { } index || BuildExpression(index) is not { } indexExpression)
                return null;
            target = new IndexNode(target, indexExpression, FluidScriptFrontEnd.Span(part));
        }

        return BuildExpression(context.expression()) is { } value
            ? new AssignmentNode(target, context.assignmentOperator().GetText(), value, FluidScriptFrontEnd.Span(context))
            : null;
    }

    private IfNode? BuildIf(FluidScriptParser.IfStatementContext context)
    {
        var condition = BuildExpression(context.expression());
        var blocks = context.block();
        return condition is null || blocks.Length == 0
            ? null
            : new IfNode(
                condition,
                BuildBlock(blocks[0]),
                blocks.Length > 1 ? BuildBlock(blocks[1]) : null,
                FluidScriptFrontEnd.Span(context));
    }

    private WhileNode? BuildWhile(FluidScriptParser.WhileStatementContext context)
    {
        var condition = BuildExpression(context.expression());
        return condition is null
            ? null
            : new WhileNode(condition, BuildBlock(context.block()), FluidScriptFrontEnd.Span(context));
    }

    private ForNode? BuildFor(FluidScriptParser.ForStatementContext context)
    {
        var expressions = context.expression();
        if (expressions.Length < 2)
            return null;
        var start = BuildExpression(expressions[0]);
        var end = BuildExpression(expressions[1]);
        var step = expressions.Length > 2 ? BuildExpression(expressions[2]) : null;
        return start is null || end is null
            ? null
            : new ForNode(
                context.IDENTIFIER().GetText(),
                start,
                end,
                step,
                BuildBlock(context.block()),
                FluidScriptFrontEnd.Span(context));
    }

    private SwitchNode? BuildSwitch(FluidScriptParser.SwitchStatementContext context)
    {
        var value = BuildExpression(context.expression());
        if (value is null)
            return null;

        var cases = context.switchCase()
            .Select(item =>
            {
                var caseValue = BuildExpression(item.expression());
                return caseValue is null
                    ? null
                    : new SwitchCaseNode(caseValue, BuildBlock(item.block()), FluidScriptFrontEnd.Span(item));
            })
            .Where(item => item is not null)
            .Cast<SwitchCaseNode>()
            .ToArray();
        var otherwise = context.otherwiseCase() is { } otherwiseCase
            ? BuildBlock(otherwiseCase.block())
            : null;
        return new SwitchNode(value, cases, otherwise, FluidScriptFrontEnd.Span(context));
    }

    private FunctionNode? BuildFunction(FluidScriptParser.FunctionDeclarationContext context)
    {
        var parameters = context.parameterList()?.parameter()
            .Select(parameter => new ParameterNode(
                parameter.IDENTIFIER().GetText(),
                TypeName(parameter.typeHint()),
                parameter.expression() is { } expression ? BuildExpression(expression) : null,
                FluidScriptFrontEnd.Span(parameter)))
            .ToArray() ?? Array.Empty<ParameterNode>();

        return new FunctionNode(
            context.IDENTIFIER().GetText(),
            parameters,
            TypeName(context.typeHint()),
            BuildBlock(context.block()),
            FluidScriptFrontEnd.Span(context));
    }

    private TypeNode BuildType(FluidScriptParser.TypeDeclarationContext context)
    {
        var fields = new List<VariableNode>();
        var methods = new List<FunctionNode>();
        foreach (var member in context.typeBlock().typeMember())
        {
            if (member.variableDeclaration() is { } variable)
                fields.Add(BuildVariable(variable)!);
            else if (member.constantDeclaration() is { } constant)
            {
                var value = BuildConstant(constant);
                if (value is not null)
                    fields.Add(value);
            }
            else if (member.functionDeclaration() is { } function)
            {
                var method = BuildFunction(function);
                if (method is not null)
                    methods.Add(method);
            }
        }
        return new TypeNode(context.IDENTIFIER().GetText(), fields, methods, FluidScriptFrontEnd.Span(context));
    }

    private ReturnNode? BuildReturn(FluidScriptParser.ReturnStatementContext context) =>
        new(
            context.expression() is { } expression ? BuildExpression(expression) : null,
            FluidScriptFrontEnd.Span(context));

    private BlockNode BuildBlock(FluidScriptParser.BlockContext context)
    {
        var statements = context.statement()
            .Select(BuildStatement)
            .Where(statement => statement is not null)
            .Cast<StatementNode>()
            .ToArray();
        return new BlockNode(statements, FluidScriptFrontEnd.Span(context));
    }

    private ExpressionNode? BuildExpression(FluidScriptParser.ExpressionContext context)
    {
        if (context.lambdaExpression() is { } lambda)
            return BuildLambda(lambda);
        return context.logicalOrExpression() is { } logicalOr
            ? BuildLogicalOr(logicalOr)
            : null;
    }

    private LambdaNode? BuildLambda(FluidScriptParser.LambdaExpressionContext context)
    {
        var parametersContext = context.lambdaParameters();
        var parameters = parametersContext.IDENTIFIER() is { } single
            ? new[] { new ParameterNode(single.GetText(), null, null, FluidScriptFrontEnd.Span(single.Symbol)) }
            : parametersContext.lambdaParameterList()?.lambdaParameter()
                .Select(parameter => new ParameterNode(parameter.IDENTIFIER().GetText(), TypeName(parameter.typeHint()), null, FluidScriptFrontEnd.Span(parameter)))
                .ToArray() ?? Array.Empty<ParameterNode>();
        var body = context.lambdaBody();
        var expressionBody = body.logicalOrExpression() is { } expression
            ? BuildLogicalOr(expression)
            : null;
        var blockBody = body.block() is { } block ? BuildBlock(block) : null;
        if (expressionBody is null && blockBody is null)
        {
            AddUnsupported(context, "The lambda body is not valid.");
            return null;
        }
        return new LambdaNode(parameters, TypeName(context.typeHint()), expressionBody, blockBody, FluidScriptFrontEnd.Span(context));
    }

    private ExpressionNode? BuildLogicalOr(FluidScriptParser.LogicalOrExpressionContext context)
    {
        var values = context.logicalAndExpression().Select(BuildLogicalAnd).ToArray();
        return FoldBinary(values, context.OR(), "||", context);
    }

    private ExpressionNode? BuildLogicalAnd(FluidScriptParser.LogicalAndExpressionContext context)
    {
        var values = context.equalityExpression().Select(BuildEquality).ToArray();
        return FoldBinary(values, context.AND(), "&&", context);
    }

    private ExpressionNode? BuildEquality(FluidScriptParser.EqualityExpressionContext context)
    {
        var values = context.comparisonExpression().Select(BuildComparison).ToArray();
        var operators = context.EQUAL().Cast<ITerminalNode>().Concat(context.NOT_EQUAL()).ToArray();
        return FoldBinary(values, operators, operators.Select(token => token.GetText()).ToArray(), context);
    }

    private ExpressionNode? BuildComparison(FluidScriptParser.ComparisonExpressionContext context)
    {
        var values = context.additiveExpression().Select(BuildAdditive).ToArray();
        var operators = context.LT().Cast<ITerminalNode>()
            .Concat(context.LTE())
            .Concat(context.GT())
            .Concat(context.GTE())
            .OrderBy(token => token.Symbol.TokenIndex)
            .ToArray();
        return FoldBinary(values, operators, operators.Select(token => token.GetText()).ToArray(), context);
    }

    private ExpressionNode? BuildAdditive(FluidScriptParser.AdditiveExpressionContext context)
    {
        var values = context.multiplicativeExpression().Select(BuildMultiplicative).ToArray();
        var operators = context.PLUS().Cast<ITerminalNode>()
            .Concat(context.MINUS())
            .OrderBy(token => token.Symbol.TokenIndex)
            .ToArray();
        return FoldBinary(values, operators, operators.Select(token => token.GetText()).ToArray(), context);
    }

    private ExpressionNode? BuildMultiplicative(FluidScriptParser.MultiplicativeExpressionContext context)
    {
        var values = context.unaryExpression().Select(BuildUnary).ToArray();
        var operators = context.MULTIPLY().Cast<ITerminalNode>()
            .Concat(context.DIVIDE())
            .Concat(context.MODULO())
            .OrderBy(token => token.Symbol.TokenIndex)
            .ToArray();
        return FoldBinary(values, operators, operators.Select(token => token.GetText()).ToArray(), context);
    }

    private ExpressionNode? BuildUnary(FluidScriptParser.UnaryExpressionContext context)
    {
        if (context.unaryExpression() is { } operand)
            return BuildUnary(operand) is { } inner
                ? new UnaryNode(context.Start.Text, inner, FluidScriptFrontEnd.Span(context))
                : null;
        return context.postfixExpression() is { } postfix ? BuildPostfix(postfix) : null;
    }

    private ExpressionNode? BuildPostfix(FluidScriptParser.PostfixExpressionContext context)
    {
        var expression = BuildPrimary(context.primaryExpression());
        if (expression is null)
            return null;

        foreach (var part in context.postfixPart())
        {
            if (part.LPAREN() is not null)
            {
                var values = (part.argumentList()?.argument() ?? Array.Empty<FluidScriptParser.ArgumentContext>())
                    .Select(BuildArgument)
                    .Where(argument => argument is not null)
                    .Cast<ArgumentNode>()
                    .ToArray();
                expression = new CallNode(expression, values, FluidScriptFrontEnd.Span(part));
            }
            else if (part.LBRACK() is not null && part.expression() is { } index)
            {
                expression = BuildExpression(index) is { } indexExpression
                    ? new IndexNode(expression, indexExpression, FluidScriptFrontEnd.Span(part))
                    : null;
                if (expression is null)
                    return null;
            }
            else
            {
                var member = part.IDENTIFIER();
                if (member is null)
                    return null;
                expression = new MemberNode(expression, member.GetText(), FluidScriptFrontEnd.Span(part));
            }
        }

        return expression;
    }

    private ArgumentNode? BuildArgument(FluidScriptParser.ArgumentContext context)
    {
        var value = BuildExpression(context.expression());
        return value is null
            ? null
            : new ArgumentNode(
                context.ASSIGN() is null ? null : context.IDENTIFIER().GetText(),
                value,
                FluidScriptFrontEnd.Span(context));
    }

    private ExpressionNode? BuildPrimary(FluidScriptParser.PrimaryExpressionContext context)
    {
        if (context.literal() is { } literal)
            return BuildLiteral(literal);
        if (context.IDENTIFIER() is { } identifier)
            return new NameNode(identifier.GetText(), FluidScriptFrontEnd.Span(identifier.Symbol));
        if (context.expression() is { } expression)
            return BuildExpression(expression);
        if (context.arrayLiteral() is { } array)
        {
            var elements = array.arrayElements()?.expression()
                .Select(BuildExpression)
                .Where(element => element is not null)
                .Cast<ExpressionNode>()
                .ToArray() ?? Array.Empty<ExpressionNode>();
            return new ArrayNode(elements, FluidScriptFrontEnd.Span(array));
        }
        if (context.dictionaryLiteral() is { } dictionary)
        {
            var entries = dictionary.dictionaryEntries()?.dictionaryEntry()
                .Select(BuildDictionaryEntry)
                .Where(entry => entry is not null)
                .Cast<DictionaryEntryNode>()
                .ToArray() ?? Array.Empty<DictionaryEntryNode>();
            return new DictionaryNode(entries, FluidScriptFrontEnd.Span(dictionary));
        }
        return null;
    }

    private DictionaryEntryNode? BuildDictionaryEntry(FluidScriptParser.DictionaryEntryContext context)
    {
        var expressions = context.expression();
        if (expressions.Length != 2 || BuildExpression(expressions[0]) is not { } key || BuildExpression(expressions[1]) is not { } value)
            return null;
        return new DictionaryEntryNode(key, value, FluidScriptFrontEnd.Span(context));
    }

    private ExpressionNode? BuildLiteral(FluidScriptParser.LiteralContext context)
    {
        var token = context.Start;
        try
        {
            return token.Type switch
            {
                FluidScriptParser.INTEGER => new LiteralNode(FluidValue.From(long.Parse(token.Text, System.Globalization.CultureInfo.InvariantCulture)), FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.DECIMAL => new LiteralNode(FluidValue.From(decimal.Parse(token.Text, System.Globalization.CultureInfo.InvariantCulture)), FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.STRING => BuildStringLiteral(context, token.Text),
                FluidScriptParser.TRUE => new LiteralNode(FluidValue.From(true), FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.FALSE => new LiteralNode(FluidValue.From(false), FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.NULL => new LiteralNode(FluidValue.Null, FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.DATETIME => new LiteralNode(FluidValue.From(DateTimeOffset.Parse(token.Text[1..^1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal)), FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.GUID => new LiteralNode(FluidValue.From(Guid.Parse(token.Text[1..^1])), FluidScriptFrontEnd.Span(context)),
                FluidScriptParser.BYTE => BuildByteLiteral(context, token.Text),
                _ => UnsupportedLiteral(context)
            };
        }
        catch (FormatException)
        {
            AddError("FS2002", "The literal is not valid.", context);
            return null;
        }
        catch (OverflowException)
        {
            AddError("FS2003", "The literal is outside the supported range.", context);
            return null;
        }
        catch (ArgumentException)
        {
            AddError("FS2002", "The literal is not valid.", context);
            return null;
        }
    }

    private LiteralNode? BuildByteLiteral(FluidScriptParser.LiteralContext context, string text)
    {
        if (!ulong.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            AddError("FS2002", "The literal is not valid.", context);
            return null;
        }
        if (value > byte.MaxValue)
        {
            AddError("FS2003", "The literal is outside the supported range.", context);
            return null;
        }
        return new LiteralNode(FluidValue.From((byte)value), FluidScriptFrontEnd.Span(context));
    }

    private ExpressionNode? BuildStringLiteral(FluidScriptParser.LiteralContext context, string token)
    {
        var text = Unescape(token);
        if (!text.Contains('{') && !text.Contains('}'))
            return new LiteralNode(FluidValue.From(text), FluidScriptFrontEnd.Span(context));

        var parts = new List<InterpolationPart>();
        var literal = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '{' && index + 1 < text.Length && text[index + 1] == '{')
            {
                literal.Append('{');
                index++;
                continue;
            }
            if (text[index] == '}' && index + 1 < text.Length && text[index + 1] == '}')
            {
                literal.Append('}');
                index++;
                continue;
            }
            if (text[index] == '}')
            {
                AddError("FS3002", "Unmatched closing interpolation brace.", context);
                return null;
            }
            if (text[index] != '{')
            {
                literal.Append(text[index]);
                continue;
            }

            if (literal.Length > 0)
            {
                parts.Add(new InterpolationPart(literal.ToString(), null));
                literal.Clear();
            }
            var expressionStart = ++index;
            var depth = 1;
            for (; index < text.Length && depth > 0; index++)
            {
                if (text[index] == '{')
                    depth++;
                else if (text[index] == '}')
                    depth--;
            }
            if (depth != 0)
            {
                AddError("FS3002", "Unmatched opening interpolation brace.", context);
                return null;
            }
            var expressionText = text[expressionStart..(index - 1)].Trim();
            if (expressionText.Length == 0)
            {
                AddError("FS3003", "Interpolation expression cannot be empty.", context);
                return null;
            }
            var nestedParse = FluidScriptFrontEnd.Parse(expressionText + "\n");
            if (!nestedParse.Success || nestedParse.Program is null)
            {
                diagnostics.AddRange(nestedParse.Diagnostics.Select(d => d with { Code = "FS3003", Message = "Interpolation expression is invalid." }));
                return null;
            }
            var nestedBuilder = new SyntaxTreeBuilder();
            var nestedScript = nestedBuilder.Build(nestedParse.Program);
            diagnostics.AddRange(nestedBuilder.Diagnostics.Select(d => d with { Code = "FS3003", Message = "Interpolation expression is invalid." }));
            var nestedExpression = nestedScript.Statements.OfType<ExpressionStatementNode>().FirstOrDefault()?.Expression;
            if (nestedExpression is null)
            {
                AddError("FS3003", "Interpolation expression is invalid.", context);
                return null;
            }
            parts.Add(new InterpolationPart(null, nestedExpression));
            index--;
        }
        if (literal.Length > 0)
            parts.Add(new InterpolationPart(literal.ToString(), null));
        return new InterpolatedStringNode(parts, FluidScriptFrontEnd.Span(context));
    }

    private ExpressionNode? UnsupportedLiteral(FluidScriptParser.LiteralContext context)
    {
        AddUnsupported(context, "This literal kind is not implemented yet.");
        return null;
    }

    private static string Unescape(string token)
    {
        var text = token[1..^1];
        var builder = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\\')
            {
                builder.Append(text[index]);
                continue;
            }

            if (++index >= text.Length)
                throw new FormatException();
            if (text[index] == 'u')
            {
                if (index + 4 >= text.Length ||
                    !int.TryParse(text.AsSpan(index + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
                    throw new FormatException();
                builder.Append((char)code);
                index += 4;
                continue;
            }

            builder.Append(text[index] switch
            {
                '"' => '"',
                '\\' => '\\',
                '/' => '/',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw new FormatException()
            });
        }
        return builder.ToString();
    }

    private ExpressionNode? FoldBinary<TContext>(
        ExpressionNode?[] values,
        IReadOnlyList<ITerminalNode> operators,
        string operatorText,
        TContext context)
        where TContext : ParserRuleContext =>
        FoldBinary(values, operators, Enumerable.Repeat(operatorText, operators.Count).ToArray(), context);

    private ExpressionNode? FoldBinary<TContext>(
        ExpressionNode?[] values,
        IReadOnlyList<ITerminalNode> operators,
        IReadOnlyList<string> operatorTexts,
        TContext context)
        where TContext : ParserRuleContext
    {
        if (values.Any(value => value is null))
            return null;
        var result = values[0]!;
        for (var index = 0; index < operators.Count; index++)
            result = new BinaryNode(result, operatorTexts[index], values[index + 1]!, FluidScriptFrontEnd.Span(context));
        return result;
    }

    private string? TypeName(FluidScriptParser.TypeHintContext? context) =>
        context?.typeReference()?.GetText();

    private void AddUnsupported(ParserRuleContext context, string message) =>
        AddError("FS3001", message, context);

    private void AddError(string code, string message, ParserRuleContext context) =>
        diagnostics.Add(new Diagnostic(code, message, FluidScriptFrontEnd.Span(context)));
}

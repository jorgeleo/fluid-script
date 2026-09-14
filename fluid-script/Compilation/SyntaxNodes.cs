using FluidScript.Parsing;
using FluidScript.Runtime;

namespace FluidScript.Compilation;

internal abstract record StatementNode(SourceSpan Span);
internal sealed record BlockNode(IReadOnlyList<StatementNode> Statements, SourceSpan Span);
internal sealed record VariableNode(string Name, string? TypeName, ExpressionNode? Initializer, bool IsConstant, SourceSpan Span) : StatementNode(Span);
internal sealed record AssignmentNode(ExpressionNode Target, string Operator, ExpressionNode Value, SourceSpan Span) : StatementNode(Span);
internal sealed record ExpressionStatementNode(ExpressionNode Expression, SourceSpan Span) : StatementNode(Span);
internal sealed record IfNode(ExpressionNode Condition, BlockNode Then, BlockNode? Else, SourceSpan Span) : StatementNode(Span);
internal sealed record WhileNode(ExpressionNode Condition, BlockNode Body, SourceSpan Span) : StatementNode(Span);
internal sealed record ForNode(string Name, ExpressionNode Start, ExpressionNode End, ExpressionNode? Step, BlockNode Body, SourceSpan Span) : StatementNode(Span);
internal sealed record SwitchNode(ExpressionNode Value, IReadOnlyList<SwitchCaseNode> Cases, BlockNode? Otherwise, SourceSpan Span) : StatementNode(Span);
internal sealed record SwitchCaseNode(ExpressionNode Value, BlockNode Body, SourceSpan Span);
internal sealed record FunctionNode(string Name, IReadOnlyList<ParameterNode> Parameters, string? ReturnType, BlockNode Body, SourceSpan Span) : StatementNode(Span);
internal sealed record TypeNode(string Name, IReadOnlyList<VariableNode> Fields, IReadOnlyList<FunctionNode> Methods, SourceSpan Span) : StatementNode(Span);
internal sealed record ReturnNode(ExpressionNode? Value, SourceSpan Span) : StatementNode(Span);
internal sealed record BreakNode(SourceSpan Span) : StatementNode(Span);
internal sealed record ContinueNode(SourceSpan Span) : StatementNode(Span);
internal sealed record ParameterNode(string Name, string? TypeName, ExpressionNode? DefaultValue, SourceSpan Span);
internal sealed record TryNode(BlockNode Body, CatchNode? Catch, BlockNode? Finally, SourceSpan Span) : StatementNode(Span);
internal sealed record CatchNode(string? Name, string? TypeName, BlockNode Body, SourceSpan Span);
internal sealed record ThrowNode(ExpressionNode Value, SourceSpan Span) : StatementNode(Span);
internal sealed record ImportNode(string Name, bool IsPath, SourceSpan Span) : StatementNode(Span);

internal abstract record ExpressionNode(SourceSpan Span);
internal sealed record LiteralNode(FluidValue Value, SourceSpan Span) : ExpressionNode(Span);
internal sealed record NameNode(string Name, SourceSpan Span) : ExpressionNode(Span);
internal sealed record UnaryNode(string Operator, ExpressionNode Operand, SourceSpan Span) : ExpressionNode(Span);
internal sealed record BinaryNode(ExpressionNode Left, string Operator, ExpressionNode Right, SourceSpan Span) : ExpressionNode(Span);
internal sealed record CallNode(ExpressionNode Target, IReadOnlyList<ArgumentNode> Arguments, SourceSpan Span) : ExpressionNode(Span);
internal sealed record ArgumentNode(string? Name, ExpressionNode Value, SourceSpan Span);
internal sealed record ArrayNode(IReadOnlyList<ExpressionNode> Elements, SourceSpan Span) : ExpressionNode(Span);
internal sealed record DictionaryNode(IReadOnlyList<DictionaryEntryNode> Entries, SourceSpan Span) : ExpressionNode(Span);
internal sealed record DictionaryEntryNode(ExpressionNode Key, ExpressionNode Value, SourceSpan Span);
internal sealed record IndexNode(ExpressionNode Target, ExpressionNode Index, SourceSpan Span) : ExpressionNode(Span);
internal sealed record MemberNode(ExpressionNode Target, string Name, SourceSpan Span) : ExpressionNode(Span);
internal sealed record InterpolatedStringNode(IReadOnlyList<InterpolationPart> Parts, SourceSpan Span) : ExpressionNode(Span);
internal sealed record InterpolationPart(string? Text, ExpressionNode? Expression);
internal sealed record LambdaNode(IReadOnlyList<ParameterNode> Parameters, string? ReturnType, ExpressionNode? ExpressionBody, BlockNode? BlockBody, SourceSpan Span) : ExpressionNode(Span);

internal sealed record ScriptNode(IReadOnlyList<StatementNode> Statements, SourceSpan Span);

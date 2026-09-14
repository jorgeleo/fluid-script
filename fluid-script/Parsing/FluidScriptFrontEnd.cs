using Antlr4.Runtime;
using FluidScript.Generated;

namespace FluidScript.Parsing;

public sealed record ParseResult(
    FluidScriptParser.ProgramContext? Program,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

public static class FluidScriptFrontEnd
{
    public static ParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<Diagnostic>();
        var input = CharStreams.fromString(source);
        var lexer = new FluidScriptLexer(input);
        var listener = new DiagnosticErrorListener(diagnostics);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new LexerDiagnosticErrorListener(diagnostics));

        var tokens = new CommonTokenStream(lexer);
        var parser = new FluidScriptParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(listener);
        var program = parser.program();

        return new ParseResult(program, diagnostics);
    }

    internal static SourceSpan Span(ParserRuleContext context)
    {
        var start = context.Start;
        var stop = context.Stop ?? start;
        var length = start is null || stop is null
            ? 0
            : Math.Max(0, stop.StopIndex - start.StartIndex + 1);

        return start is null
            ? SourceSpan.None
            : new SourceSpan(start.Line, start.Column, length);
    }

    internal static SourceSpan Span(IToken token) =>
        token is null ? SourceSpan.None : new SourceSpan(token.Line, token.Column, Math.Max(0, token.Text?.Length ?? 0));

    private sealed class DiagnosticErrorListener(ICollection<Diagnostic> diagnostics) : BaseErrorListener
    {
        public override void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            IToken offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            var length = offendingSymbol?.Text?.Length ?? 1;
            diagnostics.Add(new Diagnostic(
                "FS1001",
                msg,
                new SourceSpan(line, charPositionInLine, length)));
        }
    }

    private sealed class LexerDiagnosticErrorListener(ICollection<Diagnostic> diagnostics) : IAntlrErrorListener<int>
    {
        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            int offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            diagnostics.Add(new Diagnostic(
                "FS1001",
                msg,
                new SourceSpan(line, charPositionInLine, 1)));
        }
    }
}

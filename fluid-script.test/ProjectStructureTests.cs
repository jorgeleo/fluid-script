using FluidScript;
using FluidScript.Generated;
using Antlr4.Runtime;

namespace FluidScript.Test;

[TestClass]
public sealed class ProjectStructureTests
{
    [TestMethod]
    public void Library_uses_the_expected_assembly_name()
    {
        Assert.AreEqual("FluidScript", typeof(FluidScriptAssembly).Assembly.GetName().Name);
    }

    [TestMethod]
    public void Grammar_generates_a_parser_that_accepts_a_minimal_program()
    {
        var lexer = new FluidScriptLexer(CharStreams.fromString("dim value = 1\n"));
        var parser = new FluidScriptParser(new CommonTokenStream(lexer));

        parser.program();

        Assert.AreEqual(0, parser.NumberOfSyntaxErrors);
    }
}

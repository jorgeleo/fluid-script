using FluidScript.Parsing;

namespace FluidScript.Test;

[TestClass]
public sealed class ParsingTests
{
    [TestMethod]
    public void Front_end_accepts_comments_separators_and_crlf_source()
    {
        var result = FluidScriptFrontEnd.Parse("// header\r\ndim value = 1; /* between */ print(value)\r\n");

        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        Assert.IsEmpty(result.Diagnostics);
    }

    [TestMethod]
    public void Front_end_accepts_dictionary_literals_with_computed_keys()
    {
        var result = FluidScriptFrontEnd.Parse("dim prefix = \"user.\"\ndim values = { prefix + \"name\": \"Ada\", \"age\": 40 }\n");

        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
    }

    [TestMethod]
    public void Front_end_reports_syntax_errors_with_stable_source_spans()
    {
        var result = FluidScriptFrontEnd.Parse("dim value = 1 dim other = 2\n");

        Assert.IsFalse(result.Success);
        var diagnostic = result.Diagnostics.Single(d => d.Code == "FS1001");
        Assert.AreEqual(1, diagnostic.Span.Line);
        Assert.IsGreaterThan(0, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Front_end_reports_lexer_errors_for_unterminated_strings()
    {
        var result = FluidScriptFrontEnd.Parse("print(\"unterminated\n");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS1001"));
    }
}

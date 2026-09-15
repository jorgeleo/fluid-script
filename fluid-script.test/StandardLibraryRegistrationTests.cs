using FluidScript.Compilation;
using FluidScript.Runtime;

namespace FluidScript.Test;

[TestClass]
public sealed class StandardLibraryRegistrationTests
{
    [TestMethod]
    public void Vm_registers_the_four_standard_library_groups()
    {
        var registrations = new IRegister[]
        {
            new StringLibrary(),
            new RegExpLibrary(),
            new NumberLibrary(),
            new MathLibrary()
        };
        var host = new FluidScriptHost();
        foreach (var registration in registrations)
            registration.Register(host);

        var wrappedRegex = host.Wrap(new FluidRegExp("a"));
        Assert.AreEqual(FluidValueKind.HostObject, wrappedRegex.Kind);
        Assert.IsInstanceOfType<FluidRegExp>(wrappedRegex.AsHostObject().Instance);

        var result = FluidScriptCompiler.Compile("print(String(12).toString())\n", host);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();
        result.Execute(new FluidScriptExecutionContext(host, output: output.Add));
        CollectionAssert.AreEqual(new[] { "12" }, output);
    }

    [TestMethod]
    public void Vm_registers_standard_libraries_for_direct_module_execution()
    {
        var result = FluidScriptCompiler.Compile("print(Math.PI > 3)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();
        new VirtualMachine().Run(result.Module!, output.Add);
        CollectionAssert.AreEqual(new[] { "true" }, output);
    }

    [TestMethod]
    public void Standard_libraries_support_string_regex_number_and_math_calls()
    {
        var result = FluidScriptCompiler.Compile("""
            dim text: string = "  Hello world  "
            print(text.trim().toUpperCase())
            print(text.length)
            dim pattern: RegExp = RegExp("(a)(b)", "g")
            print(pattern.test("ab ab"))
            print(pattern.lastIndex)
            print(Number.parseInt("ff", 16))
            print(Number(12.5).toFixed(1))
            print(Number.MAX_SAFE_INTEGER)
            print(Math.max(2, 9, 4))
            print(Math.PI > 3)
            print(Math.sqrt(9))
            """);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();
        result.Execute(output.Add);
        CollectionAssert.AreEqual(new[] { "HELLO WORLD", "15", "true", "2", "255", "12.5", "9007199254740991", "9", "true", "3" }, output);
    }

    [TestMethod]
    public void Every_string_registration_is_callable()
    {
        var output = Run("""
            dim text: string = "  Abc Abc  "
            String("text")
            String.fromCharCode(65, 66)
            String.fromCodePoint(128512)
            String.raw("raw")
            text.length
            text.at(1)
            text.charAt(1)
            text.charCodeAt(1)
            text.codePointAt(1)
            text.concat("!")
            text.endsWith("  ")
            text.includes("Abc")
            text.indexOf("Abc")
            text.lastIndexOf("Abc")
            text.localeCompare(text)
            text.padEnd(15, ".")
            text.padStart(15, ".")
            text.repeat(2)
            text.replace("Abc", "X")
            text.replaceAll("Abc", "X")
            text.search(RegExp("Abc"))
            text.slice(2, 5)
            text.split(" ")
            text.startsWith("  ")
            text.substr(2, 3)
            text.substring(2, 5)
            text.toLowerCase()
            text.toUpperCase()
            text.trim()
            text.trimStart()
            text.trimEnd()
            text.trimLeft()
            text.trimRight()
            text.normalize("NFC")
            text.isWellFormed()
            text.toWellFormed()
            text.match(RegExp("Abc"))
            text.matchAll(RegExp("Abc", "g"))
            text.valueOf()
            text.toString()
            print("ok")
            """);

        CollectionAssert.AreEqual(new[] { "ok" }, output);
    }

    [TestMethod]
    public void Every_regexp_registration_is_callable()
    {
        var output = Run("""
            dim pattern: RegExp = RegExp("a", "dgimsuvy")
            RegExp.escape("a+b")
            pattern.source
            pattern.flags
            pattern.global
            pattern.ignoreCase
            pattern.multiline
            pattern.dotAll
            pattern.unicode
            pattern.sticky
            pattern.lastIndex = 0
            pattern.lastIndex
            pattern.test("a")
            pattern.exec("a")
            pattern.toString()
            print("ok")
            """);

        CollectionAssert.AreEqual(new[] { "ok" }, output);
    }

    [TestMethod]
    public void Every_number_registration_is_callable()
    {
        var output = Run("""
            dim number = Number("12.5")
            Number(12)
            Number.isFinite(12)
            Number.isInteger(12)
            Number.isNaN("not a number")
            Number.isSafeInteger(12)
            Number.parseFloat("3.14 trailing")
            Number.parseInt("ff", 16)
            Number.EPSILON
            Number.MAX_SAFE_INTEGER
            Number.MIN_SAFE_INTEGER
            number.toFixed(1)
            number.toString()
            number.valueOf()
            dim integer: int = 12
            integer.toFixed(0)
            integer.toString(16)
            integer.valueOf()
            print("ok")
            """);

        CollectionAssert.AreEqual(new[] { "ok" }, output);
    }

    [TestMethod]
    public void Every_math_registration_is_callable()
    {
        var output = Run("""
            Math.E
            Math.LN2
            Math.LN10
            Math.LOG2E
            Math.LOG10E
            Math.PI
            Math.SQRT1_2
            Math.SQRT2
            Math.abs(-1)
            Math.ceil(1.1)
            Math.floor(1.9)
            Math.max(1, 2)
            Math.min(1, 2)
            Math.pow(2, 3)
            dim randomValue = Math.random()
            Math.round(-1.5)
            Math.sign(-1)
            Math.sqrt(4)
            Math.trunc(1.9)
            Math.cbrt(8)
            Math.exp(0)
            Math.log(1)
            Math.log10(1)
            Math.log2(1)
            Math.sin(0)
            Math.cos(0)
            Math.tan(0)
            Math.asin(0)
            Math.acos(1)
            Math.atan(0)
            Math.atan2(0, 1)
            Math.hypot(3, 4)
            Math.imul(2, 3)
            Math.clz32(1)
            Math.fround(1.5)
            print(randomValue >= 0 && randomValue <= 1)
            """);

        CollectionAssert.AreEqual(new[] { "true" }, output);
    }

    [TestMethod]
    public void Standard_library_failures_are_reported_as_runtime_faults()
    {
        var result = FluidScriptCompiler.Compile("dim pattern = RegExp(\"[\", \"\")\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());
        Assert.AreEqual("FS5018", fault.Code);
    }

    private static List<string> Run(string source)
    {
        var result = FluidScriptCompiler.Compile(source);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();
        result.Execute(output.Add);
        return output;
    }
}

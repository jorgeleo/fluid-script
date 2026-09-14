using FluidScript.Compilation;
using FluidScript.Runtime;

namespace FluidScript.Test;

[TestClass]
public sealed class CompilerRuntimeTests
{
    [TestMethod]
    public void Arithmetic_respects_precedence_and_prints_the_result()
    {
        var output = Run("print(2 + 3 * 4)\n");

        CollectionAssert.AreEqual(new[] { "14" }, output);
    }

    [TestMethod]
    public void If_and_while_update_a_global_variable()
    {
        var output = Run("""
            dim value = 0
            if true
                value += 1
            end
            while value < 3
                value += 1
            end
            print(value)
            """);

        CollectionAssert.AreEqual(new[] { "3" }, output);
    }

    [TestMethod]
    public void Functions_support_parameters_recursion_and_forward_calls()
    {
        var output = Run("""
            print(add(2, 3))
            function add(a: int, b: int): int
                return a + b
            end
            """);

        CollectionAssert.AreEqual(new[] { "5" }, output);
    }

    [TestMethod]
    public void Functions_support_named_arguments_and_literal_defaults()
    {
        var output = Run("function greet(name: string, greeting: string = \"Hello\"): string\n    return greeting + \" \" + name\nend\nprint(greet(name = \"Ada\"))\nprint(greet(\"Lin\", \"Hi\"))\n");

        CollectionAssert.AreEqual(new[] { "Hello Ada", "Hi Lin" }, output);
    }

    [TestMethod]
    public void Calls_report_unknown_duplicate_and_missing_arguments()
    {
        var result = FluidScriptCompiler.Compile("function add(a: int, b: int): int\n    return a + b\nend\nprint(add(a = 1, a = 2, c = 3))\n");

        Assert.IsFalse(result.Success);
        CollectionAssert.AreEquivalent(new[] { "FS2106", "FS2108", "FS2109" }, result.Diagnostics.Select(d => d.Code).ToArray());
    }

    [TestMethod]
    public void Expression_lambdas_can_be_stored_and_called()
    {
        var output = Run("dim double = x => x * 2\nprint(double(6))\n");

        CollectionAssert.AreEqual(new[] { "12" }, output);
    }

    [TestMethod]
    public void Lambdas_capture_local_cells_by_reference_after_the_creator_returns()
    {
        var output = Run("function makeCounter(): any\n    dim value = 0\n    return () =>\n        value += 1\n        return value\n    end\nend\ndim next = makeCounter()\nprint(next())\nprint(next())\n");

        CollectionAssert.AreEqual(new[] { "1", "2" }, output);
    }

    [TestMethod]
    public void Function_type_hints_accept_compatible_lambdas()
    {
        var output = Run("dim double: int => int = x => x * 2\nprint(double(7))\n");

        CollectionAssert.AreEqual(new[] { "14" }, output);
    }

    [TestMethod]
    public void Function_type_hints_reject_unknown_type_shapes()
    {
        var result = FluidScriptCompiler.Compile("dim operation: missing => int = x => x\n");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS2100"));
    }

    [TestMethod]
    public void Calling_a_non_callable_value_is_a_runtime_fault()
    {
        var result = FluidScriptCompiler.Compile("dim value = 3\nprint(value())\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());

        Assert.AreEqual("FS5007", fault.Code);
    }

    [TestMethod]
    public void Return_outside_a_function_is_a_compile_error()
    {
        var result = FluidScriptCompiler.Compile("return 1\n");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS2104"));
    }

    [TestMethod]
    public void Try_catch_finally_handles_explicit_and_runtime_faults()
    {
        var output = Run("try\n    throw \"oops\"\ncatch error\n    print(error)\nfinally\n    print(\"done\")\nend\ntry\n    print(1 / 0)\ncatch error\n    print(\"caught\")\nend\n");

        CollectionAssert.AreEqual(new[] { "oops", "done", "caught" }, output);
    }

    [TestMethod]
    public void Unhandled_throw_preserves_the_runtime_fault_kind()
    {
        var result = FluidScriptCompiler.Compile("throw \"stop\"\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());

        Assert.AreEqual("FS5001", fault.Code);
        Assert.AreEqual(1, fault.Span.Line);
    }

    [TestMethod]
    public void Finally_runs_before_an_uncaught_fault_is_rethrown()
    {
        var result = FluidScriptCompiler.Compile("try\n    print(1 / 0)\nfinally\n    print(\"cleanup\")\nend\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();

        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute(output.Add));

        CollectionAssert.AreEqual(new[] { "cleanup" }, output);
        Assert.AreEqual("FS5003", fault.Code);
    }

    [TestMethod]
    public void Typed_catches_filter_thrown_values()
    {
        var matched = Run("try\n    throw \"stop\"\ncatch error: string\n    print(error)\nend\n");
        CollectionAssert.AreEqual(new[] { "stop" }, matched);

        var result = FluidScriptCompiler.Compile("try\n    throw 1\ncatch error: string\n    print(error)\nend\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());
        Assert.AreEqual("FS5001", fault.Code);
    }

    [TestMethod]
    public void Imports_use_the_explicit_resolver_boundary()
    {
        var noResolver = FluidScriptCompiler.Compile("import \"missing\"\n");
        Assert.IsFalse(noResolver.Success);
        Assert.IsTrue(noResolver.Diagnostics.Any(d => d.Code == "FS2301"));

        var missing = FluidScriptCompiler.Compile("import library.module\n", new TestModuleResolver());
        Assert.IsFalse(missing.Success);
        Assert.IsTrue(missing.Diagnostics.Any(d => d.Code == "FS2302"));

        var cycle = FluidScriptCompiler.Compile("import \"a\"\n", new DictionaryModuleResolver(new Dictionary<string, string>
        {
            ["a"] = "import \"a\"\n"
        }));
        Assert.IsFalse(cycle.Success);
        Assert.IsTrue(cycle.Diagnostics.Any(d => d.Code == "FS2303"));
    }

    [TestMethod]
    public void Recursive_functions_can_call_themselves()
    {
        var output = Run("""
            function factorial(value: int): int
                if value <= 1
                    return 1
                end
                return value * factorial(value - 1)
            end
            print(factorial(5))
            """);

        CollectionAssert.AreEqual(new[] { "120" }, output);
    }

    [TestMethod]
    public void Logical_operators_short_circuit_and_return_boolean_values()
    {
        var output = Run("""
            print(false && (10 / 0 == 0))
            print(true || (10 / 0 == 0))
            """);

        CollectionAssert.AreEqual(new[] { "false", "true" }, output);
    }

    [TestMethod]
    public void Break_and_continue_control_the_nearest_loop()
    {
        var output = Run("""
            dim value = 0
            while value < 5
                value += 1
                if value == 2
                    continue
                end
                if value == 4
                    break
                end
                print(value)
            end
            """);

        CollectionAssert.AreEqual(new[] { "1", "3" }, output);
    }

    [TestMethod]
    public void String_escapes_are_decoded_before_execution()
    {
        var output = Run("print(\"line\\n\\u0041\")\n");

        CollectionAssert.AreEqual(new[] { "line\nA" }, output);
    }

    [TestMethod]
    public void String_interpolation_evaluates_names_and_expressions()
    {
        var output = Run("dim name = \"Ada\"\ndim count = 2\nprint(\"Hello {name}: {count + 1}\")\nprint(\"{{literal}}\")\n");

        CollectionAssert.AreEqual(new[] { "Hello Ada: 3", "{literal}" }, output);
    }

    [TestMethod]
    public void String_interpolation_rejects_unmatched_or_empty_expressions()
    {
        var unmatched = FluidScriptCompiler.Compile("print(\"Hello {name\")\n");
        Assert.IsFalse(unmatched.Success);
        Assert.IsTrue(unmatched.Diagnostics.Any(d => d.Code == "FS3002"));

        var empty = FluidScriptCompiler.Compile("print(\"Hello {}\")\n");
        Assert.IsFalse(empty.Success);
        Assert.IsTrue(empty.Diagnostics.Any(d => d.Code == "FS3003"));
    }

    [TestMethod]
    public void DateTime_guid_and_byte_literals_round_trip_through_the_vm()
    {
        var output = Run("print(#2024-01-02#)\nprint({00112233-4455-6677-8899-aabbccddeeff})\nprint(0x2A)\n");

        CollectionAssert.AreEqual(new[]
        {
            "2024-01-02T00:00:00.0000000+00:00",
            "00112233-4455-6677-8899-aabbccddeeff",
            "0x2A"
        }, output);
    }

    [TestMethod]
    public void Byte_literals_outside_the_byte_range_are_compile_errors()
    {
        var result = FluidScriptCompiler.Compile("print(0x100)\n");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS2003"));
    }

    [TestMethod]
    public void Type_hints_accept_compatible_initializers()
    {
        var output = Run("dim count: int = 2\ndim ratio: decimal = count + 0.5\nprint(ratio)\n");

        CollectionAssert.AreEqual(new[] { "2.5" }, output);
    }

    [TestMethod]
    public void Type_hints_reject_unknown_types_and_incompatible_initializers()
    {
        var result = FluidScriptCompiler.Compile("dim count: int = \"two\"\ndim value: missing = 1\n");

        Assert.IsFalse(result.Success);
        CollectionAssert.AreEquivalent(new[] { "FS2100", "FS2101", "FS2101" }, result.Diagnostics.Select(d => d.Code).ToArray());
    }

    [TestMethod]
    public void Disassembler_lists_constants_functions_and_source_locations()
    {
        var result = FluidScriptCompiler.Compile("print(2 + 3)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var listing = PCodeDisassembler.Disassemble(result.Module!);

        StringAssert.Contains(listing, "function 0 __main/0");
        StringAssert.Contains(listing, "Const");
        StringAssert.Contains(listing, "; 1:7");
    }

    [TestMethod]
    public void For_loop_supports_ascending_and_descending_ranges()
    {
        var output = Run("""
            for i = 1 to 3
                print(i)
            end
            for j = 3 to 1 step -1
                print(j)
            end
            """);

        CollectionAssert.AreEqual(new[] { "1", "2", "3", "3", "2", "1" }, output);
    }

    [TestMethod]
    public void Switch_runs_one_matching_case_without_fallthrough()
    {
        var output = Run("""
            switch 2
                case 1
                    print(1)
                case 2
                    print(2)
                otherwise
                    print(3)
            end
            """);

        CollectionAssert.AreEqual(new[] { "2" }, output);
    }

    [TestMethod]
    public void Switch_uses_otherwise_when_no_case_matches()
    {
        var output = Run("""
            switch 9
                case 1
                    print(1)
                otherwise
                    print(0)
            end
            """);

        CollectionAssert.AreEqual(new[] { "0" }, output);
    }

    [TestMethod]
    public void Unknown_names_are_reported_as_compile_errors()
    {
        var result = FluidScriptCompiler.Compile("print(missing)\n");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS2001"));
    }

    [TestMethod]
    public void Constant_assignment_is_reported_as_a_compile_error()
    {
        var result = FluidScriptCompiler.Compile("""
            const answer = 42
            answer = 7
            """);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS2008"));
    }

    [TestMethod]
    public void Array_literals_and_indexing_execute()
    {
        var output = Run("print([[1, 2], [3, 4]][1][0])\n");

        CollectionAssert.AreEqual(new[] { "3" }, output);
    }

    [TestMethod]
    public void Array_index_assignment_and_compound_assignment_mutate_once()
    {
        var output = Run("dim values = [1, 2]\nvalues[0] += 4\nvalues[1] = values[0] * 2\nprint(values[0])\nprint(values[1])\n");

        CollectionAssert.AreEqual(new[] { "5", "10" }, output);
    }

    [TestMethod]
    public void Types_construct_objects_and_support_field_reads_and_writes()
    {
        var output = Run("type Person\n    dim name: string\n    dim age: int = 1\n    const kind: string = \"person\"\nend\ndim person = Person(\"Ada\", 40)\nperson.age += 1\nprint(person.name)\nprint(person.age)\nprint(person.kind)\n");

        CollectionAssert.AreEqual(new[] { "Ada", "41", "person" }, output);
    }

    [TestMethod]
    public void Type_methods_receive_self_and_can_read_and_mutate_fields()
    {
        var output = Run("type Counter\n    dim value: int\n    function increment(amount: int): int\n        value += amount\n        return value\n    end\nend\ndim counter = Counter(1)\nprint(counter.increment(4))\nprint(counter.value)\n");

        CollectionAssert.AreEqual(new[] { "5", "5" }, output);
    }

    [TestMethod]
    public void Object_member_errors_are_reported_or_faulted_stably()
    {
        var compileResult = FluidScriptCompiler.Compile("type Person\n    dim name: string\nend\ndim person = Person(\"Ada\")\nprint(person.missing)\n");
        Assert.IsFalse(compileResult.Success);
        Assert.IsTrue(compileResult.Diagnostics.Any(d => d.Code == "FS2202"));

        var runtimeResult = FluidScriptCompiler.Compile("type Person\n    const kind: string = \"person\"\nend\ndim person = Person()\nperson.kind = \"other\"\n");
        Assert.IsFalse(runtimeResult.Success);
        Assert.IsTrue(runtimeResult.Diagnostics.Any(d => d.Code == "FS2203"));
    }

    [TestMethod]
    public void Invalid_array_index_is_a_runtime_fault()
    {
        var result = FluidScriptCompiler.Compile("print([1][2])\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());

        Assert.AreEqual("FS5012", fault.Code);
    }

    [TestMethod]
    public void Break_outside_a_loop_is_reported_as_a_compile_error()
    {
        var result = FluidScriptCompiler.Compile("break\n");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "FS2011"));
    }

    [TestMethod]
    public void Non_boolean_conditions_are_reported_as_runtime_faults()
    {
        var result = FluidScriptCompiler.Compile("if 1\n    print(1)\nend\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());

        Assert.AreEqual("FS5002", fault.Code);
    }

    [TestMethod]
    public void Division_by_zero_is_a_source_mapped_runtime_fault()
    {
        var result = FluidScriptCompiler.Compile("print(10 / 0)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var fault = Assert.Throws<RuntimeFaultException>(() => result.Execute());

        Assert.AreEqual("FS5003", fault.Code);
        Assert.AreEqual(1, fault.Span.Line);
    }

    private static List<string> Run(string source)
    {
        var result = FluidScriptCompiler.Compile(source);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();
        result.Execute(output.Add);
        return output;
    }

    private sealed class TestModuleResolver : IFluidModuleResolver
    {
        public bool TryResolve(string moduleName, out string source)
        {
            source = string.Empty;
            return false;
        }
    }

    private sealed class DictionaryModuleResolver(IReadOnlyDictionary<string, string> modules) : IFluidModuleResolver
    {
        public bool TryResolve(string moduleName, out string source) => modules.TryGetValue(moduleName, out source!);
    }
}

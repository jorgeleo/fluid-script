using FluidScript.Runtime;
using FluidScript.Compilation;
using FluidScript.Parsing;

namespace FluidScript.Test;

[TestClass]
public sealed class PCodeVerifierTests
{
    [TestMethod]
    public void Verifier_rejects_invalid_constants_and_stack_underflow()
    {
        var module = new PCodeModule(
            Array.Empty<FluidValue>(),
            new[]
            {
                new PCodeFunction("__main", 0, 0, new[]
                {
                    new Instruction(OpCode.Const, 4),
                    new Instruction(OpCode.Pop),
                    new Instruction(OpCode.Halt)
                })
            });

        var diagnostics = PCodeVerifier.Verify(module);

        CollectionAssert.AreEquivalent(new[] { "FS4004" }, diagnostics.Select(d => d.Code).ToArray());
    }

    [TestMethod]
    public void Verifier_rejects_invalid_branch_targets_even_when_unreachable()
    {
        var module = new PCodeModule(
            Array.Empty<FluidValue>(),
            new[]
            {
                new PCodeFunction("__main", 0, 0, new[]
                {
                    new Instruction(OpCode.Jump, 99),
                    new Instruction(OpCode.Halt)
                })
            });

        var diagnostics = PCodeVerifier.Verify(module);

        Assert.IsTrue(diagnostics.Any(d => d.Code == "FS4002"));
    }

    [TestMethod]
    public void Verifier_rejects_invalid_dictionary_entry_counts()
    {
        var module = new PCodeModule(
            Array.Empty<FluidValue>(),
            new[]
            {
                new PCodeFunction("__main", 0, 0, new[]
                {
                    new Instruction(OpCode.MakeDictionary, -1),
                    new Instruction(OpCode.Halt)
                })
            });

        var diagnostics = PCodeVerifier.Verify(module);

        Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.Code == "FS4004"));
    }

    [TestMethod]
    public void Verifier_rejects_typed_json_deserialization_without_a_declared_type()
    {
        var module = new PCodeModule(
            Array.Empty<FluidValue>(),
            new[]
            {
                new PCodeFunction("__main", 0, 0, new[]
                {
                    new Instruction(OpCode.Null),
                    new Instruction(OpCode.CallNative, -3, 1, 0),
                    new Instruction(OpCode.Halt)
                })
            });

        var diagnostics = PCodeVerifier.Verify(module);

        Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.Code == "FS4004"));
    }

    [TestMethod]
    public void PCode_serialization_round_trips_deterministically()
    {
        var result = FluidScriptCompiler.Compile("print([1, 2][0])\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var first = PCodeSerializer.Serialize(result.Module!);
        var restored = PCodeSerializer.Deserialize(first);
        var second = PCodeSerializer.Serialize(restored);

        CollectionAssert.AreEqual(first, second);
        StringAssert.Contains(PCodeDisassembler.Disassemble(restored), "IndexGet");
    }

    [TestMethod]
    public void PCode_serialization_preserves_dictionary_constants_and_instructions()
    {
        var source = "type Pair\n    dim first: int\n    dim second: int\nend\ndim values = { \"second\": 2, \"first\": 1 }\ndim restored = jsonDeserializeAs(Pair, jsonSerialize(values))\nprint(restored.first)\n";
        var result = FluidScriptCompiler.Compile(source);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var compiledBytes = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.None);
        var compiled = PCodeSerializer.Deserialize(compiledBytes);
        var output = new List<string>();
        new VirtualMachine().Run(compiled, output.Add);
        CollectionAssert.AreEqual(new[] { "1" }, output);
        StringAssert.Contains(PCodeDisassembler.Disassemble(compiled), "MakeDictionary");

        var first = new PCodeModule(
            new[] { FluidValue.FromDictionary(new FluidDictionary(new Dictionary<string, FluidValue>
            {
                ["second"] = FluidValue.From(2L),
                ["first"] = FluidValue.From(1L)
            })) },
            new[] { new PCodeFunction("__main", 0, 0, new[] { new Instruction(OpCode.Const), new Instruction(OpCode.Halt) }) });
        var second = new PCodeModule(
            new[] { FluidValue.FromDictionary(new FluidDictionary(new Dictionary<string, FluidValue>
            {
                ["first"] = FluidValue.From(1L),
                ["second"] = FluidValue.From(2L)
            })) },
            new[] { new PCodeFunction("__main", 0, 0, new[] { new Instruction(OpCode.Const), new Instruction(OpCode.Halt) }) });

        var firstBytes = PCodeSerializer.Serialize(first, PCodeDebugInfo.None);
        var restored = PCodeSerializer.Deserialize(firstBytes);
        CollectionAssert.AreEqual(firstBytes, PCodeSerializer.Serialize(second, PCodeDebugInfo.None));
        Assert.AreEqual(FluidValueKind.Dictionary, restored.Constants.Single().Kind);
        Assert.AreEqual(1L, restored.Constants.Single().AsDictionary().Entries["first"].AsInt());
    }

    [TestMethod]
    public void PCode_serialization_can_omit_debug_spans()
    {
        var result = FluidScriptCompiler.Compile("print(1 + 2)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));

        var withDebug = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.SourceSpans);
        var withoutDebug = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.None);
        var restored = PCodeSerializer.Deserialize(withoutDebug);

        Assert.IsGreaterThan(withoutDebug.Length, withDebug.Length);
        Assert.IsTrue(result.Module!.Functions.SelectMany(function => function.Instructions)
            .Any(instruction => instruction.Span != SourceSpan.None));
        Assert.IsTrue(restored.Functions.SelectMany(function => function.Instructions)
            .All(instruction => instruction.Span == SourceSpan.None));
        CollectionAssert.AreEqual(withoutDebug, PCodeSerializer.Serialize(restored, PCodeDebugInfo.None));
        var output = new List<string>();
        new VirtualMachine().Run(restored, output.Add);
        CollectionAssert.AreEqual(new[] { "3" }, output);
    }

    [TestMethod]
    public void PCode_deserialization_rejects_unknown_debug_flags()
    {
        var result = FluidScriptCompiler.Compile("print(1)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var bytes = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.None);
        bytes[6] = 0xff;

        Assert.Throws<InvalidDataException>(() => PCodeSerializer.Deserialize(bytes));
    }

    [TestMethod]
    public void PCode_deserialization_rejects_truncated_payloads()
    {
        var result = FluidScriptCompiler.Compile("print(1)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var bytes = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.SourceSpans);

        Assert.Throws<InvalidDataException>(() => PCodeSerializer.Deserialize(bytes[..^1]));
    }

    [TestMethod]
    public void PCode_deserialization_rejects_trailing_data()
    {
        var result = FluidScriptCompiler.Compile("print(1)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var bytes = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.None).Concat(new byte[] { 0x7f }).ToArray();

        Assert.Throws<InvalidDataException>(() => PCodeSerializer.Deserialize(bytes));
    }

    [TestMethod]
    public void Debug_pcode_records_and_validates_the_exact_source_hash()
    {
        const string source = "print(1 + 2)\n";
        var result = FluidScriptCompiler.Compile(source);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var bytes = PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.SourceSpans);
        CollectionAssert.AreEqual(bytes, PCodeSerializer.Serialize(result.Module!, source));

        var restored = PCodeSerializer.Deserialize(bytes, source);

        CollectionAssert.AreEqual(PCodeSerializer.ComputeSourceHash(source), restored.SourceHash.ToArray());
        Assert.IsTrue(PCodeSerializer.SourceHashMatches(restored, source));
        Assert.IsFalse(PCodeSerializer.SourceHashMatches(restored, source + " "));
        Assert.Throws<InvalidDataException>(() => PCodeSerializer.Deserialize(bytes, source + " "));
        Assert.IsFalse(PCodeSerializer.SourceHashMatches(
            PCodeSerializer.Deserialize(PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.None)), source));
    }
}

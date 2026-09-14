using FluidScript.Runtime;
using FluidScript.Compilation;

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
}

using FluidScript.Compilation;
using FluidScript.Runtime;

namespace FluidScript.Test;

[TestClass]
public sealed class DebugExecutionTests
{
    [TestMethod]
    public void Debug_execution_stops_before_a_requested_line_and_resumes_from_a_self_contained_state()
    {
        const string source = """
            function double(value: int): int
                dim doubled = value * 2
                return doubled
            end
            print("before")
            dim result = double(3)
            print(result)
            """;
        var result = FluidScriptCompiler.Compile(source);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var output = new List<string>();
        var machine = new VirtualMachine();

        var paused = machine.RunDebug(result.Module!, new[] { 2 },
            new FluidScriptExecutionContext(output: output.Add));

        Assert.IsTrue(paused.IsStopped);
        Assert.IsNull(paused.Value);
        Assert.AreEqual(2, paused.State!.Line);
        Assert.HasCount(2, paused.State.Frames);
        Assert.AreEqual(3L, paused.State.Frames[^1].Variables["value"].AsInt());
        CollectionAssert.AreEqual(PCodeSerializer.ComputeDebugPCodeHash(result.Module!), paused.State.PCodeHash.ToArray());
        CollectionAssert.AreEqual(new[] { "before" }, output);

        var resumedGlobals = new Dictionary<string, FluidValue>(StringComparer.Ordinal);
        var completed = new VirtualMachine().RunFromDebugState(result.Module!, paused.State,
            new FluidScriptExecutionContext(globals: resumedGlobals, output: output.Add));

        Assert.IsFalse(completed.IsStopped);
        Assert.AreEqual(FluidValue.Null, completed.Value);
        CollectionAssert.AreEqual(new[] { "before", "6" }, output);
        Assert.AreEqual(6L, resumedGlobals["result"].AsInt());
    }

    [TestMethod]
    public void Debug_resume_rejects_a_checkpoint_for_different_pcode()
    {
        var result = FluidScriptCompiler.Compile("dim value = 1\nprint(value)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var machine = new VirtualMachine();
        var paused = machine.RunDebug(result.Module!, new[] { 1 });
        Assert.IsTrue(paused.IsStopped);
        var wrongHash = paused.State!.PCodeHash.ToArray();
        wrongHash[0] ^= 0xff;
        var invalidState = new PCodeDebugState(
            wrongHash,
            paused.State.Line,
            paused.State.Frames,
            paused.State.OperandStack,
            paused.State.Globals,
            paused.State.InstructionsExecuted);

        Assert.Throws<ArgumentException>(() => machine.RunFromDebugState(result.Module!, invalidState));
    }

    [TestMethod]
    public void Debug_execution_requires_debug_pcode()
    {
        var result = FluidScriptCompiler.Compile("print(1)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var releaseModule = PCodeSerializer.Deserialize(PCodeSerializer.Serialize(result.Module!, PCodeDebugInfo.None));

        Assert.Throws<InvalidOperationException>(() => new VirtualMachine().RunDebug(releaseModule, new[] { 1 }));
    }

    [TestMethod]
    public void Debug_state_json_can_be_sent_to_a_new_runner_and_resume_the_program()
    {
        var result = FluidScriptCompiler.Compile("dim value = 2\nvalue += 3\nprint(value)\n");
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        var paused = new VirtualMachine().RunDebug(result.Module!, new[] { 2 });
        Assert.IsTrue(paused.IsStopped);

        var transported = PCodeDebugStateJson.Deserialize(PCodeDebugStateJson.Serialize(paused.State!));
        transported.SetGlobal(0, FluidValue.From(40L));
        Assert.AreEqual(40L, transported.Globals[0].AsInt());
        var output = new List<string>();
        var completed = new VirtualMachine().RunFromDebugState(
            result.Module!, transported, new FluidScriptExecutionContext(output: output.Add));

        Assert.IsFalse(completed.IsStopped);
        CollectionAssert.AreEqual(new[] { "43" }, output);
    }

    [TestMethod]
    public void Debug_state_json_rejects_invalid_value_references()
    {
        const string invalid = """
            {"pCodeHash":"AA==","line":1,"frames":[],"operandStack":[{"ref":4}],"globals":[],"nodes":[]}
            """;

        Assert.Throws<InvalidDataException>(() => PCodeDebugStateJson.Deserialize(invalid));
    }

    [TestMethod]
    public void Debug_state_json_preserves_cells_shared_with_a_closure()
    {
        var cell = new FluidCell(FluidValue.Null);
        cell.Value = FluidValue.FromFunction(7, new[] { cell });
        var state = new PCodeDebugState(
            new byte[32],
            1,
            new[] { new PCodeDebugFrame(0, 0, 0, new[] { cell }) },
            Array.Empty<FluidValue>(),
            Array.Empty<FluidValue>());

        var restored = PCodeDebugStateJson.Deserialize(PCodeDebugStateJson.Serialize(state));
        var closure = (FunctionHandle)restored.Frames[0].Locals[0].Value.Raw!;

        Assert.AreSame(restored.Frames[0].Locals[0], closure.Captures![0]);
    }
}

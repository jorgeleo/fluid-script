using System.Diagnostics;
using FluidScript.Compilation;
using FluidScript.Runtime;

namespace FluidScript.Test;

[TestClass]
[DoNotParallelize]
public sealed class VirtualMachineOptimizationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Arithmetic_and_loops_preserve_vm_behavior()
    {
        var result = Compile("dim total = 0\nfor i = 1 to 100\n    total += i\nend\nprint(total)\n");
        var output = new List<string>();

        result.Execute(output.Add);

        CollectionAssert.AreEqual(new[] { "5050" }, output);
    }

    [TestMethod]
    public void Script_calls_and_closures_preserve_vm_behavior()
    {
        var result = Compile("function makeCounter(): any\n    dim value = 0\n    return () =>\n        value += 1\n        return value\n    end\nend\ndim next = makeCounter()\nprint(next())\nprint(next())\n");
        var output = new List<string>();

        result.Execute(output.Add);

        CollectionAssert.AreEqual(new[] { "1", "2" }, output);
    }

    [TestMethod]
    public void Arrays_dictionaries_and_fault_handlers_preserve_vm_behavior()
    {
        var result = Compile("dim values = [1, 2]\nvalues[0] += 4\ndim data: dict = { \"answer\": values[0] }\ntry\n    print(data[\"missing\"])\ncatch error\n    print(error)\nend\nprint(data[\"answer\"])\n");
        var output = new List<string>();

        result.Execute(output.Add);

        CollectionAssert.AreEqual(new[] { "Dictionary key 'missing' is not initialized.", "5" }, output);
    }

    [TestMethod]
    public void Host_object_calls_preserve_vm_behavior()
    {
        var host = new FluidScriptHost();
        host.RegisterType("Person", typeof(TestPerson));
        var result = Compile("dim person = Person(\"Ada\", 36)\nperson.Age += 1\nprint(person.Greet(\"Hi\"))\nprint(person.Age)\n", host);
        var output = new List<string>();

        result.Execute(new FluidScriptExecutionContext(host, output: output.Add));

        CollectionAssert.AreEqual(new[] { "Hi Ada", "37" }, output);
    }

    [TestMethod]
    public void Host_overload_selection_preserves_best_match_and_ambiguity_behavior()
    {
        var host = new FluidScriptHost();
        host.RegisterType("Overloaded", typeof(TestOverloaded));
        var result = Compile("dim item = Overloaded(7)\nprint(item.Kind)\n", host);
        var output = new List<string>();

        result.Execute(new FluidScriptExecutionContext(host, output: output.Add));

        CollectionAssert.AreEqual(new[] { "long" }, output);

        var ambiguous = Compile("dim item = Overloaded(7)\nprint(item.Describe(7))\n", host);
        var fault = Assert.Throws<RuntimeFaultException>(() =>
            ambiguous.Execute(new FluidScriptExecutionContext(host)));
        Assert.AreEqual("FS5017", fault.Code);
    }

    [TestMethod]
    public void Declared_object_fields_preserve_vm_behavior()
    {
        var result = Compile("type Counter\n    dim value: int\nend\ndim counter = Counter(1)\ncounter.value += 4\nprint(counter.value)\n");
        var output = new List<string>();

        result.Execute(output.Add);

        CollectionAssert.AreEqual(new[] { "5" }, output);
    }

    [TestMethod]
    public void Invoke_preserves_script_function_behavior()
    {
        var result = Compile("function add(a: int, b: int): int\n    return a + b\nend\n");

        var value = new VirtualMachine().Invoke(
            result.Module!,
            "add",
            new[] { FluidValue.From(2L), FluidValue.From(3L) });

        Assert.AreEqual(FluidValue.From(5L), value);
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void Vm_performance_snapshot_for_optimization_loop()
    {
        var scenarios = new[]
        {
            new PerfScenario("arithmetic", Compile("dim total = 0\nfor i = 1 to 5000\n    total += i\nend\n"), null),
            new PerfScenario("calls", Compile("function add(a: int, b: int): int\n    return a + b\nend\ndim total = 0\nfor i = 1 to 1000\n    total = add(total, i)\nend\n"), null),
            new PerfScenario("closures", Compile("dim add = (a, b) => a + b\ndim total = 0\nfor i = 1 to 1000\n    total = add(total, i)\nend\n"), null),
            new PerfScenario("arrays-dictionaries", Compile("dim values = [1, 2, 3]\ndim data: dict = {}\nfor i = 1 to 1000\n    values[0] = values[0] + 1\n    data[\"value\"] = values[0]\nend\n"), null),
            new PerfScenario("objects", Compile("type Counter\n    dim value: int\nend\ndim counter = Counter(0)\nfor i = 1 to 10000\n    counter.value += 1\nend\n"), null),
            CreateHostScenario()
        };

        foreach (var (name, result, context) in scenarios)
        {
            for (var warmup = 0; warmup < 5; warmup++)
                Execute(result, context);

            var elapsed = new double[7];
            var allocations = new long[7];
            for (var sample = 0; sample < elapsed.Length; sample++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var beforeAllocations = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                for (var iteration = 0; iteration < 20; iteration++)
                    Execute(result, context);
                stopwatch.Stop();
                elapsed[sample] = stopwatch.Elapsed.TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocations;
            }
            Array.Sort(elapsed);
            Array.Sort(allocations);
            TestContext.WriteLine($"{name}: median {elapsed[elapsed.Length / 2]:F2} ms / 20 runs; {allocations[allocations.Length / 2]:N0} bytes");
        }
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void Vm_invoke_performance_snapshot_for_optimization_loop()
    {
        var result = Compile("function add(a: int, b: int): int\n    return a + b\nend\n");
        var arguments = new[] { FluidValue.From(1L), FluidValue.From(2L) };
        var virtualMachine = new VirtualMachine();
        for (var warmup = 0; warmup < 5; warmup++)
            virtualMachine.Invoke(result.Module!, "add", arguments);

        var elapsed = new double[7];
        var allocations = new long[7];
        for (var sample = 0; sample < elapsed.Length; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var beforeAllocations = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (var iteration = 0; iteration < 20; iteration++)
                virtualMachine.Invoke(result.Module!, "add", arguments);
            stopwatch.Stop();
            elapsed[sample] = stopwatch.Elapsed.TotalMilliseconds;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocations;
        }
        Array.Sort(elapsed);
        Array.Sort(allocations);
        TestContext.WriteLine($"invoke: median {elapsed[elapsed.Length / 2]:F2} ms / 20 runs; {allocations[allocations.Length / 2]:N0} bytes");
    }

    private static CompilationResult Compile(string source, FluidScriptHost? host = null)
    {
        var result = host is null
            ? FluidScriptCompiler.Compile(source)
            : FluidScriptCompiler.Compile(source, host);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        return result;
    }

    private static PerfScenario CreateHostScenario()
    {
        var host = new FluidScriptHost();
        host.RegisterType("Person", typeof(TestPerson));
        var result = Compile("dim person = Person(\"Ada\", 36)\nfor i = 1 to 1000\n    person.Age += 1\n    person.Greet(\"Hi\")\nend\n", host);
        return new PerfScenario("host-objects", result, new FluidScriptExecutionContext(host));
    }

    private static void Execute(CompilationResult result, FluidScriptExecutionContext? context)
    {
        if (context is null)
            result.Execute(instructionLimit: 2_000_000);
        else
            result.Execute(context, instructionLimit: 2_000_000);
    }

    private sealed class TestPerson
    {
        public TestPerson(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Name { get; }
        public int Age { get; set; }
        public string Greet(string greeting) => $"{greeting} {Name}";
    }

    private sealed class TestOverloaded
    {
        public TestOverloaded(long _) => Kind = "long";
        public TestOverloaded(string _) => Kind = "string";

        public string Kind { get; }
        public string Describe(decimal _) => "decimal";
        public string Describe(double _) => "double";
    }

    private sealed record PerfScenario(string Name, CompilationResult Result, FluidScriptExecutionContext? Context);
}

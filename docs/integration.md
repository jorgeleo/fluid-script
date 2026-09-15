# Integrating FluidScript into a .NET project

`fluid-script` is a `net10.0` class library. Add it as a project reference (or
package reference when you distribute it), then use the public compilation and
runtime APIs from `FluidScript.Compilation` and `FluidScript.Runtime`.

```xml
<ItemGroup>
  <ProjectReference Include="../fluid-script/fluid-script.csproj" />
</ItemGroup>
```

## Compile and execute a script

Always inspect compilation diagnostics before running. `Compile(source)` creates
a host with the four standard libraries, and `CompilationResult.Execute` uses
that compatible host automatically.

```csharp
using FluidScript.Compilation;
using FluidScript.Runtime;

var source = """
    dim input: int
    dim output: int = input * 2
    print("result: {output}")
    """;

var compilation = FluidScriptCompiler.Compile(source);
if (!compilation.Success)
{
    foreach (var diagnostic in compilation.Diagnostics)
        Console.Error.WriteLine($"{diagnostic.Code} at {diagnostic.Span}: {diagnostic.Message}");
    return;
}

var globals = new Dictionary<string, FluidValue>
{
    ["input"] = FluidValue.From(21L)
};
var context = new FluidScriptExecutionContext(globals: globals, output: Console.WriteLine);
var result = compilation.Execute(context);

Console.WriteLine(globals["output"].AsInt()); // 42
```

Globals supplied by name initialize matching script globals. After a successful
run, declared globals are written back to the same dictionary. Arrays,
dictionaries, and objects are reference values, so mutations can intentionally
cross the boundary. Catch `RuntimeFaultException` around execution to report
the script's code, message, span, and function without treating it as an
unhandled host failure.

```csharp
try
{
    compilation.Execute(context, instructionLimit: 100_000);
}
catch (RuntimeFaultException fault)
{
    logger.LogWarning("{Code} in {Function} at {Span}: {Message}",
        fault.Code, fault.FunctionName, fault.Span, fault.Message);
}
```

Use a lower instruction limit for untrusted or latency-sensitive scripts. The
VM has no ambient I/O, filesystem, network, process, or reflection access;
only explicitly registered capabilities are available.

To call one named script function directly, use the VM. Its supplied context
provides the same globals, output sink, and host capabilities.

```csharp
var value = new VirtualMachine().Invoke(
    compilation.Module!,
    "multiply",
    new[] { FluidValue.From(6L), FluidValue.From(7L) },
    context);
```

## Add application capabilities and CLR types

Create one `FluidScriptHost`, add capabilities before compilation, and use that
same host for every execution of the resulting module. Registration is
append-only: do not change the name or meaning of a capability in a persisted
P-code compatibility set.

```csharp
var host = FluidScriptHost.CreateStandardLibraryHost();

host.RegisterFunction("getOrderTotal", arguments =>
{
    if (arguments.Count != 1)
        throw new ArgumentException("getOrderTotal expects one order ID.");

    var orderId = arguments[0].AsInt();
    return FluidValue.From(orders.GetTotal(orderId));
});

var compilation = FluidScriptCompiler.Compile(
    "dim total = getOrderTotal(42)\nprint(\"{total}\")\n", host);
var context = new FluidScriptExecutionContext(host, output: Console.WriteLine);
compilation.Execute(context);
```

`NativeFunction` receives `IReadOnlyList<FluidValue>` and returns a
`FluidValue`. Validate arity and kinds at the boundary; exceptions from a host
function are converted to a script fault. Do not give an untrusted script a
generic “run any command” or file/network capability.

For a controlled object surface, register a CLR type. Scripts can construct it
and access only its public instance constructors, properties, fields, and
methods. A pre-existing instance must be wrapped explicitly.

```csharp
host.RegisterType<Customer>("Customer");
var globals = new Dictionary<string, FluidValue>
{
    ["currentCustomer"] = host.Wrap(customer)
};

var compilation = FluidScriptCompiler.Compile(
    "currentCustomer.Points += 10\nprint(currentCustomer.Name)\n", host);
compilation.Execute(new FluidScriptExecutionContext(host, globals, Console.WriteLine));
```

`Wrap` rejects an instance whose type is not registered. Constructor/method
binding and CLR-member failures are script fault `FS5017`; this is the intended
reflection boundary, not permission to expose arbitrary CLR objects.

### Group custom capabilities as a library

An application library can implement `IRegister` and register a coherent,
versioned group of functions/types. Apply it before compiling scripts that call
it:

```csharp
public sealed class OrdersLibrary : IRegister
{
    public void Register(FluidScriptHost host) =>
        host.RegisterFunction("orders.isPriority", args =>
            FluidValue.From(args.Count == 1 && args[0].AsInt() >= 1000));
}

var host = FluidScriptHost.CreateStandardLibraryHost();
new OrdersLibrary().Register(host);
var compilation = FluidScriptCompiler.Compile("print(orders.isPriority(1200))\n", host);
```

The built-in `String`, `RegExp`, `Number`, and `Math` libraries are registered
by `CreateStandardLibraryHost`; `FluidScriptCompiler.Compile(source, host)`
also makes their registration idempotent. Keep a single stable registration
order for any compiled or serialized module that references host capabilities.

## Serialize P-code for storage or distribution

Compile source once, serialize the successful `PCodeModule`, and deserialize
before execution. The default format includes source spans and binds the module
to the exact UTF-8 source text with SHA-256; use it for diagnostics and
debugging. The compact release form removes spans and source hash.

```csharp
var debugBytes = PCodeSerializer.Serialize(compilation.Module!, source);
var restoredDebug = PCodeSerializer.Deserialize(debugBytes, source); // verifies source hash

var releaseBytes = PCodeSerializer.Serialize(compilation.Module!, PCodeDebugInfo.None);
var restoredRelease = PCodeSerializer.Deserialize(releaseBytes);

var context = new FluidScriptExecutionContext(host, output: Console.WriteLine);
new VirtualMachine().Run(restoredRelease, context);
```

Treat P-code as input that still needs a compatible runtime and host manifest.
In particular, a module using registered functions/types must execute with the
same compatible registrations. Do not attempt source-hash validation against
changed whitespace or line endings: debug P-code is bound to the exact source
bytes encoded as UTF-8.

## Enable detached debugging

Debugging requires source-span P-code (the default serializer form and a
freshly compiled module both qualify). Supply one-based source lines at which
the VM should stop. The returned `PCodeDebugState` is self-contained and can
be serialized, stored, or sent to another process; resume validates a hash of
the deterministic debug P-code before executing.

```csharp
var machine = new VirtualMachine();
var paused = machine.RunDebug(
    compilation.Module!,
    breakLines: new[] { 3 },
    context: new FluidScriptExecutionContext(host, globals, Console.WriteLine));

if (paused.IsStopped)
{
    var checkpointJson = PCodeDebugStateJson.Serialize(paused.State!);
    // Persist or return checkpointJson together with the unchanged source/module identity.

    var restoredState = PCodeDebugStateJson.Deserialize(checkpointJson);
    var completed = new VirtualMachine().RunFromDebugState(
        compilation.Module!,
        restoredState,
        new FluidScriptExecutionContext(host, globals, Console.WriteLine));
}
```

Breakpoints stop _before_ an instruction on the requested line. A resumed state
includes frames, locals/captures, globals, the operand stack, exception state,
and instruction count. Do not resume a checkpoint against altered source or a
different P-code module; the VM rejects it. `RunDebug` and `RunFromDebugState`
reject release P-code (`PCodeDebugInfo.None`).

The included `fluid-script-monaco` sample shows a browser protocol with
`POST /api/debug/start` and `POST /api/debug/continue`; its continuation state
is `PCodeDebugStateJson`, not a server-held VM session.

## Manage source modules

Imports are resolved only through `IFluidModuleResolver`; the compiler never
opens files or URLs itself. Map a script-visible module name to source in the
application boundary and reject names outside your allowed catalog/root.

```csharp
public sealed class CatalogModuleResolver(
    IReadOnlyDictionary<string, string> modules) : IFluidModuleResolver
{
    public bool TryResolve(string moduleName, out string source) =>
        modules.TryGetValue(moduleName, out source!);
}

var resolver = new CatalogModuleResolver(new Dictionary<string, string>
{
    ["shared.validation"] = "dim validated: bool = true\n"
});

var compilation = FluidScriptCompiler.Compile(
    "import shared.validation\nprint(\"validated\")\n", resolver);
```

The resolver gets the exact name from either `import shared.validation` or
`import "shared/validation"`. Missing resolver, missing module, and an import
cycle compile as `FS2301`, `FS2302`, and `FS2303` respectively.

Important current limitation: imports only resolve and compile dependencies.
They do **not** execute a module, export symbols, establish namespaces, or make
dependency functions available to the importing script. Build shared behavior
as host capabilities today. Explicit exports/namespaces, runtime module
execution, canonical module identity caching, and serialized import metadata
are future work; do not design an integration that depends on them yet.

## Operational checklist

- Compile before every run and return source-mapped diagnostics to the author.
- Use one compatible `FluidScriptHost` for compile, run/debug, and persisted
  P-code; register capabilities before compilation.
- Set an instruction budget appropriate to the trust level; catch and log
  `RuntimeFaultException` without exposing host exception details.
- Store the exact source alongside debug P-code/checkpoints; use release P-code
  only when source-level debugging is not required.
- Keep module resolution application-owned and allowlisted. Treat current
  `import` as dependency validation, not a script-level linking mechanism.

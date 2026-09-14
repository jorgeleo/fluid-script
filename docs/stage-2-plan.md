# FluidScript Stage 2 plan

Stage 1 delivered a usable source-to-P-code vertical slice: ANTLR parsing,
source-spanned diagnostics, declarations, expressions, control flow,
functions and closures, arrays, nominal types, exceptions, imports through an
injected resolver, a verified stack VM, a disassembler, and deterministic
binary P-code. Stage 2 closes the semantic and tooling gaps that remain before
the language should be treated as complete.

This is a discussion plan, not a claim that the items below are implemented.
Each workstream is complete only after its happy-path and expected-failure TDD
fixtures pass at the parser, compiler, verifier, runtime, and source-to-VM
levels where applicable.

## Already added as part of the Stage 2 boundary

The current branch now has the first host and persistence boundary needed by
the remaining work:

* `PCodeSerializer.Serialize(module, PCodeDebugInfo.SourceSpans)` retains
  instruction source spans for diagnostics and debugging, plus a SHA-256 hash
  of the exact UTF-8 source text used to compile the module.
* `PCodeSerializer.Serialize(module, PCodeDebugInfo.None)` emits the compact
  release form without source spans or a source hash. Both forms are versioned,
  deterministic, self-describing, and deserialize through the same verifier.
  `PCodeSerializer.Deserialize(bytes, sourceText)` and
  `PCodeSerializer.SourceHashMatches` reject or report a mismatch before debug
  locations are trusted by an editor or debugger.
* `FluidScriptHost.RegisterFunction` grants named C# functions explicitly to a
  compilation and execution. The script can call those functions using
  `FluidValue` arguments and results; arbitrary reflection is not exposed.
* `FluidScriptExecutionContext` passes named global inputs into a run, captures
  global outputs after a successful run, and supplies output and host
  capabilities.
* `VirtualMachine.Invoke(module, functionName, arguments, context)` lets C# call
  a script-defined function and receive its `FluidValue` return value.

These APIs are deliberately small. Stage 2 must harden their contracts,
conversion rules, diagnostics, and compatibility tests rather than grow an
implicit ambient host object.

## Workstream 1 — finish the language semantics

### 1.1 Static types and binding

Not implemented completely:

* expression-level inference for every operator, call, member, index, lambda,
  and control-flow join;
* structural array and function types, function-parameter variance, return
  compatibility, and `any` escape rules;
* nominal user-type assignability, nullability, and definite assignment;
* lexical scope diagnostics for nested blocks, shadowing, use-before-declare,
  and unreachable statements;
* a bound symbol/type model that can be reused by the editor.

TDD order: add failing binder tests for one rule at a time, then compile
goldens and runtime checks. Every accepted type case gets a rejected mismatch
case, including an asserted diagnostic code and source span. Do not infer a
type from VM behavior after code generation; the binder must reject or
explicitly mark `any` before lowering.

### 1.2 Closures and function types

Nested functions and methods currently need capture-aware compilation. Define
whether a nested function captures locals by reference (the existing cell
model), whether a method may capture `self`, and whether a function value can
escape its module. Add tests for nested capture, mutation after the creator
returns, recursive closures, capture shadowing, invalid captures, and calls
through typed function values. Add P-code goldens for capture ordering and
verifier tests for capture-count/name mismatches.

### 1.3 Complete non-local control flow

`finally` must run when a function returns, or a loop executes `break` or
`continue`, in addition to the already-covered normal and exceptional paths.
Implement one unwinding mechanism carrying a pending completion
(`return`, `break`, `continue`, or fault) through handler/finally regions. Add
tests for nested handlers, a `finally` that itself returns or faults, and the
specified precedence when both a pending completion and a finally fault exist.
The compiler must not duplicate this behavior as ad-hoc jumps.

### 1.4 Modules and exports

The resolver boundary and missing-module/cycle diagnostics exist, but explicit
exports and qualified namespaces are not complete. Define and implement:

* `export` declarations and the shape of a module namespace;
* qualified lookup and optional aliases;
* execute-once caching keyed by canonical module identity;
* cycle diagnostics that include the complete import chain;
* visibility rules for globals, types, functions, and host capabilities; and
* serialized-module compatibility (including imported-module metadata).

Use a fake in-memory resolver for deterministic tests. Assert that an import
does not execute twice, private names cannot be read, missing exports have a
stable diagnostic, and a cycle never partially publishes a namespace.

## Workstream 2 — harden the P-code boundary

### 2.1 Debug and release serialization

Keep the two explicit forms:

| Form | Contains | Intended use |
| --- | --- | --- |
| Debug | instruction source spans, plus SHA-256 hash of the exact UTF-8 source text (and later symbol/source-map tables) | development, diagnostics, breakpoints, stack traces |
| Release | executable metadata and instructions, no source spans | distribution, caching, smaller payloads |

The format needs a documented flag byte, fixed SHA-256 hash encoding, bounded
counts, deterministic ordering, and a compatibility policy. Add tests for
byte-for-byte determinism, debug-span/hash retention, source-hash match and
mismatch, release-span/hash absence, truncated/garbled input, unknown flags,
oversized counts, malformed values, and verifier rejection after
deserialization. Decide in discussion whether future debug data is optional
sections under the same version or a new format version; do not silently accept
incompatible bytes.

### 2.2 Opcode and resource hardening

Complete verifier coverage for every opcode and every operand combination,
including handler nesting and all join depths. Add property/generated tests for
stack effects, jump targets, array bounds, and recursive call depth. Expose
instruction, stack, call-depth, and module-load budgets in an execution policy
object, and test deterministic faults when each limit is exceeded.

### 2.3 Source maps and disassembly

Promote source spans into a stable source-map abstraction that can map a P-code
offset to a source range and symbol/local name. The disassembler should support
human-readable and machine-readable output, with optional spans selected by
the same debug-information setting used by serialization.

## Workstream 3 — define the C# / script contract

The first capability API is intentionally synchronous and value-based. Stage 2
should ratify the following contract before adding convenience overloads.

### 3.1 C# functions callable from FluidScript

```csharp
var host = new FluidScriptHost();
host.RegisterFunction("add", args =>
    FluidValue.From(args[0].AsInt() + args[1].AsInt()));

var program = FluidScriptCompiler.Compile(
    "print(add(2, 3))\n", host);
program.Execute(new FluidScriptExecutionContext(host, output: Console.WriteLine));
```

Decisions still needed:

* whether registration is immutable after compilation, or calls are resolved
  by a host manifest at load time;
* how host exceptions map to `RuntimeFaultException` and which details are
  safe to expose;
* whether host functions may be re-entrant, recursive, or long-running;
* whether an async capability is needed (it should be a separate, explicitly
  awaited language feature); and
* capability naming/versioning and denial diagnostics.

Tests must cover successful scalar/array/object arguments, wrong value kinds,
missing capability IDs, duplicate/reserved names, host exceptions, and a
function that returns `null`.

### 3.2 Passing data into and out of a script

```csharp
var globals = new Dictionary<string, FluidValue>
{
    ["input"] = FluidValue.From(21L)
};
var context = new FluidScriptExecutionContext(globals: globals);
program.Execute(context);
var output = globals["output"].AsInt();
```

Choose and document whether an input may be missing, whether a declared
uninitialized global remains `null`, whether output is a snapshot or a live
reference, and how mutable arrays/objects are aliased across the boundary.
Add tests for every `FluidValueKind`, nested arrays/objects, mutation and
aliasing, missing input, undeclared input, and partial state after a fault.
If a JSON/CLR conversion helper is added, keep it outside the VM core and
test lossless decimal, date/time, GUID, byte, and null behavior explicitly.

### 3.3 C# calls into script functions

```csharp
var answer = new VirtualMachine().Invoke(
    program.Module!, "multiply",
    new[] { FluidValue.From(6L), FluidValue.From(7L) });
```

Define function lookup when names collide, default-argument behavior for host
callers, global/module context, closure handles, re-entrancy, and invocation
of a void function. Expected failures include unknown function, wrong arity,
invalid argument kind, instruction-limit exhaustion, and an uncaught script
fault with source metadata.

## Workstream 4 — editor and language-service tooling

### Recommendation: Monaco Editor for the primary editor

Use Monaco as the first-class editor surface if the product can host a web
view, browser UI, or Blazor/JavaScript bridge. Monaco provides custom language
registration, syntax tokenization, completion providers, diagnostics/markers,
hover and symbol APIs, and glyph-margin decorations suitable for breakpoints.
Its Monarch tokenizer is expressive enough for FluidScript's nested comments,
strings, and interpolation states. The official project describes custom
completion providers and validation, and its API exposes model decorations for
editor markers and breakpoint glyphs:

* [Monaco Editor](https://microsoft.github.io/monaco-editor/)
* [Monarch tokenizer](https://microsoft.github.io/monaco-editor/monarch-static.html)
* [Monaco model decorations](https://microsoft.github.io/monaco-editor/typedoc/interfaces/editor_editor_api.editor.IModelDecorationOptions.html)

The recommendation is based on the need for coloring, IntelliSense, source
markers, and debugging affordances in one editor. Ace is a reasonable small
syntax-only alternative, but its language-service and debugger integration
would be more application-owned ([Ace Editor](https://ace.c9.io/)). CodeMirror 6 is an excellent lightweight,
modular option with completion and lint extensions, but the surrounding
debugger/workbench would also be assembled by us; its lint and language-package
model illustrates that tradeoff ([lint](https://codemirror.net/examples/lint/),
[language packages](https://codemirror.net/examples/lang-package/)).

Keep the editor replaceable by defining a protocol-neutral C# language-service
facade:

```text
TextDocument(uri, version, text)
  -> Parse/Analyze: diagnostics, tokens, symbols, source ranges
Position + document
  -> CompletionItems, Hover, Definition, SignatureHelp
P-code debug session
  -> paused location, call stack, locals, evaluate, continue/step/breakpoint
```

Implementation sequence:

1. expose parse/bind diagnostics and token spans from the C# library;
2. add a Monarch tokenizer and map diagnostics to Monaco markers;
3. implement completion/hover/signature help from the bound symbol model;
4. emit source-map IDs in debug P-code and add VM pause/step/breakpoint hooks;
5. bridge those hooks to Monaco decorations and a variables/call-stack panel;
6. add editor integration tests using fixed documents and protocol responses.

If a future native-only client becomes primary, retain this facade and add a
CodeMirror 6 or native adapter instead of coupling compiler code to Monaco.

## Workstream 5 — diagnostics, CLI, and documentation

Add a stable diagnostic model containing code, severity, message, source span,
related spans, and optional quick-fix metadata. Build a small CLI/library host
that can parse, compile, disassemble, serialize debug/release P-code, run with
inputs, and invoke a named function. Every command needs an exit-code test and
malformed-input test. Turn examples in `docs/language.md` and this plan into
executable fixtures so documentation cannot drift from behavior.

## Workstream 6 — hardening and release readiness

Before calling Stage 2 complete:

* add lexer/parser fixtures for every grammar token/rule and malformed edge;
* add property-based or generated tests for arithmetic, arrays, control-flow
  targets, closures, handlers, and serialization;
* run mutation testing (or equivalent fault injection) against compiler and VM
  branches;
* benchmark compile, debug-load, release-load, and representative VM programs;
* document thread-safety, re-entrancy, resource limits, and host capability
  isolation; and
* establish a P-code version compatibility fixture and a policy for rejecting
  unsupported versions.

## Proposed discussion order

1. Lock type/nullability and module export semantics.
2. Lock non-local `finally` completion precedence.
3. Lock host conversion, exception, and capability rules.
4. Lock the P-code compatibility/debug-section policy.
5. Build the language-service facade, then the Monaco adapter.
6. Complete hardening, performance, CLI, and release evidence.

## Stage 2 exit criteria

Stage 2 is ready for release discussion when every workstream has a linked
test fixture and design decision, the full solution test run is green from a
clean checkout, debug and release P-code are both round-tripped and rejected
when malformed, host and script calls have typed failure coverage, and the
editor can display coloring, diagnostics, completion, and a source-mapped
breakpoint in a deterministic integration fixture.

# FluidScript architecture and coding decisions

This document is the engineering contract for the FluidScript repository. It
records decisions that should remain stable across compiler, VM, tooling, and
host-integration work. A change to an accepted decision requires a red test, an
updated decision entry, and a compatibility note in
[`docs/language.md`](language.md) or [`docs/pcode.md`](pcode.md), as
appropriate.

The implementation plan is tracked separately in
[`docs/stage-2-plan.md`](stage-2-plan.md). This document says how the system is
built; the plan says what remains to be built.

## Decision status

* **Accepted** means current code and tests must follow the decision.
* **Accepted, staged** means the contract is chosen but implementation or full
  test coverage is still outstanding.
* **Deferred** means no implementation may invent behavior; the listed
  decision must be made before the dependent feature is marked complete.

## Repository and build

### A1 — one .NET library and one MSTest project (Accepted)

The `fluid-script` project is the public `net10.0` class library. The
`fluid-script.test` project is its MSTest 4 test project and references the
library directly. The solution is `FluidScript.sln`.

Nullable reference types and implicit usings are enabled. Public APIs use
guard clauses for null and invalid arguments. Generated build output remains
under `bin/` and `obj/` and is not source controlled.

### A2 — ANTLR is generated during the build (Accepted)

`fluid-script/FluidScript.g4` is the syntax source of truth. The ANTLR MSBuild
integration generates lexer/parser sources as build artifacts. Generated code
is not hand-edited or checked in. Tests reach the lexer and parser through the
small adapters in `Parsing/`, which collect stable diagnostics.

## Compiler architecture

### A3 — explicit phase pipeline (Accepted)

Compilation always follows this boundary sequence:

```text
UTF-8 source
  -> ANTLR lexer/parser and syntax diagnostics
  -> immutable source-spanned syntax tree
  -> binding, scopes, symbols, and type checks
  -> lowering/desugaring
  -> P-code generation and source maps
  -> P-code verification
  -> executable PCodeModule
  -> VirtualMachine
```

No compiler phase compiles directly from an ANTLR parse context. A phase that
has errors returns diagnostics and must not silently manufacture executable
semantics from an invalid node.

The current source layout follows the boundaries:

```text
Parsing/       lexer/parser adapters, spans, diagnostics
Compilation/   syntax tree, binding/lowering, P-code generation
Runtime/       values, P-code model, verifier, serializer, VM, host boundary
```

Future binding and language-service code may receive its own `Binding/` or
`LanguageService/` namespace, but it must not collapse parsing, binding, and
runtime into one mutable pass.

### A4 — source-spanned immutable model (Accepted)

Syntax and P-code metadata carry `SourceSpan` values. Diagnostics use stable
codes, severity, message, and source span. AST-like nodes are immutable records
or immutable classes; mutable state belongs in compiler builders, VM frames,
cells, arrays, and objects only.

Source spans are not decoration: runtime faults and editor markers use them.
Any optimization that removes or merges instructions must preserve a mapping
back to the originating source range in debug P-code.

## Language semantics

### A5 — normative semantic contract (Accepted)

The grammar defines syntax; [`docs/language.md`](language.md) defines meaning.
The accepted baseline is:

* source is exact UTF-8 text; names and keywords are case-sensitive;
* newlines and semicolons separate statements, and comments are ignored;
* scopes are lexical; nested shadowing is allowed, same-scope duplicates are
  rejected, and assignment never declares a name;
* `dim` without an initializer is `null`; `const` initializes once and is
  immutable;
* type hints are checked statically, omitted hints are `any`, and runtime
  checks remain for `any` values;
* numeric arithmetic promotes integer pairs only when needed, string `+`
  requires two strings, and there is no implicit truthiness;
* conditions require `bool`; `for` bounds are inclusive and a zero step is a
  fault;
* arguments evaluate left-to-right, positional arguments precede named
  arguments, and defaults are evaluated in the defining environment;
* arrays, dictionaries, objects, and captured cells are mutable reference values;
* functions and lambdas use lexical scope; lambdas capture cells by reference;
* user `type` declarations are nominal, constructors initialize fields in
  declaration order, and methods receive implicit `self`;
* `switch` does not fall through; `try`/`catch`/`finally` preserve source
  semantics and faults carry their originating location; and
* imports use an injected resolver, never ambient filesystem, process, or
  network access.

When a feature is not covered by `language.md`, it is not yet a language
guarantee. Add the semantic rule and paired tests before relying on it.

### A6 — stable diagnostics and typed runtime faults (Accepted)

Compiler diagnostics and runtime faults use stable code families (`FS1xxx`
syntax, `FS2xxx` binding/type, `FS3xxx` lowering, `FS4xxx` P-code, and `FS5xxx`
runtime). Tests assert the code and relevant source location, not only a
localized message fragment. Runtime faults include function name and source
span when available, and resource-limit faults terminate execution
deterministically.

## P-code and VM

### A7 — verified stack machine (Accepted)

P-code is a versioned, zero-based stack machine. `PCodeModule` contains
constants, ordered functions, an entry function, global names, nominal type
metadata, and source-hash metadata. A `PCodeFunction` contains arity, local
slots/names, capture names, and instructions.

Every opcode has a documented stack effect in [`docs/pcode.md`](pcode.md).
`PCodeVerifier` rejects invalid operands, indexes, call arities, branch
targets, stack joins, handler nesting, and capture metadata before execution.
The VM never executes an unverified deserialized module.

Each VM frame owns locals, captures, an instruction pointer, a stack base,
handler state, and the current source span. The VM enforces instruction,
operand-stack, and call-depth limits. Resource limits are safety boundaries,
not language-level control flow.

### A8 — tagged values and explicit mutability (Accepted)

`FluidValue` is the public tagged value representation for `null`, booleans,
integers, decimals, strings, dates, GUIDs, bytes, arrays, dictionaries,
functions, script objects, registered CLR objects, and captured cells. Arrays,
dictionaries, script objects, and registered CLR objects preserve reference
identity when mutated; captured variables use `FluidCell` so closures observe
updates after the creating frame exits. CLR access is explicit and capability
based: only public instance members of types registered on `FluidScriptHost`
are reachable by the VM.

### A9 — two deterministic P-code wire forms (Accepted)

`PCodeSerializer` emits one of two forms selected by `PCodeDebugInfo`:

| Form | Required payload | Purpose |
| --- | --- | --- |
| `SourceSpans` | executable metadata, instructions, source spans, and SHA-256 hash | diagnostics, stack traces, editor locations, debugging |
| `None` | executable metadata and instructions, without spans or hash | distribution and caching |

The source hash is SHA-256 over the exact UTF-8 source text passed to the
compiler. Debug serialization obtains it from the compiled module or from the
explicit source-text overload. `Deserialize(bytes, sourceText)` rejects a
mismatch; `SourceHashMatches` is available when the caller wants a boolean
check before trusting locations. A release payload has no source hash and
cannot claim that its source locations are current.

The binary format has a magic value, version, debug flag, bounded counts,
deterministic ordering, and verifier-on-load behavior. Unknown versions or
flags, truncation, trailing data, malformed values, and invalid bytecode are
rejected as `InvalidDataException`. A format change requires a version decision
and compatibility fixture.

### A10 — source maps are an execution contract (Accepted, staged)

Debug P-code will grow from instruction spans into a source-map abstraction
that can map function/offset to source range, symbol, and local name. The
disassembler, debugger, and editor consume that abstraction rather than
reimplementing offset arithmetic. Release P-code intentionally omits this
debug metadata.

## Host and script boundary

### A11 — capability-based host functions and CLR objects (Accepted)

Host behavior is explicitly registered through `FluidScriptHost`. A
`NativeFunction` receives and returns `FluidValue`; names are resolved at
compile time to registry IDs, with ID `0` reserved for the built-in `print`.
`jsonSerialize`, `jsonDeserialize`, and `jsonDeserializeAs` use reserved
negative intrinsic IDs, so they do not shift append-only host capability IDs.
The VM exposes no ambient reflection, I/O, filesystem, network, or process
capability. Registered CLR types are an explicit reflection capability: their
public instance constructors, properties, fields, and methods are lowered to
verified host-object opcodes and remain unavailable without the matching host.

The same registry (or a compatible manifest with the same IDs) must be supplied
when executing serialized P-code. Missing IDs produce a deterministic runtime
fault. Host exceptions are converted to a script runtime fault without
exposing an uncontrolled host stack trace. Host registration is append-only so
existing function and type IDs do not change; changing a capability's meaning
requires a new name/version. Serialized modules that use registered CLR types
must execute with a compatible host registration manifest.

### A12 — explicit execution context for data exchange (Accepted)

`FluidScriptExecutionContext` owns the host registry, output callback, and
named global values. Values supplied in `Globals` initialize matching module
global slots; successful execution writes module globals back to that same
dictionary. `FluidValue` arrays, dictionaries, and objects are intentionally reference values,
so mutation/aliasing across the boundary must be documented and tested.

No implicit CLR-to-script conversion is performed. `host.Wrap` is the explicit
CLR-to-script conversion for registered objects. `FluidJson` is an explicit,
standard-JSON codec for JSON-native scalars, arrays, and dictionaries; it uses
ordinal property ordering and rejects non-JSON-native values rather than
silently converting date/time, GUID, byte, function, or cell values lossily.
Objects serialize their stored fields and restore to a nominal object only
through an explicit declared type. Any future CLR or extended JSON conversion helper remains
outside the VM and must define its own lossless behavior.

### A13 — C# invocation of script functions (Accepted, staged)

`VirtualMachine.Invoke` calls a named script function with `FluidValue`
arguments and returns a `FluidValue`. Unknown names and wrong arity are
argument errors; script faults retain their script code and source metadata.
The supplied execution context provides globals and capabilities. Default
parameter evaluation for external callers, module initialization ordering,
closure handles, and re-entrant invocation require dedicated tests before they
are treated as complete language guarantees.

## Modules

### A14 — injected module resolution (Accepted)

Imports resolve only through `IFluidModuleResolver`. Resolution is deterministic
and testable with an in-memory fake; no compiler or VM code reads process-wide
files or network resources directly.

Explicit exports, qualified namespaces, canonical identity caching, complete
cycle-chain diagnostics, and serialized import metadata are accepted as the
Stage 2 design direction but remain staged until their tests and wire-format
rules exist. Private names must not become reachable merely because a module
was resolved.

## Editor and language services

### A15 — Monaco is the primary editor target (Accepted, staged)

Monaco is the primary editor adapter when the product hosts a browser, webview,
or Blazor/JavaScript surface. It best matches the combined requirement for
custom syntax coloring, completion/IntelliSense, diagnostics, hover/symbol
features, and breakpoint decorations. Keep compiler services independent of
Monaco through a protocol-neutral C# facade for documents, diagnostics,
completion, hover, definitions, signature help, and debug sessions.

Ace remains a lightweight syntax-oriented alternative. CodeMirror 6 remains a
lightweight modular alternative with completion and lint extensions. Neither
alternative changes the compiler or language-service contract; only the editor
adapter changes. See the comparison and official references in
[`docs/stage-2-plan.md`](stage-2-plan.md).

### A16 — string-keyed dictionaries (Accepted)

`dict` is a built-in, mutable reference type. `{ key: value }` constructs a
dictionary after evaluating alternating key/value expressions left-to-right;
keys must evaluate to strings, and a later duplicate key replaces an earlier
value. `dictionary[key]` reads an existing string key, while
`dictionary[key] = value` creates or replaces one. A non-string key faults
with `FS5031`; an absent read faults with `FS5032`.

The compiler lowers literals to `MakeDictionary`, whose entry count is
verified before execution. `IndexGet` and `IndexSet` dispatch by target value
kind, retaining the existing array behavior. `FluidValueKind.Dictionary` and
`OpCode.MakeDictionary` are appended to their wire enums to preserve all
previous v0 numeric encodings. Serialized dictionary entries are written in
ordinal key order for deterministic payloads.

`FluidJson` is the explicit standard-JSON representation for the JSON-native
subset of `FluidValue`. It maps JSON objects to `FluidDictionary` recursively,
preserves array order, rejects reference cycles, and writes object properties
in ordinal order. Objects serialize their stored fields;
`jsonDeserializeAs` restores one only when the JSON field set exactly matches
its P-code type metadata. The JSON script intrinsics call this codec through
reserved negative native IDs, preserving host capability numbering.

## Coding and testing policy

### C1 — TDD is required for every feature (Accepted)

Before implementation, add a failing test with at least one happy path and one
expected failure. Then implement the smallest change, refactor with tests
green, and add a regression test for every discovered defect. A feature is not
complete because it parses or because one end-to-end example runs.

Use the appropriate layers:

```text
Parsing       token/rule and malformed-input tests
Compilation   AST, binding, lowering, and P-code golden tests
Verification  malformed-module and stack-effect tests
Runtime       opcode, value, frame, handler, and resource-limit tests
Integration   source -> compile -> VM result/output/fault tests
Compatibility serialized debug/release and version tests
```

Every accepted case must have a rejected or faulted counterpart where the
language contract permits failure. Assert stable diagnostic/fault codes and
source spans. Use MSTest data-driven tests for matrices, but retain named tests
for feature categories and failure categories.

### C2 — deterministic behavior is testable behavior (Accepted)

Use ordinal name comparison, stable declaration/order traversal, deterministic
constant/function/type ordering, and exact source text for hashes. Tests must
be repeatable without wall-clock, random, network, or process-global state.
Property/generated tests and mutation testing are required for Stage 2
hardening, in addition to example-based tests.

### C3 — small boundary-focused changes (Accepted)

Prefer small functions with one responsibility and explicit data flow. Keep
host, module, editor, and serialization boundaries typed and injectable. Do
not add ambient singletons, reflection-based dispatch, hidden global state, or
source-dependent behavior that is absent from the documented contract.

Before editing existing files, inspect the worktree and preserve unrelated
user changes. Use `apply_patch` for source/document edits. Do not hand-edit
ANTLR build outputs or use destructive repository commands to resolve conflicts.

## Required validation gate

For a compiler/runtime change, run:

```text
git diff --check
dotnet build fluid-script/FluidScript.csproj --configuration Release --no-restore
dotnet test FluidScript.sln --configuration Release --no-restore
```

If the test runner cannot open its local listener in a restricted environment,
record that limitation and rerun the same test command with the approved local
test permissions. Report focused checks, full-suite checks, and any deferred
editor/host/deployment evidence separately.

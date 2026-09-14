# FluidScript P-code v0

The current compiler emits a version-0 stack machine module. It can be kept in
memory or serialized deterministically without changing source semantics.

## Module and function layout

`PCodeModule` contains a constant pool, ordered functions, an entry-function
index, and global slot names. A `PCodeFunction` contains a name, arity, local
slot count, local names, and ordered instructions. Slots are zero-based.

## Stack effects

Each instruction consumes its operands from the top of the current frame's
operand stack and pushes its result:

| Instruction family | Effect |
| --- | --- |
| `Const`, `Null`, `Load*`, `MakeClosure` | `+1` (closure is `1 - captureCount`) |
| `Store*`, `Pop` | `-1` |
| `Dup` | `+1` |
| `Dup2`, `Swap` | `+2`, `0` |
| binary arithmetic/comparison | `-1` |
| `Neg`, `Not`, loop check/increment, jumps | `0` (`JumpIfFalse` is `-1`) |
| `Call`, `CallNative` | `1 - argumentCount` |
| `CallIndirect` | `-argumentCount` (the callable is also consumed) |
| `MakeArray` | `1 - elementCount` |
| `MakeDictionary` | `1 - (2 * entryCount)` |
| `IndexGet` | `-1` |
| `IndexSet` | `-3` |
| `NewObject` | `1 - fieldCount` |
| `FieldGet` | `0` |
| `FieldSet` | `-2` |
| `EnterHandler`, `LeaveHandler` | `0` |
| `ToText` | `0` |
| `Throw`, `Rethrow` | consumes one value and transfers control |
| `Return` | consumes one value; `ReturnVoid` consumes none |
| `Halt` | terminates with the optional top value |

The verifier rejects underflow, inconsistent join depths, invalid constant,
function, slot, and branch indexes, invalid call arity, and malformed array,
dictionary, or loop operands. Source spans are retained on every emitted instruction so VM
faults can report the originating source location.

## Calling convention

Arguments are pushed left-to-right. `Call` pops them in reverse order into the
callee's parameter slots. A callee's return value is pushed on the caller's
stack; `ReturnVoid` produces `null`. `CallNative` reserves builtin ID `0` for
`print(value)` and IDs `-1` and `-2` for `jsonSerialize(value)` and
`jsonDeserialize(text)`, plus ID `-3` for `jsonDeserializeAs(Type, text)`.
The negative IDs leave append-only host capability IDs unchanged. Other native
calls are intentionally capability-limited.

## Compatibility

`PCodeSerializer` provides two deterministic version-0 binary forms with a
magic value, explicit version, a debug-information flag, endianness-independent
primitive encodings, and metadata for constants, functions, captures, types,
globals, and instructions:

* `PCodeDebugInfo.SourceSpans` retains line/column/length on every instruction
  for diagnostics, disassembly, and future breakpoints. Its header also stores
  a SHA-256 hash of the exact UTF-8 source text used to compile the module.
* `PCodeDebugInfo.None` omits source-span payloads for a smaller distribution
  form and carries no source hash; deserialization restores `SourceSpan.None`.

`PCodeSerializer.Serialize(module)` remains the debug form for compatibility.
Use `PCodeSerializer.Deserialize(bytes, sourceText)` or
`PCodeSerializer.SourceHashMatches(module, sourceText)` to verify that debug
spans belong to the source currently displayed. Both forms are verified during
deserialization. A future format version must document compatibility and reject
unknown flags rather than guessing.
Version changes require a round-trip fixture and a compatibility decision in
the implementation plan.

`MakeDictionary` uses alternating key/value operands in source order. The VM
requires each key to be a string and retains the final value for duplicate
keys. Dictionary constants serialize entries in ordinal key order. The
dictionary value kind and `MakeDictionary` opcode were appended without
renumbering existing v0 tags or opcodes, so previously serialized modules
remain readable; modules that contain the new opcode require a dictionary-aware
runtime.

The JSON intrinsic IDs are also stable in v0 P-code. A serialized module that
uses them requires a runtime that implements the JSON conversion contract.

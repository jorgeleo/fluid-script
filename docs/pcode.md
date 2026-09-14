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
function, slot, and branch indexes, invalid call arity, and malformed array or
loop operands. Source spans are retained on every emitted instruction so VM
faults can report the originating source location.

## Calling convention

Arguments are pushed left-to-right. `Call` pops them in reverse order into the
callee's parameter slots. A callee's return value is pushed on the caller's
stack; `ReturnVoid` produces `null`. `CallNative` currently reserves builtin
ID 0 for `print(value)` and is intentionally capability-limited.

## Compatibility

`PCodeSerializer` provides a deterministic version-0 binary form with a magic
value, explicit version, endianness-independent primitive encodings, and
metadata for constants, functions, captures, types, globals, instructions,
and source spans. Deserialization verifies the module before returning it.
Version changes require a round-trip fixture and a compatibility decision in
the implementation plan.

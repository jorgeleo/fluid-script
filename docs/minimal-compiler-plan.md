# FluidScript: minimal P-code compiler and VM plan

## Purpose and v1 boundary

Build a small, deterministic FluidScript implementation that accepts a useful
subset of `fluid-script/fluidscript.g4`, emits inspectable stack-based P-code,
and executes it in an embedded VM. The first release is deliberately not a
complete implementation of every construct accepted by the grammar.

The compiler must parse the existing grammar, then issue source-spanned
diagnostics for syntactically valid features outside the selected v1 scope. It
must never emit partial or silently incorrect behavior for an unsupported
node.

### Supported in v1

* Values: `int`, exact `decimal`, `string`, `bool`, and `null`.
* Statements: `dim`, `const`, assignment to an identifier, expression
  statements, `if`/`else`, `while`, `for`, `break`, `continue`, function
  declarations, and `return`.
* Expressions: literals, identifiers, parentheses, arithmetic, comparison,
  equality, unary operators, and short-circuit `&&`/`||`.
* Calls: direct calls to top-level functions and a registered, explicit host
  builtin such as `print`.
* Type hints: parsed and retained in the AST, but not enforced in v1.

### Parsed but rejected in v1

Arrays and indexing, member access, lambdas/closures, `switch`, user `type`
declarations, imports, exceptions, named/default arguments, and type hints
that require a type system. The diagnostic should name the feature and its
source span, for example `FS3001: Lambda expressions are not supported by
P-code v1.`

Treat braces in strings literally in v1. The grammar notes an intent to add
interpolation, but it defines no interpolation syntax or escaping semantics.

## Language rules to freeze before code generation

Record the following in `docs/language-v1.md` before the compiler is exposed
as a public API:

* The required newline or semicolon before `end`; the grammar requires a
  statement separator after each statement in a block.
* Lexical scope and shadowing rules; `dim` without an initializer receives
  `null`, and undeclared assignment is an error.
* Constants are immutable after their declaration.
* Conditions require `bool`; there is no truthiness in v1.
* Integer/decimal promotion, string concatenation rules, equality, overflow,
  divide-by-zero, and modulo behavior.
* `for` evaluates start, end, and step once; a zero step faults; the test is
  inclusive and follows the sign of the step.
* Functions can call preceding or following top-level functions and themselves;
  functions have no closures in v1.
* Runtime errors contain source location, function name, and instruction
  position.

The grammar permits `try` with neither `catch` nor `finally`; v1 rejects all
exception handling. Revisit this grammar rule when exceptions are designed.

## Project layout

```text
fluid-script/                 FluidScript net10.0 class library
  fluidscript.g4              source grammar
  Parsing/                    generated parser integration and parse errors
  Ast/                        source-spanned syntax model
  Compilation/                binding, validation, and P-code generation
  Runtime/                    values, VM, call frames, builtin boundary
fluid-script.test/            FluidScript.Test net10.0 MSTest project
docs/
  minimal-compiler-plan.md    this plan
  language-v1.md              frozen language semantics
  pcode-v1.md                 bytecode and verifier contract
```

Use ANTLR to produce the parse tree, build an AST from it, then bind and
validate the AST before generation. Do not generate P-code directly from ANTLR
contexts: the AST is the stable seam for diagnostics, future type checking,
and later optimization.

```text
source -> parse tree -> AST -> bind/validate -> P-code module -> VM -> result
```

## P-code v1

Start with a readable in-memory representation. Add a compact on-disk format
only after semantic behavior and disassembly tests are stable.

```text
Module
  constants: Value[]
  functions: Function[]
  entryFunction: function id

Function
  name, arity, localCount, instructions, sourceMap
```

Core instructions:

```text
CONST k                 LOAD_LOCAL slot        STORE_LOCAL slot
LOAD_GLOBAL slot        STORE_GLOBAL slot      POP
ADD SUB MUL DIV MOD     NEG NOT
EQ NE LT LTE GT GTE
JUMP ip                 JUMP_IF_FALSE ip
CALL functionId argc    CALL_NATIVE builtinId argc
RETURN                  RETURN_VOID            HALT
```

Compile `&&` and `||` to jumps, not eager binary opcodes, so they are
short-circuiting. Compile each function into its own code unit and predeclare
all function IDs during binding, enabling recursion and forward calls. The
entry function contains executable top-level statements, not function bodies.

Either lower `for` to normal comparisons and jumps or add narrowly scoped
`FOR_CHECK` and `FOR_INCREMENT` instructions. The latter makes positive and
negative runtime step behavior explicit and easier to test.

The compiler emits a P-code verifier result before execution. It verifies
operand ranges, valid jump destinations, reachable instruction stack depth,
and function call arity.

## VM contract

The VM has a tagged `Value` representation, an operand stack, global storage,
and call frames. A frame holds its function ID, instruction pointer, local
slots, operand-stack base, and return target.

The host exposes builtins through a small registry interface; P-code never
reflects over arbitrary host objects or gains filesystem/network access by
default. The VM enforces instruction, operand-stack, and call-depth limits.
Every runtime fault resolves the instruction through the function source map
for a useful FluidScript diagnostic.

## Delivery sequence and evidence

1. Scaffold the library, MSTest project, solution, and project reference.
2. Add parser generation, parser-error tests, and fixtures exercising
   significant newlines and nested `end` blocks.
3. Add source spans and AST tests.
4. Build the binder: scopes, symbols, constants, function predeclaration,
   unresolved-name diagnostics, and unsupported-feature diagnostics.
5. Add the P-code builder, labels/fixups, disassembler, and verifier with
   golden instruction tests.
6. Implement the VM and direct builtin registry; unit-test every opcode and
   fault condition.
7. Implement expressions and declarations, then branches/loops, then
   functions/returns and `for`/loop control.
8. Add end-to-end source-to-result tests and a small CLI with `parse`,
   `compile`, `disasm`, and `run` commands.
9. Choose one coherent expansion at a time: arrays, switch, closures, types,
   modules, then exceptions last. `finally` requires a proper unwinder and
   must not be approximated with ordinary jumps.

Completion for v1 requires parser, compiler, verifier, VM, and end-to-end
coverage for every supported construct; diagnostic tests for every rejected
feature; a complete `dotnet test` run; and documented distinction between
in-memory P-code proof and any future serialized-module compatibility claim.

# FluidScript complete implementation plan

## Objective

Implement every construct accepted by `fluid-script/FluidScript.g4` as a
defined, tested language. The implementation consists of an ANTLR front end,
an AST and semantic compiler, versioned stack-based P-code, a verified VM, and
an explicit host/module boundary.

The grammar is the syntax contract. It is not, by itself, the language
contract: all runtime values, typing, scope, evaluation order, error behavior,
module behavior, and object behavior must be written down before the relevant
feature is marked complete.

The project targets the existing .NET 10 library and MSTest project. Generated
ANTLR sources remain build outputs; language/compiler/runtime tests are source
controlled.

## Non-negotiable TDD policy

Every feature follows this loop:

1. Add a failing test that demonstrates one happy path and at least one
   expected failure.
2. Implement the smallest parser, AST, binder, compiler, P-code, or VM change
   that makes the tests pass.
3. Refactor with the tests green, preserving source spans and diagnostics.
4. Add regression cases for every defect found.

No feature is complete because it parses or because its happy path runs. Its
completion record must link to:

* parser/AST tests for valid and invalid syntax;
* binder/type-checker tests for valid and invalid programs;
* P-code golden tests and verifier failure tests;
* VM/runtime tests for normal results and runtime faults; and
* end-to-end tests compiling source and observing the specified result/output.

Expected failures must assert the diagnostic code, relevant source span, and
stable message/category. Runtime failures must assert the runtime error kind,
function, instruction/source location, and that the VM terminates cleanly.

## Language contract to define first

Create `docs/language.md` as the normative semantic specification. Resolve
these decisions before implementing dependent features:

* Source is UTF-8; identifiers and keywords are case-sensitive. Newlines and
  semicolons are separators, while spaces and comments are ignored. A block
  statement must have its separator before `end`, as required by `block`.
* `dim` without an initializer starts as `null`; declarations use lexical
  scopes. Shadowing is allowed only in nested scopes, not within one scope.
  Assignment never implicitly declares a name.
* `const` is immutable after initialization. Its initializer is evaluated once
  in declaration order.
* Type hints are checked statically. An unannotated value has type `any`; an
  `any` value receives runtime operation checks. Built-ins include `int`,
  `decimal`, `string`, `bool`, `datetime`, `guid`, `byte`, `null`, and `any`.
  Array and function types are structural; user types are nominal.
* Define integer width/overflow, decimal precision, numeric promotion, string
  concatenation, equality identity/value rules, nullability, and conversion
  rules. Division/modulo by zero and invalid conversions are explicit faults.
* `if`/`while` conditions require `bool`; there is no implicit truthiness.
  `for` evaluates start, end, and step once, is inclusive, follows the sign of
  step, and faults on zero step. Its counter is scoped to the loop.
* Function arguments are evaluated left-to-right. Positional arguments precede
  named arguments; named arguments must be unique and known. Defaults are
  evaluated at call time in the function's defining environment. Return type
  and missing-return rules are defined for annotated and unannotated functions.
* Lambdas capture lexical cells by reference, retain those cells after the
  creating function returns, and have the same argument/default/type rules as
  named functions.
* `type` declarations create nominal object types with fields, constants, and
  methods. A call to a type is its constructor: fields initialize in
  declaration order, constructor arguments may be positional or named, and
  methods receive an implicit `self`.
* Arrays are mutable, zero-based, ordered values. Indexing faults on a
  non-array or out-of-range index; assignment through an index mutates the
  array. Nested arrays are ordinary values.
* `switch` evaluates its selector once, compares cases in source order, runs
  at most one case, and never falls through. `otherwise` is optional.
* `try` without `catch` or `finally` is valid per the grammar and simply lets
  faults propagate. `catch` binds the thrown value when it names a variable;
  an optional type hint filters matching exceptions. `finally` always runs,
  including during return, break, continue, and rethrow.
* `import qualified.name` and `import "path"` resolve through an injected
  module loader. Modules execute once, expose an explicit export namespace,
  reject cycles with a diagnostic, and cannot access the host except through
  registered capabilities.
* Strings decode the declared escapes. Unescaped `{expression}` segments are
  interpolated after parsing the string token; `{{` and `}}` represent literal
  braces, and unmatched interpolation braces are compile errors.

If product requirements choose different rules, update this document and the
tests before changing code.

## Complete grammar feature inventory and TDD acceptance matrix

Each row is a required test group. The examples are representative; the test
data must include nesting, boundary values, and source-location assertions.

| Grammar feature | Happy-path tests | Expected-failure tests |
| --- | --- | --- |
| `program`, `statementList`, separators | blank program; leading/trailing/multiple newlines; semicolon-separated statements | missing separator between statements; unexpected token after `end`; trailing garbage after `EOF` |
| line/block comments and whitespace | comments between tokens, at end of line, and around blocks | unterminated block comment; comment that removes a required separator |
| integer/decimal/string/bool/null literals | each literal, escapes, unicode escape, nested parentheses | malformed number; invalid escape; unterminated string; raw newline in string |
| `datetime`, `guid`, `byte` literals | date-only/date-time/time-zone forms; canonical GUID; hexadecimal byte | invalid calendar/time; malformed timezone; bad GUID groups; `0x` with no digits |
| `dim`, `const`, `typeHint` | initialized/uninitialized variables; constants; primitive, array, function, and user type hints | duplicate declaration; constant reassignment; unknown type; invalid initializer type; use before declaration |
| identifier and lexical scope | globals, locals, nested scopes, allowed shadowing, recursion | undeclared read/write; illegal same-scope redeclaration; escaping a block-only name |
| simple assignment and compound assignment | `=`, `+=`, `-=`, `*=`, `/=`, `%=` on variables and assignable paths | assignment to constant; invalid target; incompatible operand; divide/modulo by zero |
| assignable member/index paths | `object.field`, `array[i]`, and mixed chains for read/write | null receiver; missing field; non-array index; non-integer/out-of-range index |
| arithmetic and precedence | all `* / %`, `+ -`, comparisons, equality, parentheses, mixed numeric types | invalid operand types; overflow policy; divide/modulo by zero |
| unary operators | `!`, unary `+`, unary `-`, nested unary operators | `!` on non-bool; numeric unary operation on non-number |
| `&&` and `\|\|` | correct values and proven short-circuit side effects | non-bool operands; right-hand fault must not run when short-circuited |
| calls and arguments | positional, named, mixed valid calls; nested calls; builtin calls | wrong arity; duplicate/unknown named argument; positional after named; calling non-function |
| `if`/`else` | true/false branches, nested blocks, empty blocks | non-bool condition; missing block separator; malformed/duplicate `else` |
| `while` | zero-iteration and repeated loops; nested loops | non-bool condition; `break`/`continue` outside a loop |
| `for`, `to`, `step` | ascending, descending, omitted step, evaluated-once bounds | zero step; wrong bound/step types; illegal counter use; non-bool lowered condition |
| `break` and `continue` | each nesting level; `continue` runs `for` increment | use outside loop; unreachable malformed target after control statement |
| `switch`, `case`, `otherwise` | one match, no match, otherwise, nested switch, no fall-through | duplicate/unreachable case policy; invalid case comparison; `break` scope errors |
| function declarations and parameters | forward call, recursion, typed parameters/return, empty body, void return | duplicate function; invalid/default parameter ordering; missing required return; return outside function |
| default parameters | omitted defaults, explicit override, side-effect/evaluation timing | default references unavailable name; invalid default type; too few arguments |
| `return` | value and no-value returns, nested calls, return from `try`/`finally` | value from void function; missing value from required-return function |
| arrays and array literals | empty, trailing comma, nested arrays, mutation, aliasing | heterogeneous array if disallowed; invalid element type; malformed/trailing tokens |
| lambdas and lambda bodies | single expression; typed parameters; multiline `end`; closure capture/mutation | invalid arrow form; duplicate lambda parameter; capture of unavailable name; wrong call signature |
| function types | assignment/pass/return of compatible functions; variance rules | incompatible function type; wrong parameter/return type |
| `type` declarations and `typeBlock` | fields, constants, methods, initialization order, object construction | duplicate member; invalid field initializer; missing/invalid `self`; constructing abstract/unknown type |
| member access and methods | field read/write, method call, chained access, method recursion | missing member; access to null; writing constant field; calling field as method |
| `try`/`catch`/`finally` | typed/untyped catch, rethrow, nested handlers, normal and exceptional finally | invalid catch binding/type; swallowed fault policy; handler/finally control-flow violations |
| `throw` | throw literals/objects; throw from nested call; rethrow caught value | missing expression; invalid exception value if restricted; uncaught-fault metadata |
| imports and qualified names | string and dotted imports, exports, cache-once behavior, aliases if specified | missing module; syntax/module mismatch; import cycle; inaccessible export |
| string interpolation | expression interpolation, escapes, literal braces, nested member/call expressions | unmatched braces; invalid embedded expression; runtime interpolation fault |
| comments/newline edge cases | every feature with comments and CRLF/LF separators | separator-sensitive malformed variants |

The lexer test suite must additionally assert token kind/text for every keyword,
operator, punctuation token, numeric form, and comment/newline rule in the
grammar. The parser suite must assert every named parser rule is reachable by
at least one fixture.

## Test architecture

Keep tests in `fluid-script.test` grouped by layer. A feature's test folder is
created before implementation and remains the index of its contract.

```text
Parsing/       lexer tokens, parser trees, syntax diagnostics
Ast/           AST shape, source spans, literal decoding
Binding/       scopes, symbols, imports, types, overload/default resolution
Compilation/   P-code goldens, labels, lowering, source maps
Verification/  malformed bytecode, stack effects, operand/jump validation
Runtime/       opcode behavior, values, calls, arrays, objects, unwinding
Integration/   source -> compile -> VM result/output/error
Compatibility/ serialized P-code versions and module boundaries
```

Use MSTest `DataTestMethod`/`DataRow` for operator and literal matrices, but
keep a named test for each language feature and each failure category. Keep
source fixtures readable and include a marker such as `/*^ */` or a line/column
assertion helper for diagnostic spans.

Every end-to-end fixture should be testable in three modes:

1. parse only, asserting the grammar and token stream;
2. compile only, asserting symbols, diagnostics, and disassembled P-code; and
3. run, asserting value/output or a typed fault.

Add property-based or generated tests for arithmetic identities, array index
bounds, nested control-flow targets, closure lifetimes, and exception
unwinding after the basic examples are green. Add mutation testing or an
equivalent fault-injection pass to ensure tests detect removed branches and
incorrect opcode stack effects.

## Compiler architecture

Use the existing ANTLR MSBuild generation. The implementation stays split into
clear namespaces in the `FluidScript` library:

```text
Parsing       generated lexer/parser adapters and diagnostic listeners
Ast           immutable source-spanned nodes
Binding       scopes, symbols, module graph, nominal/structural types
Lowering      desugaring of switch, compound assignment, defaults, interpolation
Compilation   P-code instruction builder, labels, closures, and type tables
Verification  static P-code verifier
Runtime       values, frames, arrays, objects, handlers, module loader, VM
```

The pipeline is:

```text
source
  -> lexer/parser with collected errors
  -> AST with source spans
  -> module loading/import graph
  -> symbol binding and type checking
  -> explicit lowering
  -> P-code generation and source-map emission
  -> bytecode verification
  -> VM execution
```

Do not compile directly from ANTLR contexts. Each phase must be independently
testable and must refuse to continue after errors. Diagnostics use stable
codes, for example `FS1001` syntax, `FS2001` binding/type, `FS3001` lowering,
`FS4001` bytecode, and `FS5001` runtime.

## P-code contract

Define `docs/pcode.md` before emitting code. The module format is versioned and
contains constants, functions, closure/type metadata, module imports/exports,
exception-handler tables, and source maps. Initially use an in-memory form and
then add a deterministic binary serializer with round-trip tests.

Instruction families must cover all language features:

```text
Constants/stack: CONST, NULL, DUP, POP
Variables:       LOAD/STORE_LOCAL, LOAD/STORE_GLOBAL, LOAD/STORE_CAPTURE
Arithmetic:      ADD, SUB, MUL, DIV, MOD, NEG, NOT
Comparison:      EQ, NE, LT, LTE, GT, GTE
Control:         JUMP, JUMP_IF_FALSE, SWITCH, FOR_CHECK, FOR_INCREMENT
Calls:           CALL, CALL_NATIVE, MAKE_CLOSURE, RETURN, RETURN_VOID
Arrays:          MAKE_ARRAY, INDEX_GET, INDEX_SET
Objects:         NEW_OBJECT, FIELD_GET, FIELD_SET, METHOD_CALL
Exceptions:      THROW, ENTER_HANDLER, LEAVE_HANDLER, END_FINALLY, RERAISE
Modules:         IMPORT_MODULE, LOAD_EXPORT, STORE_EXPORT
Program:         HALT
```

The exact instruction set may lower constructs to simpler primitives, but
every emitted instruction has a documented stack effect and a compiler golden
test. The verifier checks opcode operands, constant/function/type indexes,
jump boundaries, stack depth at control-flow joins, handler nesting, closure
capture indexes, and call signatures.

`&&`/`||` must lower to short-circuit jumps. Compound/member/index assignment
must preserve evaluation order and evaluate a receiver/index exactly once.
Defaults, interpolation, and switch may lower to ordinary instructions if
their source maps and failure behavior remain observable and tested.

## VM and host contract

Implement a tagged `Value` type for all language values: null, bool, int,
decimal, string, datetime, guid, byte, arrays, objects, functions, closures,
and module references. Use explicit reference identity for mutable arrays,
objects, and captured cells.

Each call frame contains function ID, instruction pointer, locals, captures,
operand-stack base, and return target. The VM has bounded operand stack, call
depth, module loading, and instruction budget. Faults unwind frames and handler
tables while preserving the original source location.

The host boundary is capability-based. Builtins and module loading are
registered interfaces; no arbitrary reflection, filesystem, network, or
process access is available unless a host explicitly grants it. Test the same
program with a deterministic fake host and with missing/denied capabilities.

Finally semantics must be implemented by the unwinder, not duplicated as
ordinary jumps. Test `finally` while returning, breaking, continuing,
throwing, catching, and rethrowing.

## Delivery milestones

Each milestone starts with its test fixtures and ends only when all paired
happy/failure tests and the full suite pass.

1. **Contracts and harness** — finish `language.md` and `pcode.md`; add test
   helpers for source spans, diagnostics, compilation, disassembly, and VM
   output.
2. **Front end** — lexer/parser fixtures for every token/rule, AST conversion,
   malformed-input diagnostics, and complete literal decoding.
3. **Core semantics** — scopes, declarations, constants, primitive types,
   identifiers, all operators, assignment, and static type checking.
4. **P-code/VM core** — constants, locals/globals, arithmetic/comparison,
   jumps, stack verifier, calls, returns, source maps, and deterministic
   runtime faults.
5. **Control flow** — if/else, while, for/step, break/continue, and switch;
   include nested-target and unreachable-code cases.
6. **Collections and paths** — arrays, index/member paths, compound assignment,
   aliasing, bounds/type faults, and evaluation-order tests.
7. **Functions and closures** — defaults, named arguments, function types,
   lambdas, captured cells, recursion, and closure lifetime.
8. **User types** — fields, constants, constructors, methods, `self`, nominal
   typing, initialization order, and member/method faults.
9. **Exceptions** — throw, typed/untyped catch, nested handlers, rethrow, and
   fully unwound finally behavior.
10. **Modules** — resolver/loader interface, exports, qualified names, cache,
    cycle detection, capability denial, and serialized module boundaries.
11. **Interpolation and polish** — semantic string interpolation, complete
    source maps, diagnostics, disassembler, CLI, and documentation examples.
12. **Hardening** — deterministic binary P-code round trips, fuzz/property
    tests, mutation testing, resource limits, performance baselines, and
    compatibility/version tests.

## Definition of complete implementation

The language is complete only when all of the following are true:

* every lexer token and parser rule in `FluidScript.g4` has valid and invalid
  test fixtures;
* every semantic feature in the acceptance matrix has paired happy-path and
  expected-failure tests;
* every P-code opcode has stack-effect, verifier, normal-runtime, and fault
  tests where applicable;
* source-to-VM tests cover nesting, recursion, aliases, evaluation order,
  exception unwinding, module caching, and resource limits;
* diagnostics are stable, source-spanned, and documented;
* serialized P-code is versioned and round-trips deterministically;
* `dotnet test FluidScript.sln --configuration Release` passes from a clean
  checkout, with no generated files required in source control; and
* the documentation examples are executable test fixtures, not prose-only
  claims.

Passing unit tests alone is insufficient: the completion record must list the
feature matrix, test names/results, full-suite command, and any explicitly
deferred compatibility or host-environment evidence.

## Implementation progress

The current branch has a working vertical slice through the front end,
compiler, verified P-code VM, and deterministic module boundary. Implemented
and covered by MSTest fixtures are primitive literals (including datetime,
guid, and byte), source-spanned diagnostics, declarations/constants and
primitive type hints, arithmetic/comparison/logical short-circuiting,
if/while/for/switch, break/continue, arrays and evaluate-once index mutation,
functions with recursion/forward calls/defaults/named arguments, cell-backed
closures, nominal field-based types and implicit-`self` methods, interpolation,
throw/try/catch/finally unwinding, P-code verification/disassembly, and binary
P-code round trips. The runtime boundary also supports explicit C# host
functions, named global input/output values, and C# invocation of script
functions. P-code can be serialized with source spans for diagnostics or in a
compact form without debug information. Imports now have an explicit resolver
boundary and stable missing-resolver/unresolved-module diagnostics.

The remaining work is intentionally visible rather than implied complete:
full static type inference, nested-function/method closures and capture-aware
function types, return/break/continue finally unwinding, module export
namespaces/cycle detection/cache, richer host conversion/capability policies,
editor/language-service integration, and the remaining malformed-token,
property/fuzz/mutation fixtures from the matrix. See
`docs/stage-2-plan.md` for the proposed implementation and discussion order.

# FluidScript language contract

This document is the semantic companion to `FluidScript.g4`. The grammar
defines what is syntactically accepted; this document defines what accepted
programs mean and which failures are stable compiler or VM diagnostics.

## Source and names

Source text is UTF-8. Keywords and identifiers are case-sensitive. Spaces,
line comments (`//`) and block comments (`/* ... */`) are ignored. Newlines
and semicolons separate statements. A block is lexically scoped; a name may be
shadowed in a nested block, but duplicate declarations in one scope are an
error. Assignment never declares a name.

## Values and types

The runtime value set is `null`, `bool`, signed `int`, `decimal` (subject to
the host decimal range), `string`, `datetime`, `guid`,
`byte`, mutable arrays, functions, and user objects. Type hints are checked by
the semantic pass; an omitted hint has type `any` and receives runtime checks.
Integer arithmetic remains integer when both operands are integers; otherwise
numeric arithmetic promotes to decimal. Strings concatenate with `+` only
when both operands are strings. Equality is value equality for scalar values
and element/value equality for arrays. Ordering is defined for numeric pairs
and string pairs only.

`dim` without an initializer produces `null`. `const` evaluates its initializer
once and rejects later assignment. Array indexes are zero-based integers;
non-array targets, non-integer indexes, and out-of-range indexes are runtime
faults.

## Evaluation and control flow

Operands and arguments evaluate left-to-right. `if` and `while` require a
boolean; there is no implicit truthiness. `for` evaluates start, end, and step
once, uses inclusive bounds, follows the sign of step, and rejects zero step.
`break` and `continue` target the nearest enclosing loop. `switch` evaluates
its selector once, compares cases in source order, and does not fall through.

## Functions and objects

Functions have lexical scope and positional parameters. Defaults are evaluated
at call time in the defining environment. Named arguments must be unique and
match a declared parameter; positional arguments precede named arguments.
Annotated functions must return on every reachable path. Lambdas capture
lexical cells by reference and remain valid after the creating function exits.

`type` declarations create nominal object types. Fields initialize in
declaration order, constructors accept positional or named field arguments,
and methods receive an implicit `self` parameter. Member and index assignment
mutate the target object/array.

## Errors and modules

Diagnostics are source-mapped records with a stable code and severity.
Malformed syntax is reported by the parser; undefined names, duplicate names,
type mismatches, invalid arguments, and unsupported imports are compiler
errors. Runtime faults include invalid operations, division/modulo by zero,
invalid indexes, explicit `throw`, instruction-limit exhaustion, and call/
stack-limit exhaustion. `try` runs its body, transfers a matching fault to
`catch`, and always executes `finally`; a fault not handled by a catch escapes
the module. Imports resolve through an explicit host-provided module resolver,
never through ambient process state.

## Compatibility rule

Any change to these semantics requires a red test for the old behavior, an
updated happy/failure test pair, a plan update, and a versioned P-code/VM
compatibility note.

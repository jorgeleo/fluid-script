# FluidScript agent instructions

Before changing this repository, read and follow
[`docs/architecture-and-coding-decisions.md`](docs/architecture-and-coding-decisions.md).
That document is the authoritative record of the compiler architecture, VM and
P-code contracts, host boundary, editor direction, coding standards, and TDD
requirements.

Use the companion documents when relevant:

* [`docs/language.md`](docs/language.md) — normative language semantics;
* [`docs/pcode.md`](docs/pcode.md) — opcode, verifier, and wire-format rules;
* [`docs/stage-2-plan.md`](docs/stage-2-plan.md) — remaining implementation
  work and discussion order; and
* [`docs/minimal-compiler-plan.md`](docs/minimal-compiler-plan.md) — complete
  grammar/TDD inventory and implementation history.

Repository rules:

* inspect `git status` before editing and preserve unrelated user changes;
* follow red-test, implementation, refactor, regression-test TDD;
* keep diagnostics, source spans, P-code ordering, and serialization
  deterministic;
* do not hand-edit generated ANTLR output; and
* run the documented Release build and full MSTest gate before reporting a
  compiler/runtime change as complete.

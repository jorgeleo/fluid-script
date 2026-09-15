# VM optimization measurements

The VM optimization loop is covered by `VirtualMachineOptimizationTests` in
`fluid-script.test/VirtualMachineOptimizationTests.cs`. The tests first assert
the observable behavior of arithmetic, calls/closures, arrays, dictionaries,
fault handlers, declared objects, and registered host objects. The performance
snapshot then runs each compiled scenario for five warmups followed by seven
samples of twenty executions. The reported values are sample medians from a
Release build; elapsed time is wall-clock time and allocation is
`GC.GetAllocatedBytesForCurrentThread()`.

These are focused microbenchmarks, not production workload claims. In
particular, sub-millisecond results and timings that differ by less than a few
percent should be treated as noise. Allocation changes were used as the primary
decision signal when timing was inconclusive. Every candidate was run with the
behavior tests before it was accepted or reverted.

## Candidates

| Candidate | Before | After | Decision and reason |
| --- | --- | --- | --- |
| Direct script calls: remove the temporary argument array | `calls`: 7.89 ms, 8,186,120 B | `calls`: 8.04 ms, 7,066,120 B | **Kept.** Time was effectively flat/noisier, but each 20-call sample removed about 1.1 MB of allocation. |
| Array/dictionary construction capacity and array materialization | `arrays-dictionaries`: 8.73 ms, 997,320 B | 8.64 ms, 995,880 B | **Kept.** Small but repeatable reduction without changing duplicate-key evaluation order. |
| Remove LINQ from VM lookup/global/frame hot paths | `calls`: 7.86 ms, 7,066,120 B | 6.52 ms, 5,300,840 B | **Kept.** The call scenario showed a substantial reduction in both time and allocation. Other scenario timings moved within benchmark noise. |
| Array-backed operand stack | `calls`: 6.52 ms, 5,300,840 B | 7.80 ms, 5,320,040 B | **Reverted.** The stack implementation regressed calls and arrays. |
| Lazy exception-handler lists | `calls`: 6.52 ms, 5,300,840 B | 8.56 ms, 4,660,200 B | **Reverted.** Lower allocation did not offset the call/array timing regression. |
| Cached declared-type lookup for object fields | `objects`: 19.99 ms, 19,231,240 B | 21.69 ms, 9,631,240 B | **Reverted.** Although allocation fell, object access became slower. |
| Host overload selection without candidate-list/LINQ allocations | `host-objects`: 15.90 ms, 14,742,920 B | 14.50 ms, 7,535,720 B | **Kept.** Both timing and allocation improved while overload ambiguity behavior remained covered. |
| Cached reflection `ParameterInfo[]` | `host-objects`: 15.90 ms, 14,742,920 B | 17.70 ms, 6,894,920 B | **Reverted.** Allocation fell but reflection calls became slower. |
| Indirect/closure calls: read the callable in place and avoid the argument array | `closures`: 8.01 ms, 6,422,280 B | 5.08 ms, 5,302,280 B | **Kept.** This was the clearest call-path improvement. |
| Lazy local cells | `arithmetic`: 14.49 ms, 4,820,840 B | 14.74 ms, 4,821,480 B | **Reverted.** It added a branch to every local read; the small gains in a few scenarios were not consistent. |
| `Invoke` globals-array allocation (`Enumerable.Repeat` to `new[]`) | 0.01 ms, 11,400 B | 0.00 ms, 11,880 B | **Reverted.** The invocation benchmark is below timer resolution and allocation did not improve. |

The retained implementation is intentionally conservative: it changes data
movement and avoidable allocations while preserving the existing list-backed
operand stack, eager local cells, reflection behavior, and fault ordering.

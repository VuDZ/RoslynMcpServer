# Arbitration summary

| Finding ID | Grok position | Astra position | Final verdict | Required change |
| --- | --- | --- | --- | --- |
| R-01 | Scope guarantees to measured matrix; defer immutability claim | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Separate current behavior, targets, and gates; retain immutability as target |
| R-02 | Narrow consistency to flushed readers | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Separate integrity, flush freshness, and per-operation consistency; inventory readers |
| R-03 | Epoch 4 depends on epoch-2 mapping | ACCEPT | ACCEPT | Add mapping dependency; retain epoch-3 binding gate |
| R-04 | Split overwrite, CLR identity, and cache-hit questions | REJECT | REJECT | None; responsibilities are already split; clarify cache hit under E1-02 |
| E1-01 | Add an executable generated-version oracle | ACCEPT | ACCEPT | In-process production-lifecycle test reads exact generated constant |
| E1-02 | Define cache hit, reset+load, and restart | ACCEPT | ACCEPT | Name and test all three operations and graph/artifact effects |
| E1-03 | Observe cross-solution executed identity, not flags | ACCEPT | ACCEPT | A/B same-identity marker and loaded-path test |
| E1-04 | Test same-path flag-off and choose sticky vs desired state | ACCEPT | ACCEPT WITH MODIFICATION | Add transition tests; document current sticky/reset behavior; semantics remain unresolved |
| E1-05 | Require generated member use and exact oracle | ACCEPT | ACCEPT | Keep Consumer fixed; exact marker plus negative control |
| E1-06 | Name and require all three write paths | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Trace text edit, overlay apply, and watcher+flush separately |
| E1-07 | Add existing-path anti-lock fixture | ACCEPT | ACCEPT | Missing/existing path fixtures; force output-writing build after each path |
| E1-08 | Separate watcher delivery from flush | ACCEPT | ACCEPT | Bounded delivery wait, production flush, then text/marker assertion |
| E1-09 | Measure reapply failure and retain status | ACCEPT | ACCEPT | Regression for loaded shadow→edit→failed reapply; no prepare on edit in epoch 2 |
| E1-10 | Promote several-Consumer case; gate helper experiments | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Mandatory shared-generator case; separate prepare/bind/execute helper results |
| E2-01 | Remove helper obligation or include dependencies now | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Main-only policy in epoch 2; versioned path/hash-set policy; helper gate in epoch 3 |
| E2-02 | Never skip hash on an advertised refresh | ACCEPT | ACCEPT | Hash required bytes on refresh; no hashing on edit; same-timestamp test |
| E2-03 | Define manifest-last readiness and corrupt-destination handling | ACCEPT | ACCEPT WITH MODIFICATION | Validate closed files+manifest, no-replace move, reject invalid destination |
| E2-04 | Prepare only at explicit refresh; reuse mapping on edits | ACCEPT | ACCEPT | Define preparation boundaries and explicit stale refresh result |
| E2-05 | Test cross-process or use per-session roots | ACCEPT | ACCEPT WITH MODIFICATION | Retain shared root; mandatory two-process publication/reuse test |
| E2-06 | Add retention immediately via session cleanup | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Add budget, growth, ownership, disk-full, and operator cleanup; no unsafe auto-delete |
| E2-07 | Tie reuse acceptance explicitly to content ID | REJECT | REJECT | None beyond E2-01/E2-02; ticks-only already violates the proposal |
| E3-01 | Make restart the result if current loader fails | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Feasibility gate; choose proven in-process or explicit restart-required mode |
| E3-02 | Copy all output DLLs before considering ALC | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Explicit fixture set first; verified production discovery; no blanket DLL copy |
| E3-03 | Shared Roslyn/framework identity is an ALC precondition | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Enumerate host contracts and add type-identity tests before prototype |
| E3-04 | Replace global resolver or reject conflicting helpers | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Require scoped resolution or explicit unsupported failure; real owner lifetime |
| E3-05 | Completion may be a measured restart contract | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Split research selection from implementation completion; restart may be final |
| E3-06 | Eager-load or stop implying execution success | ACCEPT | ACCEPT WITH MODIFICATION | Split prepared/rewrite/load/execute states; lazy loading remains allowed |
| E4-01 | Remove in-flight snapshot lifecycle/MVCC requirement | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Minimal base-snapshot+mapping operation context; no history/merge engine |
| E4-02 | Pass unknown analyzer diffs or reject before writes | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Preflight before side effects; strip known overlay; reject unsupported diffs |
| E4-03 | Do not equate write boundary with anti-lock boundary | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Separate/load-test both boundaries; redesign only after locating a leak |
| E4-04 | Specify the text reconciliation fallback | ACCEPT | ACCEPT | Define preflight, success, partial persistence, and reconciliation outcomes |
| E4-05 | Implement after stable mapping | ACCEPT | ACCEPT | Epoch-2 mapping dependency and exact inverse tests |
| E4-06 | Make post-apply overlay publication part of workflow | ACCEPT | ACCEPT | Common workflow reapplies prepared mapping and tests marker/text/project bytes |
| E5-01 | Preserve missing-path heuristic if provenance API absent | NEEDS CLARIFICATION | UNRESOLVED | Metadata feasibility plus product choice; freeze matcher until resolved |
| E5-02 | Separate existing foreign and missing paths with binary rules | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Decision table; confirmed stale path differs from foreign; missing branch follows E5-01 |
| E5-03 | Use loaded inner-project output, not raw TFM list | ACCEPT | ACCEPT | Base selection on evaluated loaded projects and verified provenance |
| E5-04 | Add specific skip codes; avoid generic unconfirmed | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Stable reason codes including honest unconfirmed state and separate path status |
| E5-05 | Require exact foreign-marker oracle before rollout | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Add epoch-1 foreign same-name fixture; retain algorithmic independence from 2–4 |
| E6-01 | Document v1.3.5→target behavioral change and reload meanings | ACCEPT | ACCEPT | Action/current/target/test matrix and upgrade note |
| E6-02 | Correct false current-state docs immediately | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Early factual-correction task; epoch 6 audits rather than owns it |
| E6-03 | Enumerate real lifecycle branches | ACCEPT | ACCEPT | Matrix cache/graph/flag/prepare/apply/watcher/clear state effects |
| E6-04 | Keep reuse acceptance in epochs 2/4; docs reflect deferred work | PARTIALLY ACCEPT | ACCEPT WITH MODIFICATION | Epoch 6 is audit only; series status remains incomplete for missing runtime work |

Totals: **16 ACCEPT**, **23 ACCEPT WITH MODIFICATION**, **2 REJECT**, **1 UNRESOLVED**.

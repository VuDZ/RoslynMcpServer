# Traceability v2

## 1. Original requirements

| Requirement | Original source | Review findings | Arbitration | Resulting v2 sections |
|---|---|---|---|---|
| O1 new-PID acceleration | `workspace-load-cache/README` «Зачем»; Epoch 3 Mission | R-05, R-06, E0-04, E2-04, V-04 | ACCEPT WITH MODIFICATION | README §1/§3/§4; Epoch 0; Epoch 2; Verification §5–§6 |
| O2 no VCS/VS/internal serialization | v1 README «Фиксированные решения» | R-02, C-01, C-02, E2-01 | ACCEPT/ACCEPT WITH MODIFICATION | README §6; Contract §3/§5; Epoch 2 |
| O3 bounded solution cone | v1 README; Epoch 3 invalidation 1–3 | C-07, E3-03, E3-07 | ACCEPT/ACCEPT WITH MODIFICATION | Contract §8; Epoch 0; Epoch 3 coverage; Verification V16/V19/V22 |
| O4 next-call live freshness | v1 README; Epoch 3 live session 1–4 | R-05, E3-01..E3-08 | Mandatory; E3-02/E3-04 UNRESOLVED | README §1/§4; Epoch 3; U-ARB-02; Verification V19–V23 |
| O5 reuse after source/membership change | v1 Epoch 3 invalidation step 6/matrix | R-05, E2-04, E4-01 | Mandatory for series completion; 4C retained | README §1/§4/§5; Epoch 2 scope; Epoch 4C |
| O6 overlay/inner TFM/host direction | v1 README; Epoch 3 hydrate | R-04, R-06, C-05, E1-04 | ACCEPT WITH MODIFICATION | README §2/§3; Contract §2/§5; Epoch 1 |
| O7 metadata/partial directions | v1 Epoch 1/Epoch 2 | E0-05, E4-03, E4-04 | Preserved as separate directions | README §1/§4; Epoch 0 comparator; Epoch 4A |
| O8 correctness-first | v1 README; architecture write constraints | C-01, C-04, E2-01, E3-02, E3-04 | Mandatory; read policy unresolved, writes closed | README §1/§6; Contract §3/§5; Epoch 3; Verification |

## 2. Accepted and modified findings

`AWM` = ACCEPT WITH MODIFICATION.

| Finding | Verdict | Arbitration requirement | Resulting v2 section |
|---|---|---|---|
| R-01 | AWM | Choose host and operation/capability/write matrix | README §4; Epoch 0 §work; Epoch 1 capability; U-ARB-05 |
| R-02 | AWM | Versioned dependency evidence; unknown rejected before hit | Contract §3.2/§5; Epoch 0; U-ARB-06 |
| R-03 | ACCEPT | Commit/source baseline and A-* lifecycle | README §2; Epoch 0 item 1 |
| R-04 | AWM | Base/requested-effective overlay/readiness split | README §2; Contract §1/§2/§5 |
| R-05 | AWM | E2 checkpoint; O4/O5 mandatory for completion | README §1/§4/§5; Epoch 2 |
| R-06 | AWM | Named workload; required inner instances gate | README §3; Epoch 0; U-ARB-03 |
| C-01 | ACCEPT | Positive/absent/region/target evidence | Contract §3.2/§5; Verification §3 |
| C-02 | AWM | Discovery→coverage→capture→validation | Contract §5/§6; Verification §4 |
| C-04 | ACCEPT | Formal completeness independent of diagnostics | Contract §1/§3.3; Verification §3 |
| C-05 | AWM | Separate source/policy/readiness; no session-ID rebinding | Contract §1/§2/§5; U-ARB-04 |
| C-06 | AWM | Keep significant hashes; measure build invalidation | Contract §4; Verification §5 |
| C-07 | AWM | Explicit roots/budget/bounded fallback | Contract §8; Verification V16/V18 |
| C-08 | AWM | Reader ownership/lazy lifetime/safe cleanup | Contract §7; Epoch 2 store; Verification V17/V18 |
| C-09 | AWM | Effective decoding and round-trip oracle | Contract §4; Epoch 1 acceptance; Verification V02 |
| E0-01 | ACCEPT | Host/write feasibility first gate | Epoch 0 items 2–4 |
| E0-02 | AWM | Detector and positive/negative/not-run split | Epoch 0 items 5–6; Verification §3 |
| E0-03 | AWM | Full in-process generated-output oracle | Epoch 0 item 7; Verification §1/V04 |
| E0-04 | AWM | Experiment vs production; full e2e budget | README §5; Epoch 0 item 10; Verification §5/§6 |
| E0-05 | AWM | Metadata remains separate comparator | README §1/§4; Epoch 0 item 8; Epoch 4A |
| E1-01 | AWM | Operation/capability/writer/result matrix | Epoch 1 capability matrix |
| E1-02 | ACCEPT | Full transition/ownership table | Contract §6; Epoch 1 lifecycle |
| E1-03 | AWM | Sync memberships separate from query context | Contract §3.1; Epoch 1 memberships |
| E1-04 | AWM | Preserve session-sticky admission | Contract §2; Epoch 1 memberships/overlay |
| E1-05 | AWM | Series-wide no-project-write reconciliation invariant | Epoch 1 memberships/writes; Verification V19/V20 |
| E1-06 | ACCEPT | Ordinary load and hydrate regression matrix | README §2; Epoch 1 acceptance |
| E1-07 | AWM | Separate hydration and admission tables | Contract §3; Epoch 1 data evidence; Handoff §4/§5 |
| E2-01 | AWM | Hit only through C-01 protocol; retain V12 | Epoch 2 load; Verification V12 |
| E2-03 | ACCEPT | Policy/source/capture/write status split | Contract §1/§9; Epoch 2 observability |
| E2-04 | AWM | E2 unchanged checkpoint; O5 still required | README §1/§4; Epoch 2; Epoch 4C |
| E2-06 | AWM | Store checkpoint separate from activation | README §5; Epoch 2 checkpoints; Verification §6 |
| E3-01 | AWM | Exact under-lock workflow and concurrency tests | Contract §6; Epoch 3 locking; Verification §4 |
| E3-03 | ACCEPT | Role/coverage event classifier | Contract §8; Epoch 3 classifier |
| E3-05 | AWM | Content-only only for proven document role | Contract §8; Epoch 3 classifier |
| E3-06 | AWM | Separate RAM freshness/disk validity/cadence | Contract §6; Epoch 3 states; U-ARB-01 |
| E3-07 | ACCEPT | Watch/probe coverage and limits | Contract §8; Epoch 3 coverage; Verification V19/V22 |
| E3-08 | AWM | Own-write bytes/hash/revision and pending state | Epoch 1 writes; Epoch 3 own writes; Verification V20/V21 |
| E4-02 | AWM | Effective mode and producer-reader compatibility | Contract §1/§9; Epoch 4B |
| E4-03 | AWM | Per-tool partial coverage or refusal | Epoch 4A; Verification V24/V25 |
| E4-04 | ACCEPT | Discovery coverage distinct from CLI target | Epoch 4A; Verification V24a/V24b |
| V-02 | AWM | Split V11 reasons and add non-member control | Verification V11a–V11d |
| V-03 | AWM | Full oracle for supported SG; admission-only rejected | Epoch 0; Verification §1/V04 |
| V-04 | AWM | Named workload budget/hit-rate before activation | README §3; Verification §5/§6 |
| H-01 | AWM | Five decision authorities and exact next actions | README §5; all epoch handoffs; Handoff §1/§10 |

## 3. Rejected findings — closed behavior

| Finding | Verdict | Closed recommendation | Preserved v2 section |
|---|---|---|---|
| C-03 | REJECT | Do not remove absent/explicit distinction or alter key on this claim | Contract §2; Verification V15 |
| E2-02 | REJECT | Do not replace conjunctive acceptance with DTB-only gate | Epoch 2 activation; Verification §6 |
| E4-01 | REJECT | Do not remove 4C as inherently forbidden interpreter | README §1; Epoch 4C |
| V-01 | REJECT | Do not require oracle to use production hydrate host type | Verification §1 |

## 4. Unresolved findings

| Finding | Verdict | Preserved gate | Resulting v2 section |
|---|---|---|---|
| E2-05 | UNRESOLVED | U-ARB-01 capture schedule/cadence | Contract §6; Epoch 2 step 8; UNRESOLVED-v2 |
| E3-02 | UNRESOLVED | U-ARB-02 freshness cadence/overhead | Epoch 3 unresolved contract; UNRESOLVED-v2 |
| E3-04 | UNRESOLVED | U-ARB-02 stale/unknown availability | Epoch 3 unresolved contract; UNRESOLVED-v2 |

## 5. Accepted product constraints

| Constraint | Source | Resulting v2 sections |
|---|---|---|
| A-LOAD | analyzer-shadow U-ARB-04 | README §2; Contract §5/§6; Epoch 1/3 |
| A-STICKY | analyzer-shadow U-ARB-05 | README §2; Contract §2; Epoch 1 |
| A-WRITE | analyzer write boundary/v3 | README §2; Epoch 1/3 |
| A-ADMISSION | analyzer v3 gates | README §2; Contract §5; Epoch 1 |
| A-PROVENANCE | analyzer capture design | README §2; Contract §5; U-ARB-04 |
| A-LOADER | current architecture | README §2; Contract §7 |

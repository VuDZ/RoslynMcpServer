# Epoch N — Handoff v2

> Шаблон отчёта. Наличие файла не означает shipped.

## 1. Decision authorities

- Experiment allowed: yes / no; authority/evidence:
- Implementation allowed: yes / no; authority/evidence:
- Public activation allowed: yes / no; authority/evidence:
- Next epoch allowed: yes / no; exact permitted actions:
- Series complete: yes / no; O1–O8 closure:
- Commit/source version/running MCP/date:

Одно `go` не используется. Отклонение не отменяет MUST, O4/O5 или A-WRITE.

Trace: H-01 — ACCEPT WITH MODIFICATION.

## 2. Target and scope

- Mandatory requirement IDs/features for this decision:
- Target solution/size:
- Required SDK/TFM/project instances/generators/imports:
- Overlay/metadata/graph modes:
- Supported, unsupported and unknown detector:
- Agreed requirement/scope changes and authority:

## 3. Host and capability matrix

| Operation | Host capability | Disk writer | Preflight | Result/failure/partial | Reload |
|---|---|---|---|---|---|
| read | | | | | |
| text edit | | | | | |
| add/remove/rename document | | | | | |
| project mutation | | | | | |
| disk reconciliation | | | | | |

Trace: R-01, E0-01, E1-01.

## 4. Hydration data

| Category | DTO/API source | Completeness evidence | Positive test | Limit |
|---|---|---|---|---|
| roots/instances/edges | | | | |
| options/documents/references | | | | |
| generated/analyzer inputs | | | | |
| decoding/write policy | | | | |

## 5. Admission evidence

| Category | Evidence source/version | Coverage/negative evidence | Admission test | Unknown behavior |
|---|---|---|---|---|
| positive dependencies | | | | |
| known absent | | | | |
| wildcard/negative regions | | | | |
| target inputs/current toolset | | | | |

Trace: R-02, C-01, C-04, E1-07.

## 6. Lifecycle, ownership and persistence

- State-transition table artifact:
- Candidate/old session ownership and cancellation:
- Prepare/gate/publication result:
- Reader ownership/lazy read lifetime/cleanup:
- Own-write revision and partial persistence:
- A-LOAD/A-STICKY/A-WRITE/A-ADMISSION/A-PROVENANCE/A-LOADER evidence:

## 7. Verification

| Test ID | Class | Result | Artifact/reason |
|---|---|---|---|
| | positive equivalence / negative admission / fault / perf | passed / failed / not run | |

Unsupported/not-run не засчитываются как positive equivalence.

## 8. Performance

- Workload and pre-approved budget:
- Raw data:
- Median/p95 load and first-semantic:
- Hit-rate over all attempts and miss overhead:
- DTB, probe/capture/prepare, changed categories, bytes, peak memory:
- Gate result:

## 9. Compatibility and migration

- Schema/producer and reader compatibility:
- Public API/default/Description impact:
- Store namespace/lifetime and migration:
- Product README/AGENTS/ARCHITECTURE/version changes actually shipped:

## 10. Unresolved and next actions

- Applicable U-ARB items and evidence still required:
- POST-ARB items:
- Exact allowed next actions:
- Explicitly forbidden actions:

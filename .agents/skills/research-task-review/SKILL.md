---
name: research-task-review
description: >-
  Reviews the result of a documented research task (inventory, spike, measurement,
  design input) against its task file and the current code: verifies key claims in
  the source, checks completeness with independent searches, flags buried findings,
  stale numbers and untracked bugs, and checks status bookkeeping. Use when the user
  asks to review, assess or give an opinion on the evidence/result of a docs task
  (task-NN, evidence/*.md) rather than on a code diff.
---

# Research task review

The target is a research result: a task file under `docs/<topic>/epoch-*/task-NN-*.md`
and its evidence report. The question is not "is the text consistent" but "can the
next task and the owner rely on it". A plan review with zero findings and no checks
against code is a weak signal; do not produce one.

## Inputs

1. `AGENTS.md`, the task file, its epoch README and topic README (scope, open
   questions `U-*`, compared models).
2. Every evidence file the task links, in full.
3. The commit the evidence was written against, and the current `HEAD`. Note the
   base drift (server version in `RoslynMcpServer.csproj`, merges since then).
4. `docs/README.md` section «Учёт задач, ревью и статистики» for bookkeeping rules.

Do not read other reviewers' results before forming your own findings.

## Checks

1. **Spec coverage.** Every mandatory category, scenario and deliverable of the task
   file is present, or its absence is stated with a reason. Count-style claims
   (e.g. "54/54 tools") are recomputed from the source, not trusted.
2. **Independent completeness probe.** Run at least one search the author did not
   describe, aimed at what is easy to miss. For a process-state inventory:
   `SetCurrentDirectory`, `SetEnvironmentVariable`, `AppContext.SetSwitch`,
   `AppDomain`/`AssemblyLoadContext` events, `Console.Set*`, `[ThreadStatic]`,
   `AsyncLocal`, static mutable collections, background tasks and timers. Exclude
   tests, spikes, samples and `forks/`. Report what was searched and what was found.
3. **Spot-check claims in code.** Pick the 3–5 claims with the largest consequence
   for later decisions (blockers, "impossible", "leaks", "not isolated") and verify
   each at `file:line`. List them as verified, refuted or unverifiable.
4. **Buried findings.** A defect found along the way (leak, race, wrong default)
   must appear in a summary and be tracked: fixed with a link, or recorded as an
   item with an owner. "Out of scope of this docs-only task" without a tracking
   item is a finding.
5. **Summary for the reader.** The report starts with a short «Главное» section:
   blockers, surprising facts, bugs found, decisions the owner must make, effect on
   each compared model. Long tables without it are a finding (minor), because the
   owner and the next task cannot act on them.
6. **Decisiveness.** Classifications with several labels per row ("сессия /
   идентичность / конфликт") are acceptable in an inventory, but the summary must
   say which rows block a design choice. Unknowns have a reason and a receiver task.
7. **Numbers and observations.** Every number has a source: measured (method,
   base version, date), owner's observation, or estimate. Numbers from an older
   base or another configuration (e.g. before a GC mode change) are marked stale.
   An owner's observation is not a measurement and not a hypothetical example either.
8. **Model neutrality.** Conclusions that only hold for one compared model are
   labelled with that model. Findings that apply to every model (e.g. state that
   must become per-session) are pulled out, since they are the safest next work.
9. **Validation claims.** Docs-only tasks need link/diff checks. If code or tests
   changed, the AGENTS.md suite must have run. Zero-test runs, sandbox aborts and
   timeouts are not passes; unrun platforms are listed, not passed.
10. **Bookkeeping.** Task header, epoch README, topic README and `docs/README.md`
    agree on status, date, accepted scope and evidence links. Review counters use
    `—` before a review and `0` only after one. Executor and reviewer identities
    are real or stated as unavailable, never invented.

## Severity

- **blocker** — the next task or the owner would decide on a false or missing fact.
- **major** — a mandatory part of the spec is missing, a key claim is refuted, or a
  found defect is untracked.
- **minor** — usability, stale-number labelling, bookkeeping inconsistency.
- **suggestion** — improvement that the spec did not require.

## Output

Write the review in the topic's language (Russian for `docs/`). Unless the user
asks for chat only, save it next to the topic:

```
docs/<topic>/review-<task-or-scope>-<YYYY-MM-DD>/
  result.md     verdict, «Главное», findings table, verified claims, recommendations
  coverage.md   what was read, searched and run; what was not, and why
  .status       one line `completed`, written last
```

`result.md` findings table: `ID | Severity | Где | Что не так | Что сделать`.
IDs are stable (`F-01`…) within the reviewed scope. Do not edit the reviewed
documents or code; the coordinator applies fixes and updates the counters.

In chat, give the verdict first, then at most the top findings and the
recommended next step. Do not restate the task.

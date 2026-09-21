---
name: epoch-subagent-loop
description: >-
  Implements a docs epoch series by launching a subagent, reviewing the diff,
  sending must-fix findings back to a subagent until the review accepts, then
  committing and continuing to the next epoch when the series says so. Use when
  the user asks to implement docs epochs with subagents, a review-fix loop, and
  a commit between epochs.
---

# Epoch subagent loop

запуск задачи сабагентом, ревью изменений, если нужны доработки - исправление саб агентом, опять проверка, если нужны доработки - повторить сабагент/ревью и так до завершения, потом коммит и следующая эпоха, если указано

The parent orchestrates. It does not implement the epoch itself.

## Order

1. Read the series README. Take the first epoch whose status is not done. Do not start the next epoch until the previous one is committed. The series README wins if it says the epochs share files.
2. Launch one `generalPurpose` subagent (`run_in_background: false`). The prompt must include the epoch file path, the repo root, and every constraint below. The subagent has no chat history.
3. Parent runs the build and the tests the epoch names, via Roslyn MCP (`run_dotnet_build`, `run_specific_test`). No shell `dotnet build` / `dotnet test`.
4. If the build or tests fail, resume the same implementer with the failure text. Repeat until green, at most 3 build-fix rounds. Then stop and report.
5. Launch a **new** `generalPurpose` reviewer (do not resume the implementer). It reads the epoch spec and the diff of files this epoch touched. Pre-existing dirty files are out of scope.
6. The reviewer returns `ACCEPT` or a must-fix list. Nits are not must-fix. Spec violations, missing tests, and behavior bugs are must-fix.
7. If must-fix: resume the implementer with that list only, re-run the tests, then a fresh reviewer. Repeat until `ACCEPT`, at most 4 review rounds. Then stop and report.
8. Commit only this epoch's files. Do not commit unrelated dirty files. Do not push.
9. If the series README lists a later epoch, go back to step 2.

## Implementer constraints

- Edit with the IDE. Do not use MCP `apply_patch` / `update_file_content`.
- Do not commit. Do not start the next epoch.
- Do not rewrite the epoch spec. A one-line status flip to done is allowed only after the parent says the review accepted.
- Touch only files the epoch lists, plus tests that fail because those files changed.
- Version bump (`Version`, `AssemblyVersion`, `FileVersion` together; not `PackageVersion`) only after the parent reports a green build, and only when the epoch asks for one.
- Return: files changed, what was left unbumped, and anything the parent must measure (catalog byte counts).

## Reviewer output

```
ACCEPT
```

or:

```
MUST-FIX
- file:line — what is wrong relative to the epoch spec
```

No praise, no restating the spec.

## Commit

Imperative subject, why rather than what. One epoch per commit. On PowerShell pass the message as a here-string, not a bash heredoc.

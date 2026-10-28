---
name: research-task-authoring
description: >-
  Writes or revises research task files and epoch plans under docs/<topic>/ (inventory,
  spike, measurement, design decision): go/no-go checks first, the problem measured and
  a zero option compared, known primitives as base hypotheses, a «Главное» summary in
  every result, found bugs tracked, and the repository's header and bookkeeping rules.
  Use when the user asks to plan research, add or rewrite a task-NN file, or turn a
  discussion into tasks for agents.
---

# Research task authoring

A research task exists to unlock a decision. Write the decision first, then the
cheapest evidence that can change it. Volume of text is not rigor: a short task with
sharp questions and checks beats a long one that restates the scope.

## Before writing

1. Read `AGENTS.md`, the topic README (scope, open questions `U-*`, compared
   models) and `docs/README.md` «Учёт задач, ревью и статистики».
2. Separate what is known from what is assumed. For each number, record whether it
   is measured (method, version, date), an owner's observation, or an estimate.
   An owner's observation of a real situation is not a hypothetical example.
3. Check facts that shape the task in the code, at `file:line`, before writing them
   down. Do not plan around a constraint you have not seen.

## Plan shape

- **Go/no-go first.** Put the cheap checks that can kill or redirect the topic
  before mechanism spikes: does the environment allow it (harness, OS, process
  lifetime), how big is the real problem and what is it made of. A spike whose
  scope depends on the answer depends on that task.
- **Compare against a zero option.** Every design task compares the favourite
  approach with doing nothing and with the simplest improvement of the current
  mode, on the same criteria. A model the owner has not chosen may be listed as an
  alternative, marked as needing the owner's explicit decision.
- **Name what is shared by all options.** Work that every option needs (for
  example, splitting process-wide state into per-session and per-workspace) is
  called out; it is the safest next step.
- **Start from known primitives.** When a standard mechanism likely solves part of
  the problem (OS file lock or named mutex for single ownership, the protocol's own
  session handling), make it the base hypothesis the spike checks, and require a
  stated reason before inventing a protocol.
- **Decision points.** After a go/no-go task, write who decides what, and record
  the owner's choice in the topic README before dependent tasks start.

## Task file

Name: `task-NN-<description>-<low|med|hi|xhi>.md`. Numbers follow creation order;
execution order is given by `Depends on` and the epoch README. Header exactly as in
`docs/README.md` (recommended models, reasoning, complexity, risk with reason,
abilities, status `planned`, dependencies, empty actual-executor and review fields).

Sections, in the topic's language (Russian for `docs/`):

1. **Цель** — the decision this unlocks and the 2–5 questions to answer.
2. **Входы и границы** — documents, code areas, what must not change, required
   reading (`docs/code-style.md` before any C# edit).
3. **Что сделать** — numbered checks or experiments; for each, what observation
   would change the decision. Known fixed facts go here as facts, with `file:line`.
4. **Конкретный результат** — the evidence file path and its required parts.
5. **Проверки** — acceptance checks and validation per `AGENTS.md` (docs-only:
   link and diff checks; code: SourceStructure, Release build, main suite, and
   AnalyzerLifecycle when the lifecycle paths are touched).

Require in every result:

- **«Главное» at the top**: up to ten bullets — blockers, surprising facts, defects
  found, decisions for the owner, effect on each compared model.
- **Found defects tracked**: a bug found during research is either fixed with a
  regression test in a separate change or recorded as an item with an owner and a
  link. "Out of scope" alone loses it.
- **Unknowns** with a reason and the task that receives them.
- **Base version** (commit, server version) the evidence applies to; numbers from an
  older base are labelled as such.
- Measurement tasks: method, configuration that affects the number (e.g. GC mode),
  n of runs, and unverified platforms listed rather than passed.

TDD fits tasks that implement a behavioural contract (spikes, integration). For
observation tasks (harness behaviour, memory breakdown) say that the red/green cycle
does not apply and require reproducible steps instead.

## Style

- State each disclaimer once, in the topic README (for example "this is not design
  acceptance"); task files link to it instead of repeating it.
- One requirement per sentence; no paragraph-long table cells.
- Do not promise savings or results before measurement.
- Status bookkeeping follows `AGENTS.md`: after completing or adding tasks, update
  the task header, epoch README, topic README and `docs/README.md` row consistently,
  with date, accepted scope and evidence link.

## After writing

Run link/anchor and `git diff --check` on the changed Markdown, confirm the
dependencies in the epoch README match the task headers, and write a short evidence
note for the plan change (what changed and why, which review it was not covered by).

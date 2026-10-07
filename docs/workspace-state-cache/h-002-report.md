# H-002 — Применение решения U-001=B

Дата: **2026-10-07**. Статус: **accepted owner decision / applied to requirements**.
Scope: docs-only применение [H-002](human-decisions.md#h-002--ограниченный-strong-name-contract-mvp)
вместе с ранее принятым H-001. [U-001](unresolved.md) **resolved**.

## Изменения

- Зафиксированы решение владельца B, rationale и уточнение про подпись на конвейере.
  Допуск определяется effective developer configuration; корпоративный ключ не
  требуется для MVP spike. CI-only signing не является blanket exclusion локального profile.
- Hydrated scope ограничен подтверждёнными signing-independent проектами/операциями.
  Signing-dependent и unknown requests идут целиком на ordinary load до execution/
  side effects; невозможность перехода даёт отказ. Emit/signing из hydrated compilation
  исключены. Semantic/write correctness и remaining required state сохраняются.
- Не допускаются private reflection, silent loss, изменение signing flags/csproj/refs
  ради pass. Подписанные внешние DLL не исключаются автоматически. Effective options,
  assembly attributes и bindings учитываются в admission; успешного symbol query мало.
- Добавлено последующее требование владельца к явным логам H-002: stage, project/
  configuration/TFM, operation, конкретная причина и ordinary/refusal route.
  Общий cache miss недостаточен; ordinary failures и secrets обрабатываются отдельно.
  S-001 проверяет reason/route, E1 — actual log records.
- Согласованы root/epoch READMEs, affected E0/E1/E2 specs/tasks, execution, spikes,
  benchmark, decision/status summaries, docs index и проверяющий script.
- Historical archive, исходные evidence/manifests, task IDs, зависимости и review
  statistics не переписывались. Предыдущие статусы H-001/relocation обозначены как исторические.

## Validation

Author consistency/application check: `python docs/workspace-state-cache/validate-artifacts.py`.
Итог: **passed**; whitespace check exit 0.
Машиночитаемый итог: [validation.json](validation.json); охват и порядок проверки:
[validation.md](validation.md). Diff whitespace: `git diff --check -- docs`.
Проверяются ссылки/anchors, 35 task headers и 53 dependency edges без cycles,
14 P / 16 source findings / 15 ARB, неизменность 129 archive inputs и unrelated
tracked diff, U-001 resolved B и S-001/S-002 not-run. Проверены diff и отсутствие
устаревших active strong-name no-choice формулировок.

C# production/tests/build configuration не изменены; .NET build/test suites not-run
как неприменимые к docs-only revision. Проверяющий Python script исполнен.
Independent review not-run; author consistency check не считается review round.

## Оставшиеся gates

Task-00 planned; S-001/S-002 not-run. Decision prerequisite S-001 выполнен, но actual
capability verdict отсутствует. Full cross-process task-06, dependent implementation
acceptance и production activation не следуют из H-002. Все 35 tasks остаются planned.

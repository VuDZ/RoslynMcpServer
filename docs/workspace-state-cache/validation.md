# Validation specification v2

Обновлено: **2026-10-08**. Scope: artifact consistency после isolated S-001.
Runtime build/tests — отдельно в [task-08 validation](epoch-0-snapshot-roundtrip/evidence/task-08-validation.json).
Итог и exact counts сохраняются в [validation.json](validation.json).
[Проверяющий script](validate-artifacts.py) запускается из repo root:
`python docs/workspace-state-cache/validate-artifacts.py`.

Проверяются archive inventory и path/hash mapping исходных 129 files, ссылки/anchors
всего active/archive package и docs index, 35 task headers/suffixes/statuses: task-00 accepted, task-08 accepted, остальные planned, зависимости и task maps,
14 P records/source traceability, U-001 resolved по H-001/H-002 (B),
S-001 accepted для isolated base-query slice, S-002 not-run и сохранность unrelated tracked diff.
Whitespace revision проверяется script по всем собственным text files; tracked
docs index — `git diff --check -- docs/README.md`.

Textual consistency pass сохранил порядок U-001→S-001→positive E0 и
S-002→accepted E1 manifest, отдельно globals/environment/dependency source/generation
binding, fresh mode admission/portable health/roles и оба E2 eligibility predicates.
Schema sketch не создаёт cycle. Existing corpus/n/timings/fast-binary blind spot,
optional E3 и runtime permissions не изменены. Это не новое finding adjudication.

U-001 resolved по H-001/H-002 (B); [H-002 report](h-002-report.md) сохраняет
историческую docs-only validation. Task-08 accepted для isolated base-query slice, round 2;
[review](epoch-0-snapshot-roundtrip/evidence/task-08-review-round-2.md). Recheck спецификации pending.
Completed artifact check не заменяет приёмку эпохи.

До relocation author check после H-001: passed, 129 original files unchanged,
56 Markdown files / 651 links / 20 anchors. Final check после переноса сохраняется
в [validation.json](validation.json): **35 headers / 53 dependency edges**, cycles нет.
Archived inputs сверяются после механического link rebasing; historical raw snapshots
сохранены. [Relocation report](relocation-report.md) содержит порядок проверки всех 188 moved files.
Покрыты **14 P / 16 source findings / 15 ARB**; unrelated tracked diff unchanged,
docs index diff-check exit 0, trailing whitespace 0, errors 0.
На предварительном прогоне исправлен matcher task-map validator, который принимал
dependency links за primary task entries; initial validation.json создан до final check.

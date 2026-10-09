# E1/task-11 — независимый recheck, round 3

Дата: **2026-10-09**, Europe/Moscow. Рецензент: отдельный Codex reviewer subagent
`final_review`, fresh session, унаследованная модель; точный runtime model
ID/reasoning не подтверждены инструментами. Reviewer работал только на чтение;
build/tests не запускал. HEAD: `da557044333e7c0a80ffa8d7ca25462a22be51cd`.

Итог: **ACCEPT bounded test-only S-002 partial/negative experiment**.
**T11-R2-001 CLOSED**. Открытых must-fix findings: **0**.
Это independent review verdict; окончательная validation принадлежит координатору:
[итоговый recheck/validation](task-11-recheck-validation.md).

## Проверка исправления

Прочитаны task/spec/README/common execution/spikes, applicable accepted E0/task-07
outputs и полный code style до C# style comments. Scope: три test-only файла,
их reports и targeted JSON; посторонние dirty файлы не оценивались как task-11.

`Directory.Exists` guard выполняется до `Directory.GetFiles` необязательного
configuration directory. Theory `Owned_real_copy_accepts_a_single_configuration_build_tree`
покрывает Debug-only и Release-only деревья, проверяет exact copied generated
и assets bytes, отсутствие другой configuration. Fix устраняет обнаруженный
`DirectoryNotFoundException`, сохраняя mandatory top-level restore inventory.
Новых behavior/style must-fix нет.

## Независимая сверка fresh evidence

Проверены прежние measurements и [свежий result](task-11-recheck/focused/result.json)
после исправления. PID **70504**; controlled/real imports **224/228**, в каждом
log **1** EvaluationFinished и **4** ProjectStarted. Imports относятся к evaluation
**4/submission 0**; root evaluation **3** и dependent evaluation **9/submission 1**
остаются без import closure. Nested/shared/conditional/external/absent controlled
evidence сохранено; redirected assets связаны с build contexts обоих consumers.

Real workspace содержит два projects, diagnostics пусты, SamplePlugin symbol
найден. Все **234** [real-copy hashes](task-11-recheck/focused/real-copy-hashes.json)
сверены с текущими source bytes: **0 mismatches**. Result остаётся **partial**,
manifest readiness **blocked**, RealSupportedManifest=false, P-004 **unproven**.
Path-level negative check не заявлен all-instance admission.
Production/default/provenance/activation scope не расширен.

## Проверенные C# identities

- Capture: `29C52C6652218621C395DC0A0DFD7BEBA4D9B30E0545FF858C1522373E68D8C1`.
- Fixture: `95BD6198981BD8FC816E7704BBB5B1E885F92F900DF5F83F952CD6B9A1022229`.
- Tests: `B29508143B5E7762B3CE9E6F8683259D4F36CA6DA1B65CB8AEFE36BB6C93A9B3`.

Прежние 1360 tests не засчитаны как final validation исправления. Reviewer получил
от координатора новый Release build pass и focused 3/3 pass; final main suite и
task/index/execution bookkeeping закрываются координатором отдельным отчётом.
Supported manifest, task-00 implementation и reusable envelope остаются blocked;
приёмка E1 не объявляется.

# E5-S4 — приёмка provenance на fixture эпохи 1

Дата: 2026-09-12. Статус: **реализовано в v1.3.12; независимая приёмка
[принята](epoch-5-s4-acceptance.md)**.
Норматив: [epoch-5-reference-provenance.md](epoch-5-reference-provenance.md) E5-S4.
Политика U-ARB-01 (capture + confirmed-only) уже выбрана; inaccessible **не**
выбирался. Публичная MCP schema не расширялась.

## Реализация

- Lifecycle `RewriteDto` пробрасывает `ReasonCode`, original/source path
  states, `SelectedSourcePath` и `SelectionBasis` из `RewriteResult`
  (E5-S3-2). Публичный ответ `load_workspace` по-прежнему компактный текст.
- Host op `forceAccessFailure` включает test seam
  `RemainingForcedAccessFailures` до prepare, чтобы отличить
  `access_failure` от `source_output_missing` на той же redirected fixture.
- `Epoch5S4AcceptanceTests` оракулит exact executed markers и reason codes
  на фикстурах эпохи 1. Отсутствие generated ошибки само по себе не
  считается успехом.

## Проверка

Независимый прогон (локальный x64 `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`, SDK 10.0.204, Debug `--no-incremental`;
MCP был offline):

| Тест | Marker / исход | Reason code | Path states |
| --- | --- | --- | --- |
| Redirected missing-path | exact `V1`, не `FOREIGN` | `reference_rewritten` | original `Missing`, source `Exists` |
| Foreign existing same-name | exact `FOREIGN`, не `V1` | `provenance_unconfirmed` (не `proven_foreign_path`) | original `Exists`; path = `external\Generator.dll` |
| Foreign missing same-name | `no-constant`, не `V1` / не `FOREIGN` | in-solution `reference_rewritten` + missing `provenance_unconfirmed` | missing original сохранён |
| Source output отсутствует | не `V1` | `source_output_missing` | source `Missing`; не unconfirmed и не access |
| Injected access | не `V1` | `access_failure` | source `AccessFailure`; не missing |
| Failed capture | не `V1` | `provenance_unconfirmed` | 0 confirmed, 0 Applied |

- `Epoch5S4AcceptanceTests`: **6 passed / 0 failed**
- `AnalyzerReferenceShadowCopierTests`: **11 passed / 0 failed**
- `AnalyzerProvenanceBindingTests`: **3 passed / 0 failed**

Configuration/TFM по загруженным inner projects закрыты ранее
[E5-S2-1](epoch-5-s2-acceptance.md) / `F09ProductionCaptureTests`
(exact `V1` для каждого consumer в отдельном host).

Исходный missing-path repro исправляется: provenance достаточен, rewrite
подтверждён, исполняется `V1`. Сужение поддержки (Alt-3) не выбиралось.
Внешний same-name analyzer не подменяется in-solution кандидатом.
Неоднозначные проекты по-прежнему skip (`ambiguous_assembly_name` в
unit-тестах copier). Автосборка генераторов и универсальное восстановление
MSBuild-путей вне эпохи.

## Inaccessible

Отдельное действие U-ARB-01 не выбрано. `access_failure` — reason code и
path state; это не missing file и не foreign. S4 только фиксирует различие
на fixture и не назначает skip/rewrite политику для ACL/lock.

Открыто, не блокер: E5-S3-1. Inaccessible и sticky не выбирались.

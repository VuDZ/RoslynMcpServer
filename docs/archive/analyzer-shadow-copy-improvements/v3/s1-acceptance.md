# Приёмка v3 S1 — regression baseline

Дата: 2026-09-12. Вердикт: **принимается как красный baseline, не как фикс.**
Три постоянных теста описывают безопасный контракт и на v1.3.15 падают
именно на R1–R3. Самоотчёт в `s1-regression-baseline.md` не заменяет прогон.
Норматив: [s1-regression-baseline.md](s1-regression-baseline.md).

Независимый прогон (локальный `C:\Program Files\dotnet\dotnet.exe`,
`DOTNET_MULTILEVEL_LOOKUP=0`, SDK 10.0.204):

- `dotnet build` Debug `--no-incremental`: success
- `V3RegressionBaselineTests`: **3 failed / 0 passed** (~24 с), не skip/timeout

| ID | Тест | Отказ независимого прогона | Статус |
| --- | --- | --- | --- |
| R1 | `V3_R1_same_session_stale_candidate_is_rejected_before_any_write` | `ReconciliationSucceeded` / `try-apply-rejected`; `saved=1`; диск и published = held A | **закрыт** как воспроизведение |
| R2 | `V3_R2_prepare_failure_stays_fail_closed_after_text_edit` | После load fail-closed держится; после edit `realPublished/realLoaded/realProcess=True` | **закрыт** как воспроизведение |
| R3 | `V3_R3_corrupt_capture_does_not_publish_or_execute_real_output` | `capture=Failed`; published = real `Generator.dll` на load | **закрыт** как воспроизведение |

Assertions требуют `PreflightRejected` + пустые SavedPaths + текст B;
отсутствие real path/marker; для R3 ещё `no-solution`. Это целевое
безопасное поведение, не закрепление дефекта.

`publishedDocument` / default oracle идут через `GetPublishedSolutionAsync`.
`oracleSource=workspace` нет. Host/fixture: отдельный процесс,
`SdkDefaultCorrectPath`, build генератора до load. Compact failure UX есть.
Production/MCP schema не менялись; bump не нужен.

Low, не блокер: R3 oracle не достигается, пока падает load; R2 даёт
`no-constant`, не exact `V1`. Безопасность опровергается путями.

Не выпускать этот красный набор как ship. Следующий шаг — S2 (R1).

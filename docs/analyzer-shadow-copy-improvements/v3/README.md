# Analyzer shadow copy — исправления v3

Дата: 2026-09-12. Статус: **v3 принята в исходниках v1.3.20** (S1–S6, S8;
S7 повтор после S8). Первый прогон S7 на 1.3.19 [не принят](s7-acceptance.md)
(S7-1); закрыт в S8. U-ARB-01 inaccessible original — **skip** с v1.3.21
([решение](../v2/u-arb-01-inaccessible-skip.md)). Опубликованный MCP process
всё ещё **1.3.15**.
База ревью: код v1.3.15 и [спецификация v2](../v2/README.md).

v3 устраняет потерю свежих изменений при записи и обход fail-closed после
opt-in load. Это продолжение v2, а не заявление, что её прежняя приёмка
покрыла перечисленные ниже сценарии.

## Результаты ревью

| ID | Приоритет | Подтверждённое поведение v1.3.15 | Исправление |
| --- | --- | --- | --- |
| V3-R1 | P1 | Candidate той же сессии, созданный до другой записи, перезаписывает свежий текст. Результат: `ReconciliationSucceeded`, `try-apply-rejected`; на диске старый текст | S2 |
| V3-R2 | P1 | После первой ошибки prepare опубликован безопасный snapshot, но следующий text edit возвращает real analyzer reference. Oracle исполняет V1 из `Generator/bin/Debug/netstandard2.0/Generator.dll` | S3 |
| V3-R3 | P1 | Corrupt provenance capture + opt-in load оставляет real reference в semantic snapshot. Oracle исполняет V1 из real build output | S4 |
| V3-R4 | Документация | E2 допускает original после ошибки, U-ARB-04 требует fail-closed; A4-09 объявлен неблокирующим; E5 требует inaccessible до уже принятого rollout | S6 |
| S7-1 | P1 | После S5 полный ban публикует `LoadFailed` / `opt-in-prepare-not-enabled` вместо `DependencyUnsupported` (helper / main-only) | S8 |

В ревью прошли 36/36 выбранных unit-тестов и 9/9 штатных
`UArb04LoadBoundaryEvidenceTests`. Три временных regression-теста завершились
ошибками на целевых assertions и подтвердили R1–R3. Временные тесты удалены;
эти наблюдения не являются сохранённым regression suite. S1 восстанавливает
постоянные воспроизведения с теми же проверяемыми свойствами.

Частичный prepare и переходы между состояниями требуют дополнительной
проверки в S5. Они не объявляются отдельными воспроизведёнными дефектами.

## Порядок работы

Каждый файл ниже описывает ровно один шаг. У шага есть собственные границы,
результат и критерии приёмки; вложенных этапов нет. Выполнение последовательное.

| Шаг | Единственный результат | Зависимость |
| --- | --- | --- |
| [S1 — regression baseline](s1-regression-baseline.md) | Постоянные воспроизведения R1–R3 | Нет |
| [S2 — freshness перед записью](s2-write-base-freshness.md) | Stale candidate отклоняется до side effects | S1 |
| [S3 — устойчивый fail-closed](s3-persistent-publication-state.md) | Запрет raw publication переживает edit/flush/reconciliation | S2 |
| [S4 — отказ при failed capture](s4-provenance-failure-gate.md) | Без пригодного provenance opt-in semantic snapshot недоступен | S3 |
| [S5 — частичный prepare и переходы](s5-partial-prepare-transitions.md) | Безопасная публикация при смешанном результате подготовки | S4 |
| [S6 — согласование документации](s6-contract-alignment.md) | Единый актуальный норматив и честные статусы приёмки | S5 |
| [S7 — итоговая приёмка](s7-runtime-acceptance.md) | Зафиксированные результаты всей v3 ([1.3.19 не принято](s7-acceptance.md); [1.3.20 в исходниках принято](s7-acceptance.md)) | S6 |
| [S8 — сохранить execution gate](s8-preserve-execution-gate.md) | `DependencyUnsupported` / main-only переживает полный ban публикации | S7 |

## Общие ограничения

Сохраняются immutable main-only generations, original↔shadow mapping,
confirmed-only matcher, restart-required для неподдержанного in-process
обновления и opt-in session-sticky активация. Reset не выгружает CLR.
Load/cache lookup, prepare и publication остаются в одной manager boundary.
Semantic readers используют опубликованный snapshot через сериализованный API.

S3 уточняет поведение отказа: неуспешный opt-in нельзя превратить в неявный
no-overlay посредством edit, flush или cached false/omitted. Выход из запрета —
разрешённый успешный prepare либо новая сессия с явно выбранным режимом загрузки.
Restart-required при этом не обходится повторным prepare.

Новые MCP параметры, ALC, helper discovery, автоматическая сборка генераторов,
GC поколений, merge engine, MVCC и постоянная история snapshots не нужны.
Штамп базы записи и состояние допуска публикации — внутренние данные manager.
Документы описывают желаемое поведение; имена новых внутренних типов остаются
выбором реализации.

Inaccessible original закрыт отдельно в v1.3.21 как skip; v3 его не выбирала.
Failed/corrupt capture по-прежнему не равен inaccessible path. Rewrite и
fail-closed остаются альтернативами, если полевые репозитории потребуют смены.

Принятые leftover Lows (не чинить в S6–S8): S3-1 `LoadCore` cache hit
`_solution ?? CurrentSolution` (это не published accessor);
S4-1 `PublishInMemorySolution(Unavailable)` возвращает raw, публикация
режется `_solution = null`; S5-1 `IsRestartBanLatched` ищет `"restart"`
в BanReason.

Работа с кодом и запуск проверок — через Roslyn MCP. Если инструмент не даёт
пригодного результата, ограничение и использованный fallback фиксируются в
результате шага. Новые runtime-факты не заменять ссылкой на старую приёмку.

После выполнения результат записывается в тот же файл шага: commit, версия,
окружение, команды, фактические результаты и ограничения. До выполнения
раздел результата содержит только «не выполнено». Финальный номер выпуска
выбирается по актуальной версии репозитория, а не заранее из baseline 1.3.15.

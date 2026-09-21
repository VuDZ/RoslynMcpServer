# 2 — Агрегация VSTest-блоков

Pri **P1**. SemVer: **patch**. Желателен п. 1 (иначе ru-RU не парсится).
Блокер для п. 5.
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#2-агрегация-vstest-блоков).

## Цель

На `.sln` с несколькими тест-сборками counts = **сумма блоков**, не склейка
первого `Total` с чужим `Skipped`.

## Файлы

- `Diagnostics/VstestOutputParser.cs`
- `RoslynMcpServer.Tests/VstestOutputParserTests.cs`

## Правка

1. `TryParseEndSummaryLines` — сумма всех `Passed!`/`Failed!` строк, не last.
2. `TryParseTotalTestsBlocks` вместо первого `RxVstestTotalsBlock.Match`.
3. Per-block скан стоп на следующем `Total tests:` (не окно 24 строк + leak
   `TryReadCountAfterTotalTests` по всему тексту).
4. **Fail-closed смесь:** блок `Total > 0` без counts → весь summary `null`
   (`partial`). Не как форк (прибавить total и получить
   `Total != Passed+Failed+Skipped`). `Total tests: 0` без counts — нулевой блок.
5. Сохранить: Total-only все блоки → `null`; `.slnx` fail-only → infer Passed;
   одна сборка без регресса; для не-partial `Total == Passed+Failed+Skipped`.

Порядок источников: end-summary → aggregated totals → line-wise fallback.

## Тесты

- две сборки all-pass — сумма
- 0 совпадений + 3 passed
- fail-only + Passed! end-summary
- оба блока Total-only → null
- смесь Total-only + полный блок → null (наш контракт, не форк)
- одна сборка — регресс
- `.slnx` fail-only infer Passed

## Acceptance

`run_dotnet_test` по многосборочному `.sln` без фильтра = сумма сборок.

## Не копировать

`TempReportWriter`; wholesale parser форка (у нас StdOut/StdErr бюджеты,
pitfall 21 footer).

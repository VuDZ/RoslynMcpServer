# 5 — Успех при `failed==0` и exit ≠ 0

Pri **P2**. SemVer: **patch**. **Только после п. 2.**
Разбор: [../port-candidate-5-nonzero-exit.md](../port-candidate-5-nonzero-exit.md).

## Цель

Фильтр по `.sln`: xUnit sibling `No test matches` даёт exit 1, попавшая сборка
`Passed: 1` — агент видит успех, не `❌ 0 Tests Failed`.

## Файлы

- `Diagnostics/VstestOutputParser.cs` — `BuildMarkdownReport`
- `RoslynMcpServer.Tests/VstestOutputParserTests.cs`

`TestTools` silent-failure путь не ломать.

## Правка

Успех без `&& exitCode == 0`, если: есть summary, `Failed==0`, `Total>0`,
`Total == Passed+Failed+Skipped`, не silent. Exit ≠ 0 → italic note, metadata
как есть.

«no matching tests» только если нет summary или `Total==0` **и** в логе
`No test matches the given testcase filter`. При `Total>0` sibling no-match
не коротит.

## Тесты

Фикстуры из разбора п. 5: xUnit exit 1 + 1 passed; MSTest method-only;
все no-match; restore/`Build FAILED` без summary не успех; реальный fail.

## Acceptance

CsNitra/xUnit-подобный синтетический лог: зелёный заголовок; hung restore —
не успех.

## Не копировать

Слепой `if (failed == 0)`; `TempReportWriter`.

# Task-05 — финальная проверка

**2026-11-19**, Windows x64, .NET SDK **10.0.402**, Release. База `4ca7791`, версия инструмента **1.8.0**.
[Отчёт о реализации](../task-05-implementation-report.md) · ревью раунда 3 — ACCEPT.

## Первый полный прогон

Release-сборка решения через Roslyn MCP прошла. Отдельный фильтр `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`
дал **123/123**. Windows main дал **2617/2618**, 0 пропусков. Единственное падение пришло из
`Surface_sizes_match_recorded_release_numbers`: схема MCP с `depth` и новым текстом выросла на 359 байт
во всех профилях. Измеренные размеры `tools/list` для Full и Lite составили соответственно **47 302**
и **20 865** байт. Обновлены точные эталоны всех профилей и динамического включения `files`, а также
показатели в README. Бюджеты **50 KiB** для Full и **22 KiB** для Lite соблюдены.
Первый TRX — `artifacts/task-05/final/main/main.trx`.

## Повтор на итоговом дереве

`run_dotnet_build` для `RoslynMcpServer.sln` в Release с `noIncremental: true` прошёл с нулём ошибок.
Девять предупреждений относятся к файлам вне task-05. Метаданные собранной DLL подтверждают версии
`1.8.0` и `1.8.0.0`.

`run_test_by_filter` на Release с `noBuild: true`, `noRestore: true` дал **124/124**. Это все **123** теста
SourceStructure и `Surface_sizes_match_recorded_release_numbers`. Инвентарь ограничений структурного
анализа сохранён.

Для полного прогона использован CLI, чтобы записать TRX и проверить число пропусков скриптом workflow.
В обоих запусках выставлены `DOTNET_PROCESSOR_COUNT=2`, `NuGetAudit=false` и `MSBUILDDISABLENODEREUSE=1`.
Команда `dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore`
запущена с фильтром Windows main `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`. Для
AnalyzerLifecycle использованы `Category=AnalyzerLifecycle` и `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.

- **Windows main — 2618/2618**, 0 ошибок и пропусков, 6 мин 12 с.
- **AnalyzerLifecycle — 13/13**, 0 ошибок и пропусков, 59 с.

`Assert-NoSkippedTests.ps1` подтвердил равенство `total`, `executed` и `passed` в обоих TRX. Логи и TRX
сохранены в `artifacts/task-05/acceptance/`. Проверены такие SHA256 итоговых TRX:

```text
main/main.trx             CECD93F45C3132FC0CE3CD3856D059E2E2E185AAE16AE7754A642C2792D4345D
lifecycle/lifecycle.trx   FC3AD81653028C51E2A0764F02E8D1F30F66249AB38F0F4EB9D4163D2F3DAA33
```

Task-05 принята **2026-11-19** для VSTest в версии **1.8.0**. Приёмка охватывает ответ по четырём
ступеням и запуск после сборки. Замеры ширины ответа и стоимости обновления снимков относятся к task-06.

# Review coverage

Дата: **2026-10-29**. Reviewer: **Codex**.
Review directory: `docs/usage-statistics/review-codex/`.

## Reviewed target

Полностью прочитан `docs/usage-statistics/README.md`, 174 строки: Q-01…Q-06, определение workspace, существующее логирование, JSONL и retention, общие поля и семь типов событий, приватность, M1…M6, synthetic cases, критерии, harness-проверка, порядок работ и O-01…O-04.

Фактический target найден в `E:\Devel\RoslynMcpServer-usage-statistics`, ветка `usage-statistics`, HEAD `56e5f67484666fbbb45e79a0eb1b8588bd2b93da`. Рабочее дерево до ревью было чистым. Исходная папка чата `C:\Users\VuDZ\Documents\ChatGPT\Roslyn-usage-statistics` — пустой Git-репозиторий и не содержит target; для ревью использован найденный checkout.

## Context inspected

- `AGENTS.md`: правила scope, validation и task status. C# и task status не менялись; ревью не является приёмкой реализации.
- `docs/ARCHITECTURE.md`: актуальная архитектура, singleton workspace, сериализация операций, lifecycle, lazy load, prewarm, observability, privacy и platform constraints.
- `docs/README.md`: индекс и правила статусов/канона; разделы, относящиеся к текущей теме и учёту.
- `.cursor/rules/roslyn-mcp-docs-lifecycle.mdc`: граница process-материалов и канонической спецификации.
- `PRIVACY.md`, `RoslynMcpServer.csproj`: текущая политика локального сервера, GC, target/runtime и версии logging/MCP packages.
- `Program.cs`, `Hosting/RoslynMcpServiceCollectionExtensions.cs`: startup, Serilog, Generic Host, DI и stdio.
- `Diagnostics/HarnessSessionId.cs`, `HarnessNameResolver.cs`, `ParentProcessProbe.cs`, `ParentProcessSnapshot.cs`, `ToolTelemetry.cs`; регистрация существующего inbound protocol logging.
- `Services/Workspace/SolutionManager.cs`: lazy load, reset, load/prepare, cache hit, наследование свойств, disposal/reopen, failed load, path comparison; целевые участки.
- `Services/Workspace/MsBuildWorkspaceProperties.cs`, `Services/Workspace/WorkspaceLoadSource.cs`, `Services/Build/DotNetConfigurationArguments.cs`.
- `Tools/WorkspaceTools.cs`: маршруты load/reset и обработка ошибок/cancellation; `Tools/ServerLifecycleTools.cs`: stop; `Tools/ToolHelpTools.cs` и участок `Tools/UtilityTools.cs` для workspace-независимых вызовов.
- `RoslynMcpServer.Tests/Workspace/MsBuildWorkspacePropertiesTests.cs`, `SolutionManagerPassedLoadArgsTests.cs`, `RoslynMcpServer.Tests/Logging/HarnessSessionIdTests.cs`: проверены тексты существующих тестов.
- В отдельном checkout `E:\Devel\RoslynMcpServer-workspace-host-process` прочитано начало `docs/workspace-host-process/README.md` (до раздела границ): мотивация измерений, scope и наблюдения. Это контекст исследовательской ветки, не нормативное расширение текущего descriptor.

## Context not inspected

- Другие review-каталоги, findings других reviewers, defence/response, arbitration и последующие decision ledgers не читались. Упоминания таких материалов в индексах не использовались как evidence.
- Полные пакеты workspace-state-cache, backlog cache и исторических экспериментов не исследовались: они не являются реализацией текущего потока статистики.
- Не выполнялся аудит всего сервера, всех инструментов, плагинов и upstream fork.
- Не читались пользовательские raw logs или данные проектов коллег.
- Не проверялись текущие настройки, версии и UI сторонних harness.

## Source/code verification

Проверка статическая, по исходникам и текстам тестов:

- подтверждены in-process cache key и наследование omitted properties;
- подтверждены canonical Platform и OS-зависимое сравнение путей;
- подтверждены reset/dispose, reopening и отсутствие автоматического semantic load у help/file-range вызовов;
- подтверждено, что существующая ToolTelemetry пишет итог, а ошибки/cancellation инструментов могут возвращаться как текст;
- проверено, что тестовый contract допускает конкурентные load и ожидание workspace lock;
- проверены существующий stdio lifecycle и локальный характер логирования.

Контрпримеры метрик построены из предложенных определений; это не результаты реального сбора и не benchmark. Новые тесты и код не создавались; существующие тесты, build, publish и harness smoke не запускались.

## Coverage limitations

- Код статистики и анализатора ещё не начат по статусу target. Поэтому проверялись достаточность требований и существующие integration seams, а не готовая реализация writer/aggregator.
- Ссылка target на `docs/workspace-host-process/evidence/2026-10-29-freeze.md` проверена: файла нет в доступном checkout исследования; поиск истории этого точного пути в локальном ref также не дал commit. Внешние/неполученные изменения ветки не загружались. Заморозка принята только как заявленный контекст target.
- Межпроцессная атомарность JSONL/создания HMAC-секрета, реальные I/O overhead, platform memory counters и shutdown flush экспериментально не проверялись. Требование «одна соль на пользователя» не объявлено дефектом только из-за отсутствия выбранного алгоритма синхронизации.
- Оценка savings общего host не подменялась измерением достижимой экономии; стоимость trim и будущего host неизвестна.
- O-01…O-04 и пороги не решены за владельца. Для неявного fail-open требования F-007 явно указаны confidence medium и условие, способное изменить вывод.

## Scope and consistency check

Каждый из семи findings содержит target, claim, evidence, failure scenario, constraint, минимальное направление исправления и notes for arbitration. ID локальны и стабильны. F-001 — наблюдаемость активности, F-002 — идентичность, F-003 — цена возврата, F-004 — единицы, F-005 — достаточность критерия, F-006 — полнота данных, F-007 — доступность сервера: root causes различаются.

Ревью не переписывает спецификацию, не создаёт defence/арбитраж и не разрешает реализацию или сбор. Результаты записаны только в собственный review directory. Статус выполнения фиксируется отдельно в `.status` после проверки согласованности.

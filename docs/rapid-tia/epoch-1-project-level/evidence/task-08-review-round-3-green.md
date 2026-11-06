# Task-08 R2-001 — focused GREEN для раунда 3

- Дата: **2026-11-06**, Windows, Release; база `1f11b47`.
- Исполнитель: Codex, `/root/task08_implementer`; точный runtime model ID средой не предоставлен.
- Версия при прогонах: **1.6.0**; patch bump — только после сборки/разрешения координатора.
- [Основание](task-08-review-round-2-claude.md), [RED координатора](task-08-review-round-3-red.md).

Все вызовы — специализированный Roslyn MCP `run_specific_test`, проект `E:/Devel/RoslynMcpServer-rapid-tia/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`, `configuration=Release`, `noRestore=true`, `includeFullOutput=true`.

1. `className=EstimateAffectedTestsReloadTests`, `noBuild=false`, `timeoutSeconds=240`: **7 total / 7 passed / 0 failed**.
2. `className=EstimateAffectedTestsTests`, `methodName=Observation_gap_expands_projects_even_when_known_CSharp_is_unchanged_and_survives_reload`, `noBuild=true`, `timeoutSeconds=120`: **1 total / 1 passed / 0 failed**.
3. `className=Baseline`, `noBuild=true`, `timeoutSeconds=300`: **349 total / 349 passed / 0 failed**.
4. `className=SourceStructure`, `noBuild=true`, `timeoutSeconds=240`: **123 total / 123 passed / 0 failed**.
5. `className=EstimateAffectedTests`, `noBuild=true`, `timeoutSeconds=240`: **50 total / 50 passed / 0 failed**.

Ни один прогон не был zero-test pass. Итоги specialized runner не содержали skips/failures/timeouts. Весь inventory SourceStructure identities/kinds/paths сохранён. Прежние Baseline assertions не менялись; старый true-gap test только усилен доказательством не кешированного reload.

Обычный native reload после нового namespaced source возвращает фильтры только `Specs`, после C# rollback и удаления Fresh с новым reload — пустой план. Сохраняющаяся реальная `.csproj` правка выбирает `Specs` целиком, сохраняя историю исходного baseline; независимый `Isolated` исключён. Не-C# uncertainty остаётся в `Not covered`. Настоящий watcher/capture gap по-прежнему даёт оба проекта целиком после reload.

После исходного RED fixture для новых tests изолирована от известного T04-C3 generated-membership шума: `suppressGeneratedFiles=true` до первого MSBuild load, default false для прежних callers. Условия assertions не ослаблены; два сообщения падения стали полным текстом ответа. Причины и промежуточные результаты указаны в [implementation report](../task-08-implementation-report.md#реальные-reload-регрессии-и-корректировка-fixture).

Это focused validation исполнителя. Final Release build, main CI, opted-in AnalyzerLifecycle, новая версия и isolated runtime dogfood, независимый последний раунд 3 и статусы приёмки принадлежат координатору.

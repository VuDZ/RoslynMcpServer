# P1-36. Восстановить запись исходников без BOM в классических проектах .NET Framework

- Рекомендуемые модели: GPT-6.2 Astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 4/5.
- Риск ошибки: высокий — редактирование обычного загруженного проекта блокируется.
- Необходимые способности: MSBuildWorkspace, BuildHost-net472, кодировки.
- Статус: accepted, 2026-11-25. Запись без BOM в классических проектах .NET Framework сохраняет кодировку и опубликованный текст.
- Depends on: —.
- Модель, реализовавшая задачу: Codex (GPT-6), `impl36`.
- Модели, проводившие ревью: Codex (GPT-6), `review36`.
- Количество раундов ревью: 1.
- Количество исправлений после ревью: 0.
- Отчёт о реализации, ревью и validation: [приёмка](evidence/tasks-36-43-validation.md#p1-36).

## Воспроизведение

Проверена рабочая копия с исправлениями 28–35 поверх `380decf`, Release, Windows, SDK `11.0.100`.

Создать классический `.csproj` с `ToolsVersion="Current"`, `TargetFrameworkVersion=v4.7.2`, явным `Compile` и импортом `Microsoft.CSharp.targets`. Добавить ASCII-исходник без BOM с выражением `X => 1`. `dotnet msbuild -t:Build` вернул 0 и создал DLL. Roslyn загрузил проект и вернул ноль диагностик.

`apply_patch` с заменой `1` на `2` и `update_file_content` с полной версией `X => 3` завершились ошибкой `Cannot determine the project's source encoding`. Сценарий повторился с настройкой по умолчанию и с `CodePage=28591`. На том же исходнике с UTF-8 BOM запись прошла.

## Исправление

`GetBomFreeFileEncodingUnderLockAsync` требует `metadata.CodePage`. Комплектный `BuildHost-net472` поставляется из пакета Roslyn и не выдаёт дополнительные данные форка `BuildHost-netcore`. До этого изменения запись ASCII не зависела от этих данных. Получать действующую кодировку также для классического проекта и сохранять отказ при действительно неоднозначных данных.

## Критерии приёмки

- [x] Проверить оба публичных инструмента на настоящем классическом проекте. Для ASCII без BOM и для заданной Latin-1 подтвердить результат по байтам файла и опубликованному документу.
- [x] Контроли с невалидными байтами и конфликтующими страницами связанных проектов продолжают отклонять запись.

## Где смотреть

- [SolutionManager.cs](../../Services/Workspace/SolutionManager.cs)
- [ProjectLoadMetadataCapture.cs](../../Services/Workspace/ProjectLoadMetadataCapture.cs)
- [BuildHostFork.targets](../../forks/roslyn-buildhost/build/BuildHostFork.targets)
- [WorkspaceWriteEncodingTests.cs](../../RoslynMcpServer.Tests/Workspace/WorkspaceWriteEncodingTests.cs)
- Исходные карточки — [P1-28](task-28-p1-full-write-stale-snapshot-hi.md), [P1-29](task-29-p1-patch-fallback-code-page-hi.md).

[Реестр и общие требования к проверке](README.md)

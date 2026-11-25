# P1-49. Проверять кодировку исходника в обычной сборке

- Рекомендуемые модели: GPT-6.2 Astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 4/5.
- Риск ошибки: высокий. После успешной правки программа получает другое значение строкового литерала.
- Необходимые способности: MSBuild, design-time сборка Roslyn, кодировки C# и проверка результата компилятором.
- Статус `accepted` на 2026-11-26. Сверены кодировки и состав читателей при обычной сборке и в workspace, включая импорты MSBuild в режиме read-only. Проверка не выполняет цели сборки, неопределённый результат приводит к отказу до записи. [Приёмка](evidence/tasks-49-54-validation.md#p1-49) · [Повторная проверка Windows](evidence/tasks-49-54-validation.md#windows-ci-и-импорты-msbuild-в-режиме-read-only).
- Depends on: —.
- Отчёт о воспроизведении: [повторное ревью](evidence/review-f82d4ff.md#p1-49).

## Воспроизведение

1. Создать исполняемый SDK-проект `net11.0` с таким фрагментом свойств.

   `<CodePage>28591</CodePage>`

   `<CodePage Condition="'$(DesignTimeBuild)' == 'true'">65001</CodePage>`

2. Создать `Program.cs` без BOM. Исходный литерал `edit` содержит ASCII, а программа печатает кодовые единицы его символов в шестнадцатеричном виде.

   ~~~csharp
   var edit = "X";
   System.Console.WriteLine(string.Join(
       " ",
       System.Linq.Enumerable.Select(edit, character => ((int)character).ToString("X4"))));
   ~~~

3. Вызвать `load_workspace` для проекта. Через `update_file_content` записать полный текст с `var edit = "Café";`. В отдельной копии исходного проекта выполнить ту же замену через `apply_patch`. Оба вызова сообщают об успехе и оставляют литерал с байтами `43 61 66 C3 A9` без BOM.
4. Запустить `dotnet run -c Release`. Программа в обоих случаях печатает `0043 0061 0066 00C3 00A9`. При удалении условного свойства и загрузке контрольной копии проекта полная запись сохраняет `43 61 66 E9` и программа печатает `0043 0061 0066 00E9`.

### Регрессия Windows CI

Собрать исходный коммит `7d942b9` в Release. В новом процессе PowerShell включить для MSBuild загрузку в режиме read-only и выполнить тесты записи исходников без BOM.

```powershell
$env:MSBUILDLOADALLFILESASREADONLY = '1'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Bom_free_source_edits_check_ordinary_readers_without_running_build_targets|FullyQualifiedName~ApplyPatch_does_not_add_bom_to_workspace_sources'
```

Старая проверка отклоняет обе операции записи в контрольном проекте с одинаковой кодировкой и патч файла без BOM. Исправленная проверка читает XML загруженного проекта и импортов в режиме read-only. Обращение к свойству, которое определяется запуском обычной сборки, по-прежнему препятствует подтверждению её читателей. [Диагностика и повторная проверка](evidence/tasks-49-54-validation.md#windows-ci-и-импорты-msbuild-в-режиме-read-only).

## Причина и требуемое поведение

`SourceCodePagesMatchAsync` сверяет загруженные и свежие метаданные одного и того же design-time BuildHost. Эта проверка подтверждает стабильность чтения в Roslyn. Обычный запуск компилятора использует другую ветвь условия и читает те же байты по другой кодировке. При выборе представления для патча учитывать и смысл исходного текста вне замены. Успешная операция должна сохранять текст для Roslyn и обычной сборки. При отсутствии такого представления допустим явный отказ до изменения файла и опубликованного текста.

## Критерии приёмки

- [x] Проверить патч и полную запись при разных design-time и build `CodePage`. При успехе свежая сборка получает запрошенный литерал. При отказе исходные байты и опубликованный текст сохраняются.
- [x] Сохранить поведение контрольного проекта с одинаковыми настройками и прогоны P1-44 и P1-47.

## Где смотреть

- [WorkspaceProjectDocumentRegistration.cs](../../Services/Workspace/WorkspaceProjectDocumentRegistration.cs).
- [SolutionManager.cs](../../Services/Workspace/SolutionManager.cs).
- Связанные карточки — [P1-44](task-44-p1-source-write-reader-encoding-hi.md), [P1-47](task-47-p1-stale-project-code-page-hi.md).

[Реестр задач](README.md)

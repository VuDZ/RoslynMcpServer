# P1-51. Сохранять действие ProjectReference Update при переименовании

- Рекомендуемые модели: GPT-6.2 Astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 3/5.
- Риск ошибки: высокий. Успешное переименование меняет семантику ссылки проекта и ломает ранее успешную сборку.
- Необходимые способности: упорядоченные MSBuild items, переписывание проектных путей, проверка сборки до и после.
- Статус: accepted, 2026-11-25. План сохраняет действие литеральных операций ProjectReference и отклоняет неоднозначные изменения до перемещения. [Приёмка](evidence/tasks-49-54-validation.md#p1-51).
- Depends on: —.
- Отчёт о воспроизведении: [повторное ревью](evidence/review-f82d4ff.md#p1-51).

## Воспроизведение

1. Создать соседние SDK-проекты `Old/Old.csproj` и `App/App.csproj` для `net11.0`, добавить оба в `Demo.slnx`. В обоих проектах объявить публичный тип `Demo.Marker`. В `App` добавить обращение к `Marker` и включить `TreatWarningsAsErrors`.
2. В `App.csproj` добавить следующий блок. До переименования `dotnet msbuild App/App.csproj -getItem:ProjectReference` показывает `ReferenceOutputAssembly=false`, а `dotnet build App/App.csproj -c Release` проходит.

   ~~~xml
   <ItemGroup>
     <ProjectReference Include="../Old/Old.csproj" />
     <ProjectReference Update="../Old/Old.csproj" ReferenceOutputAssembly="false" />
   </ItemGroup>
   ~~~

3. Вызвать `rename_project` для `Old.csproj` с `newProjectName=New` и `dryRun=false`. MCP сообщает `Rename applied successfully.`. В `App.csproj` путь `Include` стал `../New/New.csproj`, путь `Update` остался `../Old/Old.csproj`.
4. `-getItem:ProjectReference` больше не показывает `ReferenceOutputAssembly=false`. Сборка `App` падает с `CS0436`, поскольку компилятор теперь получает конфликтующий тип из `New`.

## Причина и требуемое поведение

`RewriteXmlReferences` для проектов выбирает только атрибут `Include`. `Update` относится к тому же item и меняет его метаданные по совпадению пути. Для поддерживаемого плана сохранить действие операций с item после переноса. Если семантику плана подтвердить нельзя, отклонить его при предварительном просмотре до перемещения.

## Критерии приёмки

- [x] Переименование сохраняет эффективное `ReferenceOutputAssembly=false` и успешную сборку. При неподдерживаемом плане preview и применение отклоняют его до записи.
- [x] Проверить литеральный `Remove` для того же элемента и сохранить существующие проверки обычных ссылок и форматов решений.

## Где смотреть

- [ProjectRenameHelper.cs](../../Services/Editing/ProjectRenameHelper.cs).
- [ProjectRenameHelperTests.cs](../../RoslynMcpServer.Tests/Editing/ProjectRenameHelperTests.cs).
- Связанная карточка — [P1-03](task-03-p1-rename-project-paths-hi.md).

[Реестр задач](README.md)

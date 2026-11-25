# P1-23. Обновлять `BuildDependency` в `.slnx` при вызове `rename_project`

- Рекомендуемые модели: GPT-6.2 Astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 4/5.
- Риск ошибки: высокий — после переименования SDK перестаёт открывать решение.
- Необходимые способности: формат .slnx, разрешение путей, планирование переименований.
- Статус: planned, 2026-11-25.
- Depends on: —.
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Воспроизведение на `70aafb7`

1. Создать `Old/Old.csproj`, `Consumer/Consumer.csproj` и `Demo.slnx` с такой зависимостью. Убедиться, что `dotnet sln Demo.slnx list` выводит оба проекта.

   ```xml
   <Solution>
     <Project Path="Old/Old.csproj" />
     <Project Path="Consumer/Consumer.csproj">
       <BuildDependency Project="Old/Old.csproj" />
     </Project>
   </Solution>
   ```

2. Вызвать `rename_project` для `Old/Old.csproj` с `newProjectName="New"` и `dryRun=false`.
3. На проверенной сборке сервер сообщил об успехе. В `Project/@Path` появился `New/New.csproj`, а в `BuildDependency/@Project` остался старый путь. Повторный `dotnet sln Demo.slnx list` завершился с кодом 1 и сообщением `Project 'Old\Old.csproj' not found`.

## Исправление

Включить пути из `BuildDependency/@Project` в план преобразования `.slnx` вместе с `Project/@Path`. Построить и проверить план до перемещения каталога.

## Критерии приёмки

- [ ] Тест выполняет реальное переименование на указанном решении. После выполнения оба атрибута указывают на `New/New.csproj`, а потребитель по-прежнему зависит от переименованного проекта.
- [ ] `dotnet sln Demo.slnx list` завершается с кодом 0 до и после применения и перечисляет оба существующих проекта. Предварительный просмотр (`dryRun=true`) оставляет решение и каталоги без изменений.

## Где смотреть

- [План переименования](../../Services/Editing/ProjectRenameHelper.cs) — выбор XML-атрибутов для `.slnx`.
- [Тесты переименования проектов](../../RoslynMcpServer.Tests/Editing/ProjectRenameHelperTests.cs).
- Связанные задачи исходного ревью — [P1-03](task-03-p1-rename-project-paths-hi.md).

[Реестр задач и общая проверка](README.md)

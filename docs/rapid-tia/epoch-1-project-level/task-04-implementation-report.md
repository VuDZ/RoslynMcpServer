# task-04 — отчёт о реализации

- Дата реализации: **2026-11-05** (Europe/Moscow).
- Статус: **accepted, 2026-11-05**; [независимое ревью — ACCEPT](evidence/task-04-review-round-1.md), [финальная validation](evidence/task-04-final-validation.md).
- Scope: внутренний сервис уровня 0, структурированный результат, повторное использование существующего распознавания тестов и focused проверки. Публичного инструмента и task-05+ в этой задаче нет.
- Исполнитель: Codex; точный runtime model ID средой не предоставлен, **unknown**.
- Координатор выполнил финальные build/test validation и обновил статусы задачи и индексов после независимого ревью; принятый scope — только task-04, без приёмки всей эпохи.

## Реализация

`ProjectImpactMapper.MapAsync` получает загруженный `Solution`, его `WorkspaceInputMap`, изменённые пути, путь загруженного workspace и существующий `SolutionManager.ProjectGraphStale`. Сервис чистый относительно workspace: не запускает watchers и не меняет жизненный цикл загрузки. После reload вызывающий код передаёт обновлённые solution/map и актуальный stale-флаг.

- C#-документ относится ко всем проектам-владельцам, включая linked-документы. Карта входов повторно используется для остальных известных путей.
- Неизвестный `.cs` относится к проектам в ближайшем содержащем каталоге; при нескольких загруженных экземплярах в одном каталоге сохраняются все. Результат требует reload. Путь без владельца остаётся в `NotCoveredPaths` с полным именем файла.
- `ExpandProjects` — одно определение проектного расширения: тесты самого проекта и всех его транзитивных потребителей через `ProjectDependencyGraph.GetProjectsThatTransitivelyDependOnThisProject`.
- При stale-графе выбираются все распознанные тестовые проекты загруженного solution, выставляются `ProjectGraphStale` и `RequiresReload`. Для graph-файла используется существующий `WorkspaceDiskPathFilter.IsProjectGraphFile`; после reload действует карта обновлённого графа.
- Карта имеет приоритет над фильтром вывода, в том числе для явного входа внутри `obj`. Неизвестный вывод отбрасывается через существующий `WorkspaceDiskPathFilter.IsIgnoredPath(path, workspaceDirectory)`: общий префикс с workspace не скрывает репозиторий внутри `artifacts`, а solution в `build/` не освобождает соседние `bin`/`obj` от фильтра.
- Restore-входы `project.assets.json`, `packages.lock.json`, `nuget.config` исключены из отображения; это объявленная слепая зона уровня 0.
- Тестовые проекты определяются тем же `TestDiscoveryHelper.HasTestAttribute`, что обнаружение тестов: semantic matching, custom/derived attributes и aliases, синтаксический fallback для unbound compilation. Доступность существующего метода изменена с private на internal; его поведение не менялось.

## TDD и проверки

До production-поведения написаны `ProjectImpactMapperTests`, `ProjectImpactMapperIntegrationTests` и `ProjectImpactFixture`; для компиляции добавлены DTO и сигнатура mapper с `NotImplementedException`.

1. [RED](evidence/task-04-red.md): Release, 37 total/executed/failed, 0 passed. Координатор дополнительно проверил TRX: во всех 37 ошибках присутствует `NotImplementedException`; ошибок компиляции и фикстур нет. Реальные сборки интеграционных фикстур состоялись до вызовов пустого mapper.
2. Минимальная реализация сервиса и открытие существующего matcher внутри сборки.
3. [GREEN](evidence/task-04-green.md): Release, 37 total/passed, 0 failed через Roslyn MCP. Тесты после RED не добавлялись и не менялись.

Проверены точные множества владельцев, тестовых проектов и uncovered paths: mapped/nonmapped не-C# пути, транзитивная цепочка Library → App → Specs, linked C#, сам тестовый проект, ближайший проект для нового `.cs`, все graph-расширения и имена, сохранённый stale-флаг, walk-up props над несколькими проектами, restore blind spot, все имена игнорируемых каталогов, явный вход под `obj`, репозиторий под `artifacts`, solution в `build/`, существующие правила test attributes и cancellation.

Интеграция выполняет настоящий `dotnet build -c Release` через существующий `DotNetCliRunner` в тестовой фикстуре, затем загружает solution и проверяет реальные файлы `bin` и `obj` вне карты и явный additional input под `obj`. Второй маршрут держит solution в `build/`. Ещё одна проверка добавляет `ProjectReference` в csproj, доставляет событие существующему watcher callback, проверяет все тестовые проекты до reload и обновлённое транзитивное ребро после настоящего reload.

## Ограничения и что не менялось

- Поиск тестовых проектов читает C# syntax/semantic models загруженного solution при каждом вызове mapper. Кэша и performance benchmark в task-04 нет; цена учитывается как ограничение по R-18.
- Новый `.cs` привязывается по ближайшему каталогу, поскольку public workspace до reload не даёт оценённых правил включения нового файла. Symlink aliases не объединяются: сохраняется существующая канонизация путей.
- Restore-входы и неполнота существующей карты входов остаются слепыми зонами; неизвестный обычный путь не объявляется безопасным.
- При stale-графе результат консервативный и включает все тестовые проекты. Сервис сам stale-флаг не снимает; это состояние существующего lifecycle.
- Версия не повышалась, lifecycle/load/publication не менялись. Публичные инструменты, формат ответа и запуск выбранных тестов отложены до их задач.
- Координатор завершил Release solution build, SourceStructure 123/123, focused 54/54 и main suite 1837/1837. Task-04 принята; эпоха целиком остаётся в работе.

## Приёмка

- Дата: **2026-11-05** (Europe/Moscow).
- Принятый scope: внутренний mapper уровня 0 «путь → проекты → тестовые проекты», транзитивное расширение, stale/reload и фильтрация вывода сборки. Публичная активация и task-05+ не входят.
- Исполнитель: Codex, сессия `/root/task04_implementer`; рецензент: Codex, независимая сессия `/root/task04_review_round1`. Точные runtime model ID средой не предоставлены.
- Ревью: **1 раунд, 0 исправлений**, [ACCEPT](evidence/task-04-review-round-1.md); MUST-FIX нет. Ограничения T04-R1-L01–L05 сохранены без нового раунда по R-18.
- [Финальная validation координатора](evidence/task-04-final-validation.md): Windows Release build PASS; focused **54/54**, SourceStructure **123/123**, main CI suite **1837/1837**. Изменений кода и тестов после этих прогонов не было.
- Версия сохранена; Linux/WSL, performance benchmark и AnalyzerLifecycle не запускались, основания и границы указаны в validation.

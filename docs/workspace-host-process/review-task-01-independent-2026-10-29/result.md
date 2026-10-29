# Ревью task-01 — архитектурное решение

- Дата: 2026-10-29.
- Объект: [task-01](../epoch-0-design-and-spike/task-01-architecture-decision-hi.md), [design](../design.md), [отчёт](../epoch-0-design-and-spike/evidence/task-01-architecture-decision.md).
- База отчёта: `b2a7b80`; статусная правка описана поверх `d8c15f6`. Текущий `HEAD`: `d54a93a` (`docs: defer architecture approval pending separate reviews`). Между ними нет коммитов вне этой статусной правки. `RoslynMcpServer.csproj` — **1.5.18**.
- Рецензент: Grok 4.7; идентификатор сессии недоступен.
- Этот каталог — отдельный проход по текущему тексту. ID `F-01`…`F-03` не продолжают одноимённые замечания ревью Codex от 2026-10-29. Каталог `review-task-01-2026-10-29` при формировании замечаний не читался.
- Итог: **требуются исправления**.

## Главное

- Рекомендация выделенного host против нулевого варианта на данных task-05 сохраняется. Оценка 1878,2 MiB пересчитана из формулы task-05 и подписана как оценка. Каталог 54/54 и строки S01–S42 на месте. Выбор владельца O-01…O-04 по-прежнему отложен; task-02 начинать рано.
- **Блокер для task-02:** `list_projects` и `get_project_graph` при пути, не совпадающем с текущим workspace, вызывают `LoadAsync`. Тот сначала освобождает общий graph. Таблица design §5 относит оба инструмента к чтению опубликованного graph на host. Прямая пересылка метода подменит workspace всех клиентов этого host.
- **Крупный пробел:** `add_package_reference`, `remove_package_reference` и применённый `rename_project` вызывают `ClearWorkspaceAsync`. Он обнуляет общий manager и не трогает `SessionBuildState`. Design говорит «graph invalidation» и не задаёт эпоху, уведомление остальных сессий и запрет старых proof.
- Независимый поиск состояния процесса (ALC, AppDomain, таймеры, `Task.Run`, статические словари) не дал нового механизма вне S01–S42. Нового production-дефекта, который надо заводить отдельно от этой правки design, нет.
- Оба замечания относятся к обеим shared-моделям. В нулевом варианте процесса-host нет, и эти вызовы остаются нынешним поведением одного клиента.

## Замечания

| ID | Severity | Где | Что не так | Что сделать |
|---|---|---|---|---|
| F-01 | blocker | [design.md](../design.md) §3, §4, §5; [UtilityTools.cs](../../../Tools/UtilityTools.cs) строки 1420 и 1485; [SolutionManager.cs](../../../Services/Workspace/SolutionManager.cs) строки 242–257 и 2076–2094; [task-00](../epoch-0-design-and-spike/evidence/task-00-state-inventory.md), строка `list_projects` / `get_project_graph` | При непустом `workspacePath` оба инструмента вызывают `LoadAsync`. Если путь не совпадает с уже загруженным, `LoadCoreAsync` останавливает prewarm, делает `Dispose` workspace и обнуляет `_solution` до `MSBuildWorkspace.Create`. Заодно сбрасывается sticky overlay (`ShadowCopyAnalyzersEnabled = false`). Design §3 запрещает host менять свой workspace path по runtime-команде. Design §4 описывает переключение сессии так, что загрузка B не подменяет graph A для остальных. Таблица §5 этого развилка не содержит: инструмент назначен на host как чтение опубликованного graph и disk sync. Task-00 прямо требует не заменять graph проектом другого клиента. Следующая задача, реализуя строку таблицы, получит ложный контракт. | В §5 разделить совпадающий путь и чужой. Совпадающий путь читает опубликованный graph текущего ключа. Чужой путь идёт тем же attach/switch, что `load_workspace` в §4: подготовка другого ключа вне gate привязки, короткий commit только будущих вызовов этой сессии, отказ сохраняет прежний ключ. На host текущего ключа `LoadAsync` чужого пути не вызывать. Добавить эту последовательность в §11. |
| F-02 | major | [design.md](../design.md) §4, §5, §6; [ProjectTools.cs](../../../Tools/ProjectTools.cs) строки 58, 86, 114, 128; [SolutionManager.ClearWorkspaceAsync](../../../Services/Workspace/SolutionManager.cs) строки 768–806; поле `SessionBuildState` на строке 66 | После записи package/rename код очищает весь manager: workspace, loaded path, Loaded*, analyzer mapping, `LoadSessionId = Guid.Empty`. `SessionBuildState` при этом тот же объект на время жизни manager. Строка §5 говорит только «graph invalidation». Протокол §7 (уведомить все proxy, drain, новая generation) привязан к `reset_workspace`. Для этих трёх инструментов не сказано, что очистка — смена `WorkspaceEpoch` общего ключа, что остальные сессии теряют tickets/proofs и что следующий load того же ключа — одна shared attempt, а не тихий `LoadAsync` первого клиента. | Описать общий эффект: эпоха ключа меняется, чужие tickets и DLL-proof этого manager не принимаются, все привязанные сессии получают invalidation, повторный load идёт single-flight §7. Текст ответа вызывающему клиенту («вызовите load_workspace») сохранить. В §11 добавить одну последовательность: package/rename при двух клиентах и живом proof. |
| F-03 | minor | [design.md](../design.md) §5, строка `search_code` / `get_changed_files`; [UtilityTools.GetChangedFiles](../../../Tools/UtilityTools.cs) строки 161–163; [GitChangedFilesHelper.SuggestTestProjects](../../../Services/GitChangedFilesHelper.cs) строка 93 | Разрез «host разрешает опущенный якорь, proxy исполняет git» покрывает `ResolveGitAnchorPath`. Блок «Suggested test projects» строится обходом `solution.Projects` и документов. При исполнении git только в proxy этот блок пропадёт, хотя §5 требует сохранить нынешнее форматирование успешных результатов. | В той же строке указать, кто считает heuristic: host по захваченному snapshot и уже полученному списку путей, proxy только печатает готовый список. Пустой список при отсутствии graph оставить как сейчас. |

## Проверенные утверждения

| Утверждение | Вердикт | Где смотрел |
|---|---|---|
| Свежий load освобождает прежний graph до открытия нового | подтверждено | `SolutionManager.cs:2076–2119`: `StopSearchPrewarm`, `Dispose`, `_solution = null`, затем `MSBuildWorkspace.Create`. Cache hit того же path (строки 2043–2073) graph не освобождает. Ссылка отчёта на строку 893 указывает на начало тела под lock; сами omitted defaults — строки 906–915. Смысл верен. |
| DLL-маршрут тестов зависит от graph, `BuildContext` и `PullSnapshot` | подтверждено | `TestTools.cs:762` (`GetPublishedSolutionAfterDiskSyncAsync`), `:821` (`SessionBuildState`), `:828` (`PullInputSnapshot`). Метод начинается на строке 741, как в отчёте. |
| Успешный build не является proof содержимого inputs | подтверждено | `SessionBuildState.cs:11–12`. Ссылка на строку 8 — начало комментария типа. |
| Core DI смешивает activation и `SolutionManager` | подтверждено | `RoslynMcpServiceCollectionExtensions.cs:69–75`. |
| Старт регистрирует MSBuild, force-load Roslyn, cwd, jsonc и plugins до `host.RunAsync` | подтверждено | `Program.cs:15`, `:34`, `:58`, `:96`, `:120`. Регистрация stdio-транспортa в DI — строка 81, раньше `PluginStartup.Run`. Слушающий stdio начинается на `RunAsync`. |
| Plugin API вносит singletons в тот же контейнер | подтверждено | `IRoslynMcpPlugin.cs:4–22`. |
| Publish останавливает держателей исходных DLL | подтверждено | `publish-and-verify.ps1:86–99` и вызов на строке 208. Ссылка на строку 48 — начало `Get-RoslynMcpLockHolders`, сам `Stop-Process` ниже. `-SkipKill` обходит остановку (строка 211). |
| 54 public built-in имени каталога и таблицы design | подтверждено | `McpToolCatalog.Build`, строки 165–226: 54 имени, дубликатов нет. В таблице §5 те же 54. |
| S01–S42 все названы в §10 | подтверждено | Перечислены группами; пропусков номеров 1…42 нет. |
| Оценка экономии выделенного host 1878,2 MiB | подтверждено как арифметика оценки | task-05: `L=2514,4`, `H=267,6`, `U=92,0`, `p=23,04`. `H+16p+U = 728,24`; `L+U = 2606,4`; разница `1878,16`, в тексте округлена до 1878,2. Design помечает её как оценку, не как замер реализации. Контроль 2606,4 MiB — замер 20 процессов 1.5.18, не состав прежних 10–20 ГБ. |
| Задержки task-03 0,39–0,45 мс | не перепроверялись по сырому ряду | Число только процитировано. На выбор модели оно не опирается: в design это цена fake-вызова. |
| macOS `flock` и ACL | не проверялись | Design сам помечает macOS как непроверенную и отключает режим до task-02. Это совпадает с требованием явного платформенного предела. |
| Имена `CODEX_THREAD_ID` / `SESSION_ID` / `TOOL_CALL_ID` отсутствуют у child CLI подключённого MCP | не перезапускались | Оставлены как наблюдение отчёта 2026-10-29. V-06 по-прежнему открыт; design это говорит. |

Конфликты task-00, которые design закрывает явно и которые здесь не опровергнуты: descriptor и omitted defaults (§2), session/host split и cursors (§4), switch вне gate (§4), раздельные бюджеты и cooldown только после отказа (§7), один lock descriptor на процесс/ключ (§3), retry после наблюдаемого отказа (§7), manifest image (§9), CLI не обрывается смертью host и не становится proof нового поколения (§6). Ролевые handoff/promotion помечены неприменимыми для выбранной модели с причиной.

## Bookkeeping

Шапка task-01, README эпохи, README темы и строка темы в `docs/README.md` сходятся: task-01 **in progress, 2026-10-29, docs-only**; техническое ревью Codex — 2 раунда и 3 исправления; O-01…O-04 отложены; task-02 planned. Это не приёмка владельцем и не приёмка эпохи. Счётчики этого прохода в них ещё не входят: их обновляет координатор после решения по замечаниям. `git diff --check` по design, отчёту и task-файлу — без замечаний. Рабочее дерево на момент ревью чистое.

## Что делать дальше

Вписать в design решения F-01 и F-02 и одну проверяемую последовательность на каждый. F-03 поправить в той же строке таблицы. После этого имеет смысл повторная проверка только этих трёх мест. Выбор O-01…O-04 и старт task-02 остаются за владельцем и не следуют из закрытия этих замечаний автоматически.

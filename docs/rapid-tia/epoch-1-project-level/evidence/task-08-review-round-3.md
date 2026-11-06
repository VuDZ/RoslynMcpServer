# task-08 — независимое ревью, раунд 3

- Дата: **2026-11-06** (Europe/Moscow).
- Рецензент: **Codex**, независимый агент `/root/task08_review_round3`; точный runtime model ID средой не предоставлен.
- База: `1f11b47`, ветка `rapid-tia`, замороженные production C#/project files версии **1.6.1**. Проверен diff трёх production-файлов, новые `EstimateAffectedTestsReloadTests`, изменения существующих теста и fixture, версия/changelog и связанные вызывающие пути. Посторонний `.agents/skills/prepare-github-release/` исключён.
- Норма: полностью прочитаны `AGENTS.md`, `docs/code-style.md`, [task-08](../task-08-estimate-tool-hi.md), [второй рецензент раунда 2 и решение владельца R2-001](task-08-review-round-2-claude.md); применён R-18. Это третий, последний разрешённый раунд. Style/nits, стоимость и посторонние ограничения не стали причиной возврата.
- Вердикт: **MUST-FIX — R3-001, подтверждённая потеря ранее выбранного тестового проекта после reload**. Исходное R2-001 закрыто, но новый способ отбрасывать откатанные пути удаляет также реальное изменение исходного non-source input. Координатор уведомлён сразу после воспроизведения. Продолжение после этого раунда определяет владелец.

## R2-001 — CLOSED

`BaselineSessionSnapshot.HasObservationGap` отделяет настоящий разрыв от штатного reload. Прежний `RequiresFullScan` и объединение событийных путей с исходными/текущими C# rescan candidates сохранены. `CloseGenerationUnderLock` по-прежнему поднимает rescan-флаг, но не новый gap-флаг. `NoteObservationGap`, первоначальный startup gap и исключение всего capture-worker поднимают оба флага; обычный reload того же workspace новый gap не сбрасывает. Инициализация другого baseline задаёт его из собственного pending gap.

`TestImpactEstimator` расширяет все тестовые проекты по `HasObservationGap`; штатный `RequiresFullScan` даёт постоянную конкретную оговорку о non-C# reload window. Обычные изменения C# продолжают сравниваться с исходным Known snapshot. Для отсутствовавшего в исходном C# inventory физического пути текущий загруженный документ сравнивается с пустым снимком; подавлен только hash локальных imports/namespaces. Global using, assembly/module attributes, broken syntax, operator/conversion и extension-method расширения проходят через прежний detector/composer. Для нового контекста уже существовавшего физического пути остаётся unknown, а исходные Known/Unknown состояния не заменяются пустым снимком.

Семь новых cases используют настоящий MSBuild/native watcher и проверяют не кешированный reload: `LastLoadReopenedGraph=true`, `LastLoadWasCacheHit=false`, рост baseline generation. Они покрывают новый namespaced `.cs` и возврат фильтров без `Isolated`, откат/удаление и пустой план, сохраняющуюся `.csproj` дельту только в своей closure, C# запись до старта watcher с устаревшим mtime и нулевой event revision, запись после reopen, sticky `NoteObservationGap` и capture failure. Существующий observation-gap тест усилен реальным Release reload. Прежние Baseline assertions не изменены. `suppressGeneratedFiles=true` применяется только к новым cases; default прежних callers сохранён. Это изолирует описанный T04-C3 generated-membership шум и не отключает watcher/rescan assertions.

Рецензент прочитал публичные ответы отдельной живой проверки координатора: после нового файла и настоящего reload — два фильтрованных теста одного проекта; после восстановления — пустой план; следующая правка снова даёт фильтр. Это подтверждает закрытие исходного сценария владельца. Не-C# оговорка после reload остаётся видимой.

## R3-001 — удаление исходного AdditionalFile с расширением `.cs` забывается после reload

**Серьёзность: MUST-FIX, потеря тестового проекта в реальном сценарии.**

Место: [`TestImpactEstimator.DocumentsAsync`](../../../../Services/Testing/TestImpactEstimator.cs), строки **74, 79–80**. `originalPaths` построен только из исходных `BaselineSession.Inventory` C# `Project.Documents`. Он не содержит исходные `AdditionalDocuments`. Однако новый `RemoveWhere` принимает отсутствие в этом множестве, отсутствие файла на диске и отсутствие в **текущей** input map за доказательство полного отката нового source.

Реальный исходный AdditionalFile может иметь расширение `.cs`: расширение не является ролью, что прямо закреплено в `InputRole`. Если он включён glob'ом `AdditionalFiles Include="Data/*.cs"`, после физического удаления и повторной MSBuild evaluation он исчезает из текущих `AdditionalDocuments` и input map. Никакая правка `.csproj` для этого не нужна. Его уже доставленное watcher-событие остаётся в baseline journal, но новая ветка стирает путь до mapper. Старая дельта не откатана, а тестовый проект исчезает из плана. Строка «non-C# changes may have been missed» не заменяет обработку события, которое было успешно наблюдено **до** reload.

### Независимое воспроизведение

Отдельный stdio-процесс из существующего Release `bin/Release/net10.0/RoslynMcpServer.dll`; `get_mcp_server_info` подтвердил **1.6.1.0**. В изолированном проекте до первой загрузки заданы:

```xml
<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<GenerateAssemblyInfo>false</GenerateAssemblyInfo>
<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
<Compile Include="Cases.cs" />
<AdditionalFiles Include="Data/*.cs" />
```

`Cases.cs` содержит распознаваемый существующий `Cases.Case`; `Data/Payload.cs` присутствует до baseline, компилируемым документом не является. Содержимое `.csproj` после первой загрузки не меняется.

1. Загрузка `Specs.csproj`, `configuration=Debug`; исходный ответ: `Changed since load: 0 members in 0 files`, `Run plan: 0 projects`.
2. `File.Delete` эквивалентен `Path.unlink` для `Data/Payload.cs`; ожидание native watcher. Ответ: `Changed since load: 0 members in 1 files`, `Run plan: 1 projects`, `Specs.csproj whole project`, явная причина с путём `Data/Payload.cs`, выбран `Cases.Case`.
3. `load_workspace` **того же неизменённого** `Specs.csproj`, `configuration=Release`. Несовпадение явно переданной конфигурации исключает cache hit в `LoadCoreAsync`. Лог сессии `[cli-43ah]` содержит новую MSBuild provenance capture и `workspace_load_designtime`, запуск watchers и `Loaded Roslyn workspace ... Configuration=Release`.
4. `Payload.cs` по-прежнему удалён. Ответ: `Changed since load: 0 members in 0 files`, `Directly selected: 0 tests`, **`Run plan: 0 projects`**; осталась только общая reload uncertainty.

Локальные первичные материалы: `TestResults/task-08-review-round-3-probe/additional_probe.py`, `server-info.txt`, `initial-load.txt`, `initial.txt`, `deleted-before-reload.txt`, `reload.txt`, `deleted-after-reload.txt`. Fixture находится в исключённом из root Compile каталоге `spikes/AdapterTestNames/artifacts/task08-r3-review-probe`. Repo build/test не запускались. MSBuild workspace загрузился успешно, без workspace diagnostic lines; restore assets fixture не создавались, что открыто указано в health output. Сам тест через адаптер не запускался: доказан публичный переход уже выбранного проекта в пустой план при сохраняющейся дельте входа.

Первый sandboxed запуск probe не смог подключить buildhost named pipe и не дошёл до workspace; он не учитывается как воспроизведение. Повтор вне sandbox успешно прошёл все preconditions. Tracked C#/project files рецензент не менял.

**Требуемое исправление:** отбрасывать отсутствующий путь как полностью откатанный новый source только при доказательстве, что в исходном baseline он не был другим входом. Сохранить исходную роль/принадлежность входа либо достаточно узкую provenance действительно нового source; отсутствие в исходном C# inventory и в текущей map этого не доказывает. Нужна регрессия с исходным globbed AdditionalFile `.cs`, наблюдённым удалением и реальным reload без `.csproj` дельты. Текущие guards для нового source, rollback, исходного Known/Unknown/context miss и настоящего gap должны сохраниться.

## Проверки и границы вердикта

Рецензент прочитал [RED](task-08-review-round-3-red.md) и [focused GREEN](task-08-review-round-3-green.md), сверил первичный RED TRX: **8 executed / 3 passed / 5 expected failed**, без skipped/timeout/aborted. Это проверяет историю R2-001, а не покрывает R3-001. Новые тесты не ослабляют прежние assertions, но не включают исчезновение исходного non-source `.cs` из refreshed map.

По переданным координатором результатам финальная Release-сборка, **65 focused**, **123 SourceStructure** и полный main **1977/1977** прошли на 1.6.1. Дополнительно рецензент прочитал локальный `TestResults/task-08-round-3-lifecycle/lifecycle.trx`: **13 executed / 13 passed**, failed/skipped/timeout/aborted=0. Самостоятельного повторного build/test параллельно координатору не было. Зелёный suite не отменяет независимое воспроизведение R3-001.

Это code review третьего раунда, не изменение task/индексов и не разрешение следующего раунда. Координатор владеет final status bookkeeping, решением владельца, исправлениями, повторной validation и commit/push. Task-09 `run`, MTP, будущий граф, общий аудит и Linux validation не проверялись. Известные coarseness/cost/watcher limits вне R2-001 не переоткрывались.

# Независимое ревью: BuildHost/task-01 — тайминги запросов и фактический load graph

Ты проводишь независимое ревью. Не меняй файлы репозитория, кроме файла с отчётом.

## Куда сохранить результат

Отчёт сохрани рядом с этим запросом:

```
docs/workspace-load-performance/research-buildhost-fork/evidence/task-01-review/review-<model_name>.md
```

`<model_name>` — короткое имя модели в нижнем регистре через дефис, как в ревью task-00
(`review-gpt-6-1-sol.md`, `review-grok-4-7.md`). В начале отчёта укажи модель, дату и проверенный
commit (HEAD ветки на момент ревью). Если писать файлы ты не можешь, выведи весь отчёт одним
markdown-документом; владелец сохранит его под этим именем.

## Где лежит

Репозиторий: https://github.com/VuDZ/RoslynMcpServer, ветка `research/workspace-load-performance`.
Запрос написан при HEAD `a35f682`. Коммиты задачи:

- `ac92063` — трассировка в форке, отчёт трассы в spike, фикстура, прогоны на Linux;
- `a35f682` — прогоны владельца: Roslyn.slnx на двух компьютерах с Windows, офисное решение, фикстура
  на Windows, вложенные сборки офиса по binlog task-00a.

Постановка: [task-01](../../task-01-buildhost-graph-and-timings-hi.md). Отчёт:
[task-01-graph-and-timings.md](../task-01-graph-and-timings.md). Индекс:
[README](../../README.md). Предыдущая задача: [task-00](../../task-00-buildhost-fork-baseline-hi.md) —
форк принят, правила сравнения оттуда не ослаблять.

Upstream: `dotnet/roslyn`, commit `35d9211b841e7613c1d2f8f5af6d628ace696c4c` (пакет
`Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0).

Файлы:

- форк, `forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/`: `Tracing/BuildHostTrace.cs`
  (новый), `Rpc/RpcServer.cs`, `Build/ProjectBuildManager.cs`, `MSBuild/ProjectFile/ProjectFile.cs`,
  `AbstractBuildHost.cs`, `Program.cs`; `forks/roslyn-buildhost/README.md`;
- spike, `spikes/BuildHostFork/`: `Trace/` (все файлы и `Models/`), `Program.cs`,
  `Invoke-BuildHostForkBaseline.ps1`, `BuildHostForkCommon.ps1`, `Test-BuildHostTrace.ps1`;
- общий код: `spikes/ParallelSearch/SolutionLoader.cs`, `Models/LoadWindow.cs`;
  `spikes/LoadTimeline/TimelineReport.cs`, `LoadTimeline.csproj`;
- документы: отчёт, task-файл, README исследования, папки `evidence/task-01-roslyn-windows/`,
  `task-01-roslyn-office-pc/`, `task-01-office/`.

Сырых трасс в репозитории нет (`results/` исключены). Офисные `client.json`, `buildhost-*.jsonl`,
`graph.json` и `names-private.json` содержат пути и остаются у владельца; в Git — только сводки и
`report.md` с заглушками.

## Что проверить

1. **Трассировка не меняет поведение форка.**
   - Без `ROSLYN_BUILDHOST_TRACE_DIRECTORY` трассировка не создаёт объектов и ничего не пишет: проверь
     все места вызова `BuildHostTrace` (`?.`, ранние выходы), нет ли побочных эффектов при выключенной
     трассировке (вычисление аргументов, аллокации, изменение порядка вызовов).
   - Протокол RPC и ответы не меняются; ошибка записи трассы (нет каталога, диск полон, файл занят) не
     ломает host и не меняет исход запроса.
   - Потокобезопасность записи JSONL и вложенность span по async-потоку: может ли span одного запроса
     стать родителем span другого; что будет, если запросы когда-нибудь пойдут параллельно.
   - Span возврата TFM (`restoresTargetFramework`) и смены TFM в `ProjectBuildManager.BuildProjectAsync`
     покрывают ровно вызовы `ReevaluateIfNecessary`, порядок операций upstream не изменён.
   - Стиль — Roslyn (`forks/` исключён из `docs/code-style.md`); в трассу не попадают содержимое
     исходников, окружение и секреты.

2. **Часы и привязка к операциям клиента.**
   - Верно ли, что `IProgress<ProjectLoadProgress>.Report` в 5.9.0 вызывается синхронно сразу после
     остановки таймера операции, так что момент вызова — конец операции. Клиентского кода в форке нет:
     сверь с `MSBuildProjectLoader.Worker` (`DoOperationAndReportProgressAsync`) в `dotnet/roslyn@35d9211b`.
     Если вызов асинхронный или отложенный, разбивка окна неверна.
   - `ClockCheck`: выбор запроса (`MinBy` по началу), допуск 1 мс, смысл «unmatched». В офисе 40
     unmatched трактуются как операции проектов, ушедших в нетрассируемый net472-host. Может ли так же
     выглядеть настоящая ошибка часов или пропуск запроса, и как отличить.
   - Общие часы `Stopwatch` между процессами на Windows (QPC) и Linux (`CLOCK_MONOTONIC`) — достаточно ли
     проверки на каждой загрузке, чтобы ставить span host между отметками клиента.

3. **Разбивка окна (`WallBreakdown`) и стоимости (`ProjectCosts`).**
   - Собственное время span (минус дочерние), иначе самая внутренняя операция клиента без работы host,
     иначе промежутки клиента. Складываются ли части ровно в окно; что с перекрытиями (категория
     «several host spans at once»), с работой вне окна, с span без закрытия.
   - Отнесение записи строки трассы дочернего span к родителю — оценена ли эта ошибка.
   - Медианы по раундам в `TraceRounds`: медиана каждой категории отдельно не обязана складываться в
     медиану окна — так ли это подано в отчёте.

4. **Граф (`LoadGraphBuilder`) и повторы (`RepeatInventory`).**
   - Узел — путь и эффективный контекст (TFM сборки, вычисленные Configuration/Platform). Хватает ли этого
     для «одного пути с разными properties»; что граф теряет (global properties кроме TFM).
   - Происхождение рёбер: элемент ProjectReference собранного проекта (с `ReferenceOutputAssembly`),
     загрузка клиентом во время Resolve, запрос output path. Нет ли рёбер, приписанных не тому проекту.
   - Незагружаемые записи решения (`.vcxproj`, `.shproj`) и проекты нетрассируемого host — узлы без
     сборки. Не выдаётся ли граф за полный (контракт task-01 это запрещает).
   - Повторы: повторные корневые evaluation, загрузки из кеша `ProjectCollection`, повторные сборки
     (путь, TFM), output path на уже загруженном проекте. Могут ли повторы остаться незамеченными.

5. **Фикстура `Test-BuildHostTrace.ps1`.** 14 проверок: проверяет ли каждая то, что заявляет, или может
   пройти пустой (например, при нуле найденных span). Условный import, два TFM, `ReferenceOutputAssembly=false`,
   проект вне решения, `SetTargetFramework`, два запроса output path к `.proj` и второй из кеша. Каких
   случаев из контракта не хватает.

6. **Сравнение и цена трассировки.** `Invoke-BuildHostForkBaseline.ps1 -Trace` трассирует только форк,
   оригинал — нет; identical на 434 и 142 проектах. Корректно ли этим доказывать, что трассировка ничего
   не меняет в ответе, и честно ли подана цена (×1,000–1,04, не отделена от разброса).

7. **Выводы отчёта против данных.**
   - Числа разделов согласованы между собой, со сводками в `evidence/task-01-*` и со статусами в README
     и task-файле.
   - Измерения и оценки разделены: возврат TFM на Roslyn.slnx (измерен по span владельцем), 5 проектов
     net472-host в офисе (измерены по `client.json`, включают старт процесса), разогрев первого проекта
     (~2,2 с — не отделён от стоимости проекта), возврат TFM на офисном компьютере (~4,4 с — оценка).
   - Вложенные сборки офиса взяты из binlog task-00a (другой прогон, сервер 1.5.14), а граф — из трассы:
     обоснован ли вывод «2 740 вложенных запусков — только `GetTargetPath`/`GetTargetFrameworks`, ~0,5 с,
     полных повторных сборок нет»; верно ли сопоставление 1 435 `GetTargetPath` с 1 435 рёбрами.
   - Ранжирование кандидатов для task-02 и вывод «на медленной машине evaluation весит больше сборки»:
     не сильнее ли данных (одна загрузка на машину, 3 раунда).
   - Выполнены ли пункты «Проверки и результат» контракта task-01: несколько TFM, условные imports,
     один путь с разными properties, ROA=false, повторные query, cold/warm, оценки против точных span,
     overhead логирования, graph witness, inventory повторов, critical-path профиль, кандидаты, 2 882
     запуска не как полные сборки. Что не выполнено или выполнено слабее, чем записано.

8. **Публикация.** В tracked-файлах ветки нет имени офисного решения, его путей и имён проектов;
   `report.md` офиса — с заглушками. Имена Roslyn допустимы.

9. **Стиль.** Новый и изменённый C# в `spikes/` — по `docs/code-style.md` (прочитать полностью
   перед замечаниями по стилю). Старый код, который эти коммиты не меняли, не оценивать.

## Что можно запустить

С .NET SDK 10 и PowerShell 7 из корня репозитория:

- `dotnet build spikes/BuildHostFork -c Release`;
- `pwsh spikes/BuildHostFork/Test-BuildHostTrace.ps1` — собирает форк и spike, грузит фикстуру с
  трассировкой и выполняет 14 проверок;
- `pwsh spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1 -Solution RoslynMcpServer.sln -Runs 3 -Trace`
  — сравнение и `trace-summary.md`, `trace/fork-round<N>/report.md` в `spikes/BuildHostFork/results/`;
- отказные сценарии task-00 с изменённым форком: `pwsh spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1`.

Production-код этими коммитами не менялся: убедись по `git diff ac92063~1 a35f682 --stat`. Укажи, что
именно запускал и на какой ОС. Если не запускал, напиши прямо.

## Формат отчёта

1. **Вердикт:** можно ли принять task-01 в записанном объёме; если нет — что блокирует.
2. **Находки** по убыванию серьёзности: blocker / major / minor / nit. Для каждой: файл:строка; что не так;
   конкретный сценарий, при котором это ломает вывод или поведение; доказательство (цитата кода, ссылка
   на строку upstream, команда); предлагаемое исправление.
3. **Проверено и в порядке:** пункт и как проверено.
4. **Не проверено** и почему.

Не выдавай предположение за факт. Если вывод сделан без чтения кода или без запуска, пометь его как
непроверенный.

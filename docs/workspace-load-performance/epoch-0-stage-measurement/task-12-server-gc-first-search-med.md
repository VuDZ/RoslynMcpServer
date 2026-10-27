# D/E0/task-12 — Server GC для загрузки и первого поиска

- Дата постановки: 2026-10-23.
- Рекомендуемые модели: Claude Opus 5.5; альтернатива: GPT-6.1 Sol.
- Рекомендуемый reasoning: GPT: medium.
- Примерная сложность: 2/5; класс `med`.
- Риск ошибки: средний. Server GC держит больше памяти, а MCP-сервер долго живёт и
  большую часть времени простаивает. Выигрыш по времени легко купить ростом памяти
  после поиска, а этого не видно в одной метрике wall.
- Необходимые способности: `анализ_производительности`, `проектирование_проверок`.
- Статус: **accepted, 2026-10-27** (решение владельца: приёмка после независимого ревью). Объём: server GC в сервере (1.5.12) и сравнение режимов GC в офисном скрипте; production-код уже в `main` (f071de4). Ревью — 2 раунда, раунд 2 ACCEPT with minor notes; финальный код — Linux Release main 1426/0/0, SourceStructure 123/0/0, AnalyzerLifecycle 13/0/0 ([отчёт ревью](evidence/first-search-review-2026-10-27.md)). Прежний статус: **in progress, 2026-10-23 — Windows-матрица выполнена, 48/48 процессов,
  self-checks PASS, численный go (server) на измеренном стенде; 44 Failure загрузки;
  независимое ревью, формальная приёмка и офисный контроль pending**
  ([отчёт и validation](evidence/task-12-server-gc-first-search.md)).
  По решению владельца server GC включён в сервере 1.5.12 в research-ветке для офисного контроля.
  Перенос в main — после офисного прогона, независимо от его результата
  (см. «Включение в research»).
  Офисный контроль на 1.5.13 выполнен 2026-10-23, A/B на одной сборке:
  - первый поиск 19,5 → 16,1 с (×0,82), паузы GC 4,9 → 1,1 с;
  - загрузка не изменилась, рабочий набор после загрузки +17%;
  - [офисный прогон](evidence/office-first-search-2026-10-23.md#второй-прогон-сервер-1513-17071718).

  Пороги task-12 относились к матрице на Roslyn.slnx; офисный ×0,82 ими не оценивается.
- Depends on: [task-11](task-11-parallel-project-search-spike-hi.md).
- Модель, реализовавшая задачу: Claude Opus 5.5.
- Модели, проводившие ревью: claude-opus-5-5, новая сессия; раунд 1 и раунд 2 2026-10-27 ([отчёт ревью](evidence/first-search-review-2026-10-27.md)).
- Количество раундов ревью: 2.
- Количество исправлений после ревью: 2 (T12-R1-03 частично).
- Отчёт о реализации, ревью и validation: [Windows-матрица и validation результатов](evidence/task-12-server-gc-first-search.md);
  scope — запуск существующего spike и evidence, без изменения production и приёмки кода.

## Вопрос

Сокращает ли server GC время загрузки и первого поиска Roslyn.slnx, не увеличивая
заметно память процесса? Решение владельца 2026-10-23: проверить server GC.
Production включается только после go.

## Основания

[Анализ task-11](evidence/task-11-parallel-project-search.md#анализ-результатов-2026-10-23):

- при DOP 1 паузы GC занимают 8,0–8,3 с из 17,8–20,7 с первого поиска (40–46%);
- за один поиск проходит около 200 сборок, каждая не младше gen1;
- рабочий набор растёт с 0,8 до 5,4 ГБ;
- параллельный поиск по проектам паузы не сокращает, общий ratio упирается в 0,80–0,85.

Ни сервер, ни spike не задают режим GC, поэтому работает workstation concurrent GC.
Блокирующие сборки в нём выполняет один поток. Пробный запуск spike 2026-10-23
(Linux, 2 vCPU) показал `GCGen0MaxBudget`:

- workstation — 8 МБ;
- server — 200 МБ, DATAS включён (`GCDynamicAdaptationMode=1`).

Бюджет gen0 у workstation зависит от размера кеша CPU, поэтому на стенде он может быть
другим. Поэтому каждый прогон записывает полную конфигурацию GC.

## Границы

- Только `spikes/ParallelSearch` и evidence. Production, Version, CHANGELOG
  и runtimeconfig сервера не менять.
- Тот же стенд и те же запросы, что в task-11:
  - Roslyn.slnx на commit 013d3a758df6c137497ff37a93f0d4bed103853a;
  - SDK 10.0.301;
  - `CSharpCompilationOptions` и `NoSuchDeclarationName7d3f2a`.
- Режим задаётся только переменными окружения дочернего процесса:
  `DOTNET_gcServer` и `DOTNET_GCDynamicAdaptationMode`. Если в окружении есть
  другие GC-настройки (`DOTNET_GC*` и аналоги), скрипт отказывается запускаться.
- Подбор бюджета gen0, `GCHeapCount`, `GCConserveMem` и сочетание с параллельным
  поиском — вне задачи.

## Реализация

- Результат spike записывает:
  - `gc` — фактический режим: `GCSettings.IsServerGC`, `LatencyMode`,
    `GC.GetConfigurationVariables()`;
  - `loadGcPauseMs` — паузы GC во время загрузки;
  - `final` — память после второго поиска без принудительной сборки: working set,
    пик working set процесса, размер кучи GC и committed. Это то, что сервер
    продолжит держать после поиска.
- `spikes/ParallelSearch/Invoke-GcModeMatrix.ps1`:
  - режимы `workstation` и `server`, дополнительно `server-nodatas`;
  - DOP 1 и 4;
  - прогрев + 5 замеров;
  - порядок конфигураций меняется каждый раунд;
  - новый процесс на каждый прогон, всего 48 процессов.
- Сводка отбрасывает прогон, у которого фактический режим GC не совпал
  с запрошенным; вердикт тогда — inconclusive.
- Общие функции обоих скриптов матрицы вынесены в `MatrixCommon.ps1`.
  Проверка рефакторинга: `-SummarizeOnly` по результатам task-11 дал
  те же `summary.md` и `summary.json` с точностью до переводов строк.

## Пороги

Пороги зафиксированы до измерений и не подгоняются. Сравнение идёт с workstation
при DOP 1 на обоих запросах.

**go** для режима, если выполнены все условия:

- медиана wall первого поиска ≤ 0,80;
- медиана загрузки ≤ 1,05;
- медиана пикового working set процесса ≤ 1,25;
- медиана итогового working set ≤ 1,25;
- все ответы запроса совпали во всех режимах и DOP.

Иначе — no-go, а при неполной матрице или чужом режиме — inconclusive.
Строки DOP 4 информационные: они показывают, меняет ли server GC вывод task-11,
но вердикт не определяют.

## Проверки и результат

Перед коммитом:

- Release build spike;
- self-checks;
- end-to-end скрипта на малом решении, включая отказ при посторонней GC-переменной
  и inconclusive при подменённом режиме;
- `-SummarizeOnly` по данным task-11;
- Release build решения;
- полный main CI по [AGENTS.md](../../../AGENTS.md).

Результат — `evidence/task-12-server-gc-first-search.md`. В нём таблица
режим × DOP × запрос, вердикт и рекомендация для production.

При go включение в сервере — отдельное изменение с решением владельца.
Оно сводится к `<ServerGarbageCollection>true</ServerGarbageCollection>`
в `RoslynMcpServer.csproj`. После включения нужен офисный контроль: офисный стенд
медленнее и на 6 ядрах, тогда как матрица идёт на 16 потоках.

## Включение в research, 2026-10-23

Решение владельца: включить в research-ветке и проверить на офисных машинах.
Перенос в main — после офисного прогона, независимо от его результата.

- `RoslynMcpServer.csproj`: `<ServerGarbageCollection>true</ServerGarbageCollection>`,
  версия 1.5.12. `DOTNET_gcServer=0` в окружении сервера возвращает workstation GC
  без пересборки: переменная окружения перекрывает runtimeconfig.
  Это проверено на spike и на опубликованном сервере.
- `workspace_search_summary` пишет `serverGc`, `gcPauseMs` и `gcCollections` за поиск.
  Счётчики общие для процесса, поэтому параллельная работа сервера в них тоже попадает.
- `Measure-WorkspaceLoad.ps1` показывает режим и паузы в строке поиска и медиану пауз.
  Кроме того, он записывает значение `DOTNET_gcServer` в окружение отчёта.
- `Invoke-FirstSearchDiagnostics.ps1 -GcModes 'server,workstation'` (по умолчанию)
  прогоняет одну опубликованную сборку дважды: сначала server, затем workstation
  через `DOTNET_gcServer=0`. Trace снимается только в первом проходе. Сводка
  `comparison.md` ставит медианы рядом и показывает фактический режим из лога поиска.
  Проходы идут последовательно, а не вперемешку. Поэтому дрейф офисной машины
  (антивирус, кеш диска) за время прогона попадает в разницу режимов.
  Уточнение 2026-10-27 (ревью, T12-R1-03): с task-16 значение по умолчанию — `-GcModes server`, и
  `comparison.md` сравнивает только прогрев; для сравнения режимов GC нужен `-GcModes server,workstation`.
  Паузы GC в отчётах общие для процесса: идущий прогрев добавляет к ним свои сборки.
- Тесты:
  - `ServerGcConfigurationTests` проверяет `System.GC.Server` в runtimeconfig сервера;
  - `DeclarationSearchTimingTests` проверяет поля GC сводки поиска;
  - `scripts/Test-MeasureWorkspaceLoad.ps1` проверяет строку поиска с GC и без него;
  - `scripts/Test-FirstSearchDiagnostics.ps1` проверяет `comparison.md`.

Офисный запуск, из обновлённой ветки:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-FirstSearchDiagnostics.ps1 -Workspace <путь>\App.sln -SymbolName MainViewModel -Platform x64
```

## Запуск

Из дерева ветки `research/workspace-load-performance` на Windows-машине task-11:

```powershell
powershell -ExecutionPolicy Bypass -File .\spikes\ParallelSearch\Invoke-GcModeMatrix.ps1 -Solution <путь>\Roslyn.slnx
```

Результаты попадают в `spikes\ParallelSearch\results\gc-<время>`. Скрипт печатает
команды `git add/commit/push`; после разбора папка удаляется из репозитория.

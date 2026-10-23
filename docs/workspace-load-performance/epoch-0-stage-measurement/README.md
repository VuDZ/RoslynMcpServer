# D/E0 — Разбивка загрузки по стадиям: карта выполнения

- Статус плана: **draft, 2026-10-09; независимое ревью не проводилось**.
- Статус исполнения: **in progress, 2026-10-10 — task-00: публичные прокси сняты ([результаты](evidence/task-00-current-version.md)), офисный замер ожидается; task-01–task-03 не начаты; task-04 implemented: 44 прогона, no-go, формальная приёмка pending единого main CI ([отчёт](evidence/task-04-parallel-loader-spike.md)); task-05/task-05.1 implemented: профиль получен, обе направленные калибровки не прошли, диагностические планы не проходят gate, no-go; шаг 2 не выполнялся; scope — standalone spike и evidence, без production; формальная приёмка code/tests pending main CI ([отчёт](evidence/task-05.1-profile-retry.md))**.
- Task-07, 2026-10-10: **implemented, 40 прогонов, no-go; независимое ревью ACCEPT; code/tests acceptance pending main CI 1362 passed / 1 прежний SDK-specific failed**. Scope — standalone spike/evidence, production и E1 не менялись; [review/validation](evidence/task-07-core-first-parallel-spike/validation.md).
- Task-07.1, 2026-10-10: **implemented, шаг 0 и 52 попытки матрицы завершены, no-go; независимое ревью round 2 ACCEPT**. Scope — standalone spike/evidence; эквивалентность восстановлена, PowerToys N=4 depth2 25,228 с против baseline 41,825 с, Files cluster N=2/4 невыполнимы при ε=0,15; code/tests acceptance pending main CI (1362 passed / 1 прежний SDK-specific failed / 0 skipped), commit не создан; [результат](evidence/task-07.1-core-first-equivalence-and-partition.md), [review/validation](evidence/task-07.1-core-first-equivalence-and-partition/validation.md).
- Зависимость эпохи: нет.
- Task-07.2, 2026-10-10: **implemented, 36 matrix runs +6 дополнительных load processes, no-go; независимое ревью round 2 ACCEPT** для standalone реализации/evidence с ограничениями корректности Roslyn и детерминизма deadline-limited N8. Code/tests acceptance pending main CI 1362/1/0; [validation](evidence/task-07.2-refusal-fast-planner-roslyn/validation.md). Production/E1 не активированы, commit не создан.
- Результат: время каждой стадии ordinary load и первого семантического запроса
  на реальном большом решении; вердикт по кандидатам E1.
- Канон темы: [README](../README.md).

Актуализация **2026-10-22**: task-00 accepted в scope офисного замера по
существующим логам; task-02 partial, task-06 manual overlay pending;
[task-08](task-08-source-search-and-office-results-med.md) accepted для замены API
и офисного MainViewModel, [task-09](task-09-first-search-io-and-private-project-profile-hi.md)
in progress для оставшихся 34 с. [Форк BuildHost](../research-buildhost-fork/README.md)
оформлен отдельным planned исследованием. Этот checkpoint не закрывает E0
или прежние correctness no-go; [validation](evidence/commit-2026-10-22.md).

## Задачи

Дополнительный scope по запросу владельца **2026-10-22**: проектные тайминги
design-time build и первого name-based поиска в сервере **1.5.8**, top-10 в скрипте
замера и полные строки в `runs.json`. Статус **accepted in the requested scope**:
Release build, main CI 1409/0/0, SourceStructure 123, AnalyzerLifecycle 13/0/0,
PowerShell self-check и end-to-end PASS; validation —
[отчёт](evidence/project-timings-2026-10-22.md). Task-02 реализована частично;
task-01 и полная приёмка эпохи остаются pending. Офисный прогон 1.5.8 от 21:46:17
получен: 142 проекта, load 48,64 с, поиск 36,20 с; второй binlog имеет отказ reader.

Следующий scope владельца **2026-10-22**, сервер **1.5.9**: source-only поиск с
проверкой эквивалентности и task-06 forward-compatible replay. Реализация проверена:
Release build, main CI 1426/0/0, SourceStructure 123, AnalyzerLifecycle 13/0/0,
end-to-end и реальный replay двух PowerToys binlog PASS. Офисная проверка 1.5.9
от 22:15:22: первый поиск MainViewModel −5,60%, все четыре ответа побайтно совпали
с 1.5.8, оба binlog читаются без ошибок (второй format 27 / 49 project starts),
capture Complete / 285 confirmed bindings. Новая полная загрузка PowerToys с
overlay остаётся manual pending; scope/validation —
[отчёт](evidence/task-06-provenance-forward-compatible-replay.md).
Запрошенный scope **accepted, 2026-10-22**; проверенный пакет сервера 1.5.9 и
скрипта собран в `artifacts/RoslynMcpServer-1.5.9-win-x64.zip`.

Разбор оставшихся 34 с поиска **2026-10-22** завершён в исследовательском scope:
private_project занимает 12–13 с; 12–14 других проектных вызовов имеют характерные
секундные интервалы. На Roslyn 5.9.0 локально воспроизведён повтор чтения при
IOException (5 × 200 мс); наличие таких ошибок в офисном решении ещё не доказано.
Production код не менялся, ускорение не заявляется. Следующий диагностический шаг —
ошибки чтения и разделение первого построения индексов/compilation;
[выводы и validation probe](evidence/first-search-investigation-2026-10-22.md).

Предкоммитный checkpoint **2026-10-21**: Release builds, self-checks и полный main
CI зелёные (**1388 passed / 0 failed / 0 skipped**, SourceStructure 123 passed);
см. [сохранение серии и границы validation](evidence/commit-2026-10-21.md).
Старые статусы main CI pending/commit не создан описывают 2026-10-10.

Следующий шаг по решению владельца **2026-10-21** —
[task-07.3](task-07.3-evaluated-load-graph-hi.md): получить достоверный load graph,
проверить один Roslyn N=4 на корректность и только затем измерять полный wall
с ценой получения графа. Статус planned; реализация не начата.

- [task-00 — Замер текущей версии по существующим логам](task-00-current-version-log-measurement-low.md) — `low`, **accepted, 2026-10-22: публичные прокси и офисный замер получены ([результат](evidence/task-00-current-version.md#офисный-замер-получен-2026-10-22))**; Depends on: —
- [task-01 — Время стадий загрузки в логе](task-01-load-stage-timings-med.md) — `med`, **planned**; Depends on: —
- [task-02 — Сводка design-time build из binlog](task-02-design-time-build-summary-med.md) — `med`, **partially implemented, 2026-10-22: проектная разбивка добавлена для следующего офисного прогона; полный task-01/task-02 pending ([validation](evidence/project-timings-2026-10-22.md))**; Depends on: [task-01](task-01-load-stage-timings-med.md)
- [task-03 — Baseline на реальном решении и выбор hotspot](task-03-real-solution-baseline-med.md) — `med`, **planned**; Depends on: [task-00](task-00-current-version-log-measurement-low.md), [task-01](task-01-load-stage-timings-med.md), [task-02](task-02-design-time-build-summary-med.md)

- [task-04 — Spike: параллельная загрузка несколькими MSBuildProjectLoader](task-04-parallel-loader-spike-hi.md) — `hi`, **implemented, 2026-10-10: no-go; code/tests acceptance pending из-за SDK-specific main CI; scope — standalone spike и evidence, без изменения production load ([validation](evidence/task-04-parallel-loader-spike.md#validation-и-проверка-исполнителем))**; Depends on: —
- [task-05 — Профиль стоимости проектов и разбивка параллельной загрузки по профилю](task-05-profile-guided-partition-hi.md) — `hi`, **implemented, 2026-10-10: повтор шага 1 в task-05.1 дал no-go — модель непригодна, диагностические планы не проходят gate; шаг 2 не выполнялся; scope — standalone spike и evidence, без production; code/tests acceptance pending единого main CI ([validation](evidence/task-05.1-profile-retry.md#эквивалентность-и-validation))**; Depends on: [task-04](task-04-parallel-loader-spike-hi.md)
- [task-05.1 — Повтор шага 1 task-05: forward-compatible профиль и стоимость по разрывам](task-05.1-profile-retry-hi.md) — `hi`, **implemented, 2026-10-10: profile/калибровка/диагностическая симуляция завершены, no-go; scope — standalone spike и evidence, без production; изолированное native время недоступно, counts и границы разрывов сохранены; code/tests acceptance pending main CI: 1362 passed / 1 прежний SDK-specific failed ([validation](evidence/task-05.1-profile-retry.md#эквивалентность-и-validation))**; Depends on: [task-05](task-05-profile-guided-partition-hi.md)
- [task-06 — Provenance capture: forward-compatible replay binlog](task-06-provenance-forward-compatible-replay-med.md) — `med`, **implemented, automated validation accepted, 2026-10-22; manual PowerToys overlay pending ([отчёт](evidence/task-06-provenance-forward-compatible-replay.md))**; production bugfix, найденный в ревью task-05; Depends on: —
- [task-07 — Spike: сначала общее ядро, затем группы поверх ProjectMap](task-07-core-first-parallel-spike-hi.md) — `hi`, **implemented, 2026-10-10: 40 прогонов и шаг 0 завершены, no-go; независимое ревью round 2 ACCEPT; scope — standalone spike/evidence, без production; code/tests acceptance pending main CI (1362 passed / 1 прежний SDK-specific failed), [validation](evidence/task-07-core-first-parallel-spike/validation.md)**; Depends on: —
- [task-07.1 — Core-first: эквивалентность, кластерное разбиение и причина замедления групп](task-07.1-core-first-equivalence-and-partition-hi.md) — `hi`, **implemented, 2026-10-10: шаг 0 и 52 matrix attempts, no-go; независимое ревью round 2 ACCEPT; scope — standalone spike/evidence, без production; code/tests acceptance pending main CI (1362 passed / 1 прежний SDK-specific failed / 0 skipped), [validation](evidence/task-07.1-core-first-equivalence-and-partition/validation.md)**; Depends on: [task-07](task-07-core-first-parallel-spike-hi.md)
- [task-07.2 — Core-first: отказ до загрузки, быстрый планировщик, прогрев BuildHost и Roslyn](task-07.2-refusal-fast-planner-roslyn-hi.md) — `hi`, **implemented, 2026-10-10: 36 matrix runs и 6 дополнительных load processes, no-go; независимое ревью round 2 ACCEPT для standalone реализации/evidence. PowerToys cluster N4 23,306 с / 56,11% baseline, отказы Files/RoslynN8 ≤105%; Roslyn parallel correctness и универсальный детерминизм deadline-limited N8 не достигнуты. Code/tests acceptance pending main CI (1362 passed / 1 прежний SDK-specific failed / 0 skipped), [validation](evidence/task-07.2-refusal-fast-planner-roslyn/validation.md)**; Depends on: [task-07.1](task-07.1-core-first-equivalence-and-partition-hi.md)

- [task-07.3 — Core-first: проверка на графе зависимостей загрузки MSBuild](task-07.3-evaluated-load-graph-hi.md) — `hi`, **planned, 2026-10-21: сначала один Roslyn N4 correctness check, при успехе — wall с ценой графа**; Depends on: [task-07.2](task-07.2-refusal-fast-planner-roslyn-hi.md)

- [task-08 — Source-only поиск и офисные результаты](task-08-source-search-and-office-results-med.md) — med, **accepted in the requested scope, 2026-10-22**, API equivalence и MainViewModel −5,60%; Depends on: task-02.
- [task-09 — I/O retries и профиль private_project](task-09-first-search-io-and-private-project-profile-hi.md) — hi, **in progress, 2026-10-23**: I/O гипотеза подтверждена офисом — ~24 из 31 с поиска уходят на повторы чтения отсутствующего `VersionInfo.cs`, исправление — task-13; CPU-разбивка остатка private_project pending ([офисный прогон](evidence/office-first-search-2026-10-23.md)); Depends on: task-08.
- [task-10 — Сводка исключений чтения и trace для офиса](task-10-first-search-exception-and-trace-diagnostics-med.md) — med, **implemented, 2026-10-23; Linux validation 1381/0/0, офисный end-to-end под Windows пройден ([отчёт](evidence/task-10-first-search-diagnostics.md), [офисный прогон](evidence/office-first-search-2026-10-23.md)); ревью нет, не accepted**; сервер 1.5.11, режим dotnet-trace и один скрипт офисного прогона; Depends on: task-09.
- [task-11 — Spike: ограниченный параллельный поиск по проектам](task-11-parallel-project-search-spike-hi.md) — hi, **in progress, 2026-10-23: Windows-матрица 48/48, self-checks PASS, численный no-go; 44 Failure загрузки, code/tests acceptance и независимое ревью pending ([отчёт](evidence/task-11-parallel-project-search.md))**; standalone, DOP 1/2/4/8 на Roslyn.slnx, пороги заданы заранее; Depends on: task-08.
- [task-12 — Server GC для загрузки и первого поиска](task-12-server-gc-first-search-med.md) — med, **in progress, 2026-10-23: Windows-матрица 48/48, self-checks PASS, go (server) на измеренном стенде; 44 Failure загрузки; офис на 1.5.13: первый поиск ×0,82, паузы GC 4,9 → 1,1 с ([офисный прогон](evidence/office-first-search-2026-10-23.md)); приёмка pending ([отчёт](evidence/task-12-server-gc-first-search.md))**; паузы GC — 40–46% первого поиска в task-11; пороги заданы заранее. По решению владельца server GC включён в сервере 1.5.12 в research для офисного контроля; офисный скрипт по умолчанию сравнивает server и workstation на одной сборке; Depends on: task-11.
- [task-13 — Отсутствующие исходные файлы без секундных повторов чтения](task-13-missing-source-files-fast-fail-med.md) — med, **implemented, 2026-10-23: сервер 1.5.13, main CI 1386/0/0, AnalyzerLifecycle 13/0/0; офис: 31,4 → 19,5 с при том же GC; ревью pending ([отчёт](evidence/task-13-missing-source-files.md))**: документ без файла сразу получает тот же пустой текст, что Roslyn отдаёт после 6 попыток по 200 мс; в офисе это ~24 из 31 с первого поиска; Depends on: task-09, task-10.
- [task-14 — Spike: persistent storage индексов Roslyn (SQLite)](task-14-roslyn-index-storage-spike-hi.md) — hi, **implemented, 2026-10-23: go на Roslyn.slnx — тёплый кеш 21,0 → 10,7 с и 17,9 → 3,9 с, пик памяти поиска 5,4 → 2,0 ГБ, холодный без потерь; пакет `SQLite3MC.PCLRaw.bundle` 2.3.5; включение в сервер — решение владельца ([отчёт](evidence/task-14-roslyn-index-storage.md))**: Roslyn ищет `SQLitePCLRaw.batteries_v2` и не находит, поэтому каждый процесс заново читает и разбирает все документы; проверить совместимый пакет, корректность кеша и тёплый первый поиск; пороги заданы заранее; Depends on: task-13.

Task-00 и task-01 независимы. Task-01 и task-02 правят один и тот же путь загрузки,
поэтому их выполняет один исполнитель последовательно. Task-04 не трогает путь
загрузки сервера и идёт параллельно с остальными; её вердикт — вход для E1
наравне с baseline task-03. Task-05 проверяет, снимает ли no-go task-04
разбивка по измеренной стоимости проектов без повторов markup: сначала
офлайн-симуляция, реальные загрузки — только при прогнозе ≥ 2×. Task-05.1 —
повтор её шага 1 после снятия блокеров чтения профиля. Task-06 — production
bugfix provenance capture (binlog BuildHost-net472 новее reader SDK); он меняет
путь provenance и требует AnalyzerLifecycle, задачи spike не трогают. Task-06 и
task-05.1 независимы. Task-07 снова открывает параллельную загрузку после
поправки к ревью task-05.1: ядро грузится один раз, группы — поверх
`ProjectMap` с ядром, без повторов и гонки на obj; не зависит от task-06.
Task-07 дала 1,84× при N=4, no-go только по эквивалентности с известными
механизмами (`SolutionDir`, незапрошенные native-проекты). Task-07.1 устраняет
их входами загрузки, уменьшает ядро кластерным разбиением и измеряет причину
замедления групп; порог прежний. Task-07.2 добавляет отказ до загрузки с
откатом на обычную загрузку (решение владельца 2026-10-10), быстрый
планировщик с бюджетом, разбор прогрева BuildHost по binlog и полный
`Roslyn.slnx` с прогнозом модели до замера.

## Общие правила эпохи

- Перед первой C# правкой — [AGENTS.md](../../../AGENTS.md) и полный
  [code style](../../code-style.md).
- Время измеряется через инжектируемый `TimeProvider`, без `DateTime.Now` и без
  новых статических таймеров.
- Запись лога не меняет ответ инструмента и не добавляет параметров.
- После production C# — весь SourceStructure, Release build, полный
  `Category!=AnalyzerLifecycle`. Task-01 и task-02 трогают путь load/prepare
  и provenance replay, поэтому дополнительно AnalyzerLifecycle с
  `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`. MCP-раннеры эту переменную не задают;
  используется раннер, который может её задать, и это фиксируется в отчёте.
- Каждое изменение кода — patch bump и строка в `CHANGELOG.md`
  (формат лога видят агенты через `read_log_tail`).

## Ревью плана эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено.
- Отчёты: —.

## Статистика ревью задач

- Охват: task-00–task-07.2, включая task-05.1; task-07.2 implemented, 2026-10-10: 36 matrix runs +6 дополнительных load processes, no-go, независимое ревью round 2 ACCEPT для standalone реализации/evidence; task-07.1 implemented, шаг 0 и 52 matrix attempts, no-go, round 2 ACCEPT; task-07 implemented, 40 прогонов, no-go, ACCEPT; принятых code/tests задач нет; task-04 реализована и измерена; task-05/task-05.1 завершили повтор шага 1 с no-go (модель непригодна); формальная приёмка code/tests pending.
- Фактические исполнители: task-04, task-05, task-05.1 — Codex (GPT-6); task-07, task-07.1 и task-07.2 — по одному сабагенту Codex (GPT-6, модель сессии унаследована), включая все фиксы каждой задачи.
- Фактические рецензенты: task-04, task-05 — проверка исполнителем и Claude Opus 5.5; task-05.1 — проверка исполнителем и Claude Opus 5.5 (no-go принят); task-07, task-07.1 и task-07.2 — по два независимых новых сабагента Codex (GPT-6), финальный ACCEPT standalone scope.
- Сумма раундов ревью задач: 12 (по два у task-04, task-05, task-05.1, task-07, task-07.1 и task-07.2).
- Исправленные замечания задач: task-07 — 3 must-fix одним фикс-раундом и порядок Redact; task-07.1 — 1 must-fix (zero-cost model refusal), 1 фикс-раунд; task-07.2 — 4 must-fix, 1 фикс-раунд (deadline materialization, zero balance, model-unavailable, actual diagnostic gate), development fixes/report-only disclosures отдельно в validation. Merge/метрики исправлены при разработке task-04.
- Отчёты: [task-04](evidence/task-04-parallel-loader-spike.md), [история task-05](evidence/task-05-profile-guided-partition.md), [task-05.1](evidence/task-05.1-profile-retry.md), [task-07](evidence/task-07-core-first-parallel-spike.md), [task-07 review/validation](evidence/task-07-core-first-parallel-spike/validation.md), [task-07.1](evidence/task-07.1-core-first-equivalence-and-partition.md), [task-07.1 review/validation](evidence/task-07.1-core-first-equivalence-and-partition/validation.md). [task-07.2](evidence/task-07.2-refusal-fast-planner-roslyn.md), [task-07.2 review/validation](evidence/task-07.2-refusal-fast-planner-roslyn/validation.md).

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: эпоха не принята; production task-01–task-03 не начаты; task-04 spike реализован и измерен, no-go; task-05/task-05.1 завершили повтор шага 1, no-go — модель непригодна, шаг 2 не выполнялся; формальная приёмка code/tests pending main CI ([validation](evidence/task-05.1-profile-retry.md#эквивалентность-и-validation)).
- Task-07: реализация и отрицательный результат прошли независимое ревью round 2 ACCEPT, 2026-10-10; формальная приёмка code/tests остаётся pending того же main CI; [validation](evidence/task-07-core-first-parallel-spike/validation.md).
- Task-07.1: standalone реализация и evidence отрицательного эксперимента прошли независимое ревью round 2 ACCEPT, 2026-10-10; шаг 0 и 52 matrix attempts завершены; формальная приёмка code/tests pending того же main CI; [validation](evidence/task-07.1-core-first-equivalence-and-partition/validation.md). Production и E1 не активированы, commit не создан.
- Принятый scope и дата: —.
- Task-07.2: standalone реализация/evidence отрицательного эксперимента приняты независимым round 2 ACCEPT, 2026-10-10; формальная code/tests acceptance pending main CI (1362 passed / 1 failed / 0 skipped), [validation](evidence/task-07.2-refusal-fast-planner-roslyn/validation.md). E0 не завершена, production/E1 не активированы.
- Финальная validation и baseline: —.
- Ограничения: —.
- Отчёты: —.

# D/E0/task-12 — Windows-матрица workstation/server GC

Дата: **2026-10-23**. Scope — запуск существующего spike, проверка результатов
и evidence. **Численный verdict: go (server)** на измеренном стенде.
Task-12 остаётся in progress: независимое ревью и формальная приёмка не объявлены;
production и офисный контроль не выполнялись.

## Стенд и выполнение

- Ветка обновлена fast-forward до `3c4dc0c3c1b8`; локальная работа сохранена.
  Код spike и сервера в этом запуске не менялся.
- Roslyn.slnx: чистый до запуска существующий клон `clones/roslyn-deep`, commit
  `013d3a758df6c137497ff37a93f0d4bed103853a`, Configuration=Debug.
- Windows 11 build 26200; Ryzen 7 9800X3D, 8 физических ядер / 16 логических
  процессоров, 93,7 GiB RAM. Перед запуском свободно около 40 GiB.
- Локальный SDK 10.0.301, runtime 10.0.9; `DOTNET_ROOT` и начало `PATH` указывали
  на этот SDK. Посторонних GC-переменных перед запуском не обнаружено.
- Запущен `powershell.exe` 5.1.26100.9444 с `-NoProfile -ExecutionPolicy Bypass
  -File spikes/ParallelSearch/Invoke-GcModeMatrix.ps1`, `-Solution` указанного
  клона и `-OutDirectory` стенда. Остальные параметры по умолчанию: workstation/server,
  DOP 1/4, два прежних запроса, прогрев и 5 измерительных раундов; restore включён.
- Run ID: `gc-20261023-133122`; 13:31:22–14:14:47 МСК, **43 мин 25 с**.
  Оригиналы — `E:\Devel\roslyn-mcp-bench\measure\parallel-search\gc-20261023-133122/`.
  Других сборок во время матрицы этот агент не запускал.

## Результат

Сравнение по медианам при DOP 1, как требует task-12:

- `CSharpCompilationOptions`: wall **21,458 → 12,864 с**, ratio **0,600**;
  паузы GC **8,400 → 1,891 с**; load **40,9 → 37,8 с**, ratio **0,925**;
  пик процесса и итоговый working set **5442 → 6194 MiB**, ratio **1,138**.
- `NoSuchDeclarationName7d3f2a`: wall **17,692 → 10,018 с**, ratio **0,566**;
  паузы GC **8,000 → 1,795 с**; load **40,7 → 37,9 с**, ratio **0,932**;
  пик процесса и итоговый working set **5320 → 6318 MiB**, ratio **1,188**.
- На обоих запросах пройдены все gates: wall ≤ 0,80, load ≤ 1,05,
  process peak и final working set ≤ 1,25, ответы идентичны.
  Порогов и параметров после измерений не меняли.
- DOP 4 — информационный контроль: server GC даёт 9,706 и 7,429 с.
  Эти строки не определяют verdict task-12 и не активируют параллельный поиск.

Неизменённые файлы скрипта: [summary.md](task-12-server-gc-first-search/summary.md),
[summary.json](task-12-server-gc-first-search/summary.json),
[environment.json](task-12-server-gc-first-search/environment.json).
Полные 99 переносимых файлов матрицы — в
[spikes/ParallelSearch/results/gc-20261023-133122/](../../../../spikes/ParallelSearch/results/gc-20261023-133122/).
Transcript с локальными путями оставлен на стенде.
[SHA-256 всех 100 оригинальных файлов](task-12-server-gc-first-search/raw-sha256.txt)
включает transcript. Копии сверены побайтно; `.gitattributes` в каталоге результатов
отключает преобразование переводов строк.

## Validation и ограничения

- Release build spike: PASS, 0 warnings / 0 errors. Self-checks: PASS — порядок,
  cancellation, исключение, пустое решение, проект без документов. Restore: exit 0.
- **48/48 процессов завершились**, exit скрипта 0; failedRuns и wrongModeRuns пусты.
  В каждой из 8 конфигураций есть прогрев и ровно 5 измерений.
- Проверен фактический `gc.server` и `configuration.ServerGC` для каждого процесса;
  DATAS configuration = 1, SDK 10.0.301, runtime 10.0.9, требуемые DOP.
- Во всех результатах 434 проекта и 50 201 документ; оба поиска содержат
  по 434 успешных проектных outcome. Ответы совпали во всех режимах/раундах/DOP
  и с baseline task-11: 4 объявления для match, 0 для missing.
- **Каждая загрузка записала 44 Failure.** Безошибочная загрузка полного Roslyn
  не подтверждена. Предыдущий отдельный диагностический inventory — в
  [task-11](task-11-parallel-project-search.md#ограничения-загрузки-и-приёмки):
  41 `.shproj` и 3 XAML. В этом запуске записаны счётчики, подробная диагностика
  отдельно не повторялась; равенство счётчиков не доказывает идентичность причин.
- В 24 server-процессах суммарные паузы первого поиска **1,700–2,005 с**, включая
  прогрев. Пауза около 8 с не воспроизвелась; условный `server-nodatas` не запускался.
  Длительности отдельных GC не измерялись. Колонка GCs в сводке — счётчик gen0;
  все три поколения записаны в `gcCollections` сырых JSON.
- По сообщению владельца, для `3c4dc0c` Release build решения и main CI прошли
  (1381 тест без падений). Локально этот запуск их не повторял; SourceStructure,
  AnalyzerLifecycle, Smoke/negative checks и SummarizeOnly task-11 не повторялись.
  Изменений C#/tests в этом запуске нет.

Рекомендация по измеренному стенду — server GC как кандидат для отдельного
изменения production с решением владельца. Нужен офисный контроль на 6 ядрах;
этот результат его не заменяет. Производственный csproj/runtimeconfig не менялись.
Коммит фиксирует данные, сводки, отчёт и статусы прогона; push не выполнялся.
Task-12 и эпоха не помечены accepted.

# Этап 1. Новый Roslyn и перенос форка BuildHost

- Рекомендуемые модели: gpt-6.2-astra.
- Рекомендуемый reasoning: xhigh.
- Примерная сложность: 5/5.
- Риск ошибки: высокий — несовместимые client/host могут дать неверный workspace без ошибки сборки.
- Необходимые способности: Roslyn/MSBuild, RPC, concurrency, differential testing, анализ lifecycle.
- Статус: planned, 2026-11-18.
- Depends on: [этап 0](task-00-baseline-and-dependencies-med.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.
- Принятый scope и дата приёмки: —.

Общий контракт: [план перехода](README.md). Собственные TFM и язык сервера остаются
`net10.0` / C# 14, если gate этапа 0 не выявил обязательную зависимость от runtime 11.

## Реализация

1. Обновить весь согласованный набор Roslyn и необходимые compile-time dependencies
   сервера/тестов. Не обновлять несвязанные библиотеки «заодно».
2. Взять точную ревизию исходников нового пакета. Обновить upstream copy,
   `UPSTREAM.md` с SHA-256, license/notices и plain SDK build projects.
   Наши deliberate patches хранить различимыми от исходного upstream.
   Код `forks/` следует стилю Roslyn; перед правками остального C# прочитать
   [code-style.md](../../code-style.md) полностью.
3. Перенести tracing и A/B/C по одному, с проверкой каждой части.
   Сохранить правила извлечения служебных global properties до evaluation,
   диагностику/порядок сообщений, ошибки reference-first, отмену и завершение worker.
   Принятые четыре worker и GC настройки не перенастраивать.
4. Обновить Contracts, assembly/informational versions и guard под один пакет.
   Сохранить отказ при несовпадении и отсутствие молчаливого fallback на stock host.
   Net472 host берётся из того же нового пакета; netcore TFM/roll-forward повторяют
   подтверждённые требования upstream, а не TFM сервера.
5. Проверить build/publish/pack Content replacement и transitive project-reference output.
   Обновить spike package references, version assertions, Rpc client/comparers
   и hardcoded output paths: сейчас `BuildHostForkCommon.ps1` содержит `net8.0`/net10.0.
   На каждой стороне стенда должен быть ожидаемый host и один согласованный client.
6. Проверить Roslyn reflection/MEF hooks и SQLite assembly identity/public key/native library.
   Сохранить persistent index и проверку изменения sources между процессами.
   Проверить fingerprints и отказ от несовместимого snapshot/index без глобального удаления кешей.
7. Для reaper, missing-source retry и checksum fallback воспроизвести исходный дефект
   на старом пакете и проверить новый. Удалять только ставший ненужным путь после
   regression test, сохраняя остальные причины очистки процессов/санитизации.

## Проверки

- Стенд без оптимизаций: новый stock host против нового неизменённого форка.
  Затем A, B, C и production A+B+C: ProjectFileInfo/RPC, проекты/TFM,
  options, compiler arguments, documents/generated/additional/analyzerconfig,
  project/metadata/analyzer references, outputs, globs и diagnostics.
- Использовать существующие `Invoke-BuildHostForkBaseline.ps1`,
  `Invoke-BuildHostForkMatrix.ps1`, `Invoke-BuildHostForkScenarios.ps1`,
  `Test-ProjectFileInfo.ps1`, `Test-BuildHostTrace.ps1` после адаптации к новому пакету.
  В matrix явно включить `fork`, `fork-a`, `fork-b`, `fork-ab`, `fork-abc`;
  default список сейчас не включает C. Полные RPC проверить явно через `-RpcOnly`
  и обычную production matrix; default baseline сам по себе недостаточен.
- Сохранить self-checks comparer. Разрешены только уже согласованные различия
  порядка glob excludes/removes и регистра путей на Windows; не расширять нормализацию,
  чтобы спрятать изменённые пути, references, diagnostics или содержимое.
- Broken/missing host должен дать видимый отказ. Early cancel, cancel, kill,
  reload и dispose — без зависания/частичной publication и оставленных собственных host.
  Сценарий старого стенда допускает одинаковую upstream early-cancel утечку:
  для исправленной новой версии это допущение заменить на явную проверку отсутствия утечки.
- Пройти [матрицу совместимости](README.md#матрица-совместимости), включая реальный
  net11/C# 15 проект, а не только `LanguageVersion` enum.
  Net11-only generator на runtime 10 учитывать отдельно; не обещать его поддержку
  до положительного теста и не менять analyzer policy ради теста.
- Focused минимум: BuildHostOptimizationsTests, UnregisteredBuildHostReaperTests,
  MissingSourceFileTests, RoslynIndexStorageTests, SDK selector/host/environment tests,
  workspace freshness/publication, analyzers/generators, plugins, snapshot/codec.
  Затем весь SourceStructure, Release build, main и lifecycle по общему контракту.
- B1 против B0; новый stock/fork/off/A+B+C — по правилам замеров и порогам плана.

## Приёмка и откат

Нужны независимое ревью diff переноса, положительные RPC/scenario/semantic сравнения,
нативная validation трёх платформ и отсутствие необъяснённых регрессий.
Изменения поведения между версиями compiler перечисляются по одному с тестом/обоснованием.

Если на runtime 10 не работает обязательный сценарий net11, записать точную зависимость;
этап не объявляется полной поддержкой .NET 11. До изменения порядка этапов сохраняется
последний рабочий checkpoint; нельзя исправить отчёт установкой лишнего runtime на тестовую машину.

Откатываются согласованно Roslyn, форк, Contracts, dependencies, guard и стенд.
Закрывать task-00b и менять статус в старом индексе можно только после её реальной проверки.

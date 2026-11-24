# Этап 1. Новый Roslyn и перенос форка BuildHost

- Рекомендуемые модели: gpt-6.2-astra.
- Рекомендуемый reasoning: xhigh.
- Примерная сложность: 5/5.
- Риск ошибки: высокий — несовместимые client/host могут дать неверный workspace без ошибки сборки.
- Необходимые способности: Roslyn/MSBuild, RPC, concurrency, differential testing, анализ lifecycle.
- Статус: accepted, 2026-11-20. Нативная проверка совмещённого кандидата B2 после слияния `main` прошла на трёх платформах.
- Depends on: [этап 0](task-00-baseline-and-dependencies-med.md).
- Модель, реализовавшая задачу: Codex, координатор и сабагенты.
- Модели, проводившие ревью: независимый сабагент Codex.
- Количество раундов ревью: 1 по переносу форка.
- Количество исправлений после ревью: 1, сброс записей MSBuild при выгрузке проектов.
- Отчёт о реализации, ревью и validation: [кандидат и ограничение SDK 11](evidence/task-01-roslyn-and-buildhost.md).
- Принятый scope и дата приёмки: 2026-11-20, Roslyn `5.12.0`, перенос форка BuildHost off/A/B/C и A+B+C, согласованность RPC и поставляемых host, проверки SQLite и отклонения несовместимых снимков. Совместимость и Release-тесты подтверждены на B2. [Проверки и ревью](evidence/task-01-roslyn-and-buildhost.md).

Общий контракт: [план перехода](README.md). Исходный порядок оставлял собственные TFM и язык сервера
`net10.0` / C# 14, если gate этапа 0 не выявил обязательную зависимость host от runtime 11.
Несовместимость feature band или версии MSBuild такой зависимостью не является.
19 ноября владелец согласовал перенос runtime раньше по результату продуктового прогона.
Сценарий SDK 11 требует .NET 11 для MSBuild внутри сервера. Промежуточный кандидат B1 на .NET 10
сохранён для сравнений. [Воспроизведение и решение](evidence/task-01-roslyn-and-buildhost.md).

Этап не начинается, пока gate этапа 0 не пройден. Он не сужает матрицу и не назначает
минимум потребительского SDK. Если владелец уже назвал сужение, этап исполняет
названную матрицу и дальше её не сужает. См. [нерешённый gate](UNRESOLVED-v2.md).

## Реализация

1. Обновить весь согласованный набор Roslyn и необходимые compile-time dependencies
   сервера/тестов. Не обновлять несвязанные библиотеки «заодно».
   Зависимость клиента от `Microsoft.Build.Framework` 18.7.1 не является поводом
   менять reference-пакет форка.
2. Взять точную ревизию исходников нового пакета. Обновить upstream copy,
   `UPSTREAM.md` с SHA-256, license/notices и plain SDK build projects.
   Наши deliberate patches хранить различимыми от исходного upstream.
   Код `forks/` следует стилю Roslyn; перед правками остального C# прочитать
   [code-style.md](../code-style.md) полностью.
3. Перенести tracing и A/B/C по одному, с проверкой каждой части.
   Сохранить правила извлечения служебных global properties до evaluation,
   диагностику/порядок сообщений, ошибки reference-first, отмену и завершение worker.
   Принятые четыре worker и GC настройки не перенастраивать.
4. Обновить Contracts, assembly/informational versions и guard под один пакет.
   Сохранить отказ при несовпадении и отсутствие молчаливого fallback на stock host.
   Net472 host берётся из того же нового пакета; netcore TFM/roll-forward повторяют
   подтверждённые требования upstream, а не TFM сервера.
   Пока upstream host остаётся на `17.11.48` и `net8.0`, эти значения сохраняются.
5. Проверить build/publish/pack Content replacement и transitive project-reference output.
   Обновить spike package references, version assertions, Rpc client/comparers
   и hardcoded output paths: сейчас `BuildHostForkCommon.ps1` содержит `net8.0`/net10.0.
   Этого списка недостаточно. Четыре `Reference` на `ref\net8.0` и
   `MsbuildReferenceVersion` берутся из inventory этапа 0. Пока upstream host
   остаётся на `17.11.48` и `net8.0`, эти пути не менять на пакет 18.7.1:
   у этого пакета нет `ref/net8.0`, и host такой ссылкой не собирается.
   Если выбранный проект host меняет версию или TFM, новый путь обязан существовать
   в пакете, а выход host по-прежнему не копирует MSBuild.
   На каждой стороне стенда должен быть ожидаемый host и один согласованный client.
6. Проверить Roslyn reflection/MEF hooks и SQLite assembly identity/public key/native library.
   Сохранить persistent index и проверку изменения sources между процессами.
   Проверить fingerprints и отказ от несовместимого snapshot/index без глобального удаления кешей.
7. Для reaper, missing-source retry и checksum fallback воспроизвести исходный дефект
   на старом пакете и проверить новый. Удалять только ставший ненужным путь после
   regression test, сохраняя остальные причины очистки процессов/санитизации.
   `UnregisteredBuildHostReaper` не начинает завершать произвольные `dotnet`,
   MSBuild Server и worker nodes.

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
  net11/C# 15 проект и библиотеку `netstandard2.0` с `LangVersion` `15.0`,
  а не только `LanguageVersion` enum. Библиотека — проект пользователя;
  она не поднимает язык исходников сервера на этом этапе.
  Net11-only generator на runtime 10 учитывать отдельно; не обещать его поддержку
  до положительного теста и не менять analyzer policy ради теста.
  Строки SDK не склеивать и не заменять прогоном на `10.0.402`.
  Результат `RawImportEvidenceFixture` не доказывает строки `10.0.1xx`, `10.0.300`
  и `10.0.4xx`: фикстура копирует корневой pin.
  Наблюдение MSBuild Server входит в матрицу: ноль BuildHost его не закрывает,
  opt-out дочернего процесса — только по правилу матрицы. Строку SDK 11,
  на которой использован build/test, нельзя принять без этого наблюдения.
- Повторить сценарий `net9.0` на новом штатном host и новом форке A+B+C с одним
  и тем же клиентом. В корне потребительского toolchain доступен только SDK `9.0.314`
  с runtime 9, а для уже собранного сервера отдельно доступен runtime 10. Подтвердить
  точный pin, фактический MSBuild и runtime дочернего процесса, сравнить RPC,
  диагностики и семантику, выполнить сборку через MCP. [Прогон штатного host на этапе 0](evidence/task-00-baseline-and-dependencies.md#net90-с-точным-sdk-90314).
- Focused минимум: BuildHostOptimizationsTests, UnregisteredBuildHostReaperTests,
  MissingSourceFileTests, RoslynIndexStorageTests, SDK selector/host/environment tests,
  workspace freshness/publication, analyzers/generators, plugins, snapshot/codec.
  Затем весь SourceStructure, Release build, main и lifecycle по общему контракту.
- B1 против B0; новый stock/fork/off/A+B+C — по правилам замеров и порогам плана.
  Серии снимаются в одном записанном режиме MSBuild Server.

## Приёмка и откат

Нужны независимое ревью diff переноса, положительные RPC/scenario/semantic сравнения,
нативная validation трёх платформ и отсутствие необъяснённых регрессий.
Изменения поведения между версиями compiler перечисляются по одному с тестом/обоснованием.
Приёмка не формулирует поддержку как «SDK 10.0.4xx и новее», если владелец так не записал.

Если на runtime 10 не работает обязательный сценарий net11, записать точную зависимость;
этап не объявляется полной поддержкой .NET 11. До изменения порядка этапов сохраняется
последний рабочий checkpoint; нельзя исправить отчёт установкой лишнего runtime на тестовую машину.
Отказ потребительской полосы SDK этим абзацем не лечится и к runtime 11 не приравнивается.

Откатываются согласованно Roslyn, форк, Contracts, dependencies, guard и стенд.
Закрывать task-00b и менять статус в старом индексе можно только после её реальной проверки.

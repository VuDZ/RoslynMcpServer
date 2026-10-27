# Source-only поиск и forward-compatible replay — 2026-10-22

Статус: **accepted in the requested scope, 2026-10-22**. Версия сервера 1.5.9.
Scope — прямой запрос владельца после офисного прогона 1.5.8 от 21:46:17:
проверить эквивалентность `FindSourceDeclarationsAsync`, исправить чтение второго
binlog, собрать ZIP сервера и скрипта. Независимого ревью не было.
Сохранение серии запрошено позднее 2026-10-22; [checkpoint](commit-2026-10-22.md).

## Изменения и проверяемые границы

- В общем resolver одна замена API: `FindDeclarationsAsync` →
  `FindSourceDeclarationsAsync`. Порядок обхода проектов, filter, ignoreCase,
  нормализация FQN, source-only filter, deduplication и проектные тайминги сохранены.
  После API, который также искал metadata/references, результат уже отбрасывал
  всё без source locations; новое API устраняет этот лишний поиск.
- Эквивалентность сравнивается со старым API на отдельных свежих workspace:
  классы, регистр, перегрузки, FQN/global/скобки, properties, nested types,
  project reference, один linked-файл в двух проектах, metadata-only имя,
  отсутствие имени и реально выполненный source generator. Сравниваются assembly,
  отображение символа и все source locations. Политика поиска сгенерированных
  деклараций не расширяется относительно старого API.
- В production replay включён `AllowForwardCompatibility` и обязательная подписка
  `RecoverableReadError`. Любой такой код исключает **весь файл** из provenance
  contexts/items; Failed/Incomplete считается по числу файлов, а не числу ошибок.
  Частичные интервалы остаются диагностическими и явно incomplete.
- Capture log показывает FailureCodes и максимальную версию формата.
  Проектная сводка и скрипт показывают ordinal, format, project starts и failures
  каждого файла. Данные admission не объявляются полными при пропусках событий.
- Протокол MSBuild подтверждён
  [официальной документацией](https://github.com/dotnet/msbuild/blob/main/documentation/wiki/Binary-Log.md#forward-compatibility-reading).

## Validation

Focused: **221 passed**, включая весь SourceStructure и navigation.
13 тестов эквивалентности поиска; 4 regression tests production replay:
новый совместимый формат сохраняет contexts/items, пустой новый BuildHost log
не портит capture, несколько recoverable errors одного файла дают Failed,
mixed good/bad — Incomplete, corrupt/missing/cancellation сохраняют контракт.
Оригинальный strict reader в тесте воспроизводит NotSupportedException.

Дополнительно production `ReplayAll` выполнен на реальных сохранённых PowerToys
binlog из `artifacts/parallel-load/task-05-evidence/powertoys-restored/profile-binlogs/`:
`capture-1.binlog` 8 187 531 bytes, `capture-2.binlog` 660 bytes. Результат:
**Complete, 5729 contexts, 2188 analyzer items, failures=[], max format 27**.
Сами исходные файлы не менялись.

- Release build всего решения: PASS.
- Main CI `Category!=AnalyzerLifecycle`, Release: **1426 passed / 0 failed / 0 skipped**.
  Receipt: `artifacts/source-search-20261022/main-ci-receipt.json`.
- Весь SourceStructure в focused и main CI: **123 passed**, inventories лимитов
  структурного анализа не менялись.
- AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`: **13 passed / 0 failed / 0 skipped**, 49 с.
  `artifacts/source-search-20261022/lifecycle.trx`, `lifecycle.log`.
- PowerShell parser/self-check: PASS. End-to-end Windows PowerShell 5.1:
  два свежих процесса, load + find + RAM hit, сервер 1.5.9 — PASS.
  Ответ поиска на Smoke fixture побайтно совпал с baseline 1.5.8 в обоих прогонах.
  Есть per-file format/project-starts и полные проектные тайминги.
  `artifacts/source-search-20261022/smoke/` и `smoke.log`.
- Реальный PowerToys replay receipt:
  `artifacts/source-search-20261022/powertoys-replay.json`: format 26 — 5729 starts,
  format 27 — 0 starts, оба без failures. Это пустой вспомогательный BuildHost log,
  который прежде делал capture неполным по одному лишь заголовку формата.

При разработке synthetic fixture сначала не выдавал AnyEventRaised; источник
исправлен, затем все regression tests прошли. Strict reader вызывается с явно
закрываемым stream: исключение на новом заголовке иначе удерживало тестовый файл.
В production stream уже принадлежит `using` и cleanup контракт не менялся.

## Пакет для нового замера

`artifacts/RoslynMcpServer-1.5.9-win-x64.zip`, **78 400 890 bytes**, 383 entries.
SHA-256: `DC36AAAFB1A18C4EB7FE950CCDD573B485E37AC08DD78AD5689D591D219F90FF`.
Внутри self-contained win-x64 сервер и актуальный `Measure-WorkspaceLoad.ps1`.
Проверены версии EXE/DLL **1.5.9.0**, хеши обоих бинарников и скрипта в архиве,
наличие обоих BuildHost и отсутствие `logs/`/локального JSONC.

До упаковки выполнен ещё один end-to-end именно опубликованным сервером и
приложенным скриптом: два свежих load/find, RAM hit, Complete replay и побайтно
одинаковый find response относительно 1.5.8 Smoke baseline — PASS.
Receipt: `artifacts/source-search-20261022/package-receipt.json`;
выводы: `package-smoke/`, `package-smoke.log`.
`dotnet publish` успешен; предупреждения NU1900 (сетевая недоступность audit)
и два прежних CS8603 не скрывались и не подавлялись.

Свежая полная загрузка PowerToys с opt-in overlay не выполнялась: подтверждены
production replay реальных сохранённых binlog и lifecycle fixtures, а не новое
поведение этого внешнего решения целиком. Это остаётся ручным пунктом task-06.
Полная приёмка эпохи и production activation параллельного загрузчика не заявляются.

## Неизменённые readers

`DtbEventCounter`, `RawImportEvidenceCapture` и `DependencyEvidenceRunner` в тестах
по-прежнему создают strict `BinaryLogReplayEventSource`. На формате новее их
зарегистрированного SDK они могут дать тот же NotSupportedException. Этот scope
не меняет экспериментальные readers и не расширяет выводы их старых evidence.

## Офисная проверка 1.5.9 — 2026-10-22

Сравнены `artifacts/<офисное решение>-20261022-214617` (1.5.8) и
`artifacts/<офисное решение>-20261022-221522` (1.5.9), та же машина,
SDK/configuration/platform/TFM и запрос MainViewModel. Медианы трёх fresh-process
измерений, warmup исключён:

- Первый поиск: **36 203,0009 → 34 175,3963 мс**, **−2 027,6046 мс / −5,60%**.
  Диапазоны: 36,096–38,054 с против 31,932–34,198 с; новый поиск быстрее во всех
  трёх измерениях. Это небольшой набор наблюдений, не изолированный A/B одного API.
- Load: **48 641,6144 → 49 078,8184 мс**, **+437,2040 мс / +0,90%**.
- Load + find, медиана сумм внутри прогона: **84 976,4556 → 83 160,9484 мс**,
  **−1 815,5072 мс / −2,14%**.
- Все четыре find response, включая warmup, **побайтно одинаковы** между версиями:
  те же четыре объявления, FQN, файлы, строки, столбцы и порядок.
  SHA-256 каждого: `9b4668444194460742ebfab640348ffbc92106b7658dfe77e72fc16df742f837`.
  Число промежуточных кандидатов уменьшилось с 10 до 4.
- Во всех четырёх новых запусках capture **Complete**, failures пусты:
  binlog #0 format 26 / 2833 project starts; binlog #1 format 27 /
  **49 project starts**. Второй офисный файл содержательный, в отличие от пустого
  дополнительного binlog сохранённого PowerToys control.
- Теперь 2882 provenance contexts, 804 analyzer items, **285 confirmed bindings**;
  раньше 2833 / 790 / 0 при Incomplete. В design-time сводке 142 проекта и 122
  logged evaluations, раньше 139 и 102. При этом root evaluations могут оставаться
  вне логируемых интервалов: Complete replay не означает полный профиль всех стадий.

Оптимизация поиска умеренная. Есть перераспределение стоимости: медиана вызова для
`private_project` выросла с 8,84 до 12,88 с, тогда как для `private_project_4` упала
с 5,23 до 1,04 с. Эти строки включают ленивую работу с зависимостями и не являются
чистой стоимостью собственных файлов проекта. Эквивалентность подтверждена для
измеренного запроса MainViewModel; другие запросы офисного решения не проверялись.

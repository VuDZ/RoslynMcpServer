# D/E0/task-06 — Provenance capture: forward-compatible replay binlog

- Рекомендуемые модели: Claude Opus 5.5; альтернатива: GPT-6.1 Sol.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — capture fail-closed; если recoverable read error посчитать успехом, `Complete` появится у неполного replay и разрешит opt-in overlay по неполным данным.
- Необходимые способности: `семантика_roslyn_msbuild`, `проектирование_проверок`.
- Статус: **implemented, automated validation accepted, 2026-10-22** — production replay исправлен
  вместе с переходом поиска на source-only API. Реальные сохранённые два binlog PowerToys
  (форматы до 27) читаются с `Complete`, 5729 contexts / 2188 analyzer items;
  main CI 1426/0/0, SourceStructure 123, AnalyzerLifecycle 13/0/0 и end-to-end PASS.
  Свежая полная загрузка PowerToys с opt-in overlay остаётся manual pending;
  [отчёт и границы приёмки](evidence/task-06-provenance-forward-compatible-replay.md).
- Закрытие, 2026-10-27: **accepted в объёме automated validation** (решение владельца). Ручная полная загрузка
  PowerToys с opt-in overlay отменена: PowerToys исключён из корпуса замеров решением владельца
  ([task-00 форка](../research-buildhost-fork/README.md#задачи)).
- Depends on: —.

Это production bugfix, найденный в [ревью task-05](evidence/task-05-profile-guided-partition.md#ревью-2026-10-10-claude-opus-55),
а не оптимизация D. В E0 он лежит, потому что найден здесь.

## Проблема

`AnalyzerProvenanceCaptureService.ReplayOne` читает каждый binlog загрузки через
`new BinaryLogReplayEventSource()` без `AllowForwardCompatibility`. Reader — это
MSBuild зарегистрированного SDK (на машине замера 10.0.401, формат до 26).

При загрузке решения, где стартует BuildHost-net472 (vcxproj, не-SDK проект),
этот BuildHost берёт MSBuild из Visual Studio и пишет свой binlog. Если VS новее SDK,
формат новее reader: VS 2026 18.10 пишет **27**. `Replay` бросает

```text
NotSupportedException: The log file format version is 27 with minimum required reader version 18, whereas this version of MSBuild only supports versions up to 26.
```

`ReplayAll` превращает это в `replay_failed:1:NotSupportedException`, и статус
всей загрузки становится `Incomplete`. `BuildSnapshot` делает все привязки
`CaptureIncomplete`, а `AnalyzerProvenanceCaptureGate` отвечает
`provenance-capture-incomplete`: `load_workspace(shadowCopyInSolutionAnalyzers=true)`
на таком решении отказывает. Обычная загрузка не затронута.

Наблюдение (прокси PowerToys, сервер 1.5.6, все прогоны task-00):
`Analyzer provenance capture finished: Status="Incomplete", Files=2, ... ConfirmedBindings=0`.
Второй файл — 660 байт, формат 27. Прочитанный с forward compatibility, он
содержит 6 событий (`BuildStarted`, три `BuildMessage`, `LoggersRegistered`,
`BuildFinished`) и ни одного проекта. Коды отказа в строку лога не попадают,
поэтому причину по логу не видно.

## Что сделать

Перед первой C# правкой — [AGENTS.md](../../../AGENTS.md) и полный [code style](../../code-style.md).

1. В `ReplayOne` включить `AllowForwardCompatibility = true` и подписаться на
   `RecoverableReadError` (без подписки reader бросает `InvalidOperationException`:
   `Subscription to RecoverableReadError is mandatory during forward compatible reading.`).
2. **Fail-closed сохраняется:** любая recoverable read error в файле делает этот
   файл неуспешным, как сейчас любое исключение. Код отказа различает причину:
   например `replay_recoverable:{ordinal}:{ErrorType}:{RecordKind}` (`BinaryLogReaderErrorEventArgs`).
   Файл новее reader, прочитанный без recoverable errors, — успешный.
   `FormatVersionMismatchWarning` не является отказом.
3. В строку `Analyzer provenance capture finished` добавить коды отказа
   (`FailureCodes=…`, пусто при `Complete`) и максимальную версию формата
   среди файлов. Без путей и без текста сообщений MSBuild.
4. Другие читатели binlog в тестах (`DtbEventCounter`, `RawImportEvidenceCapture`,
   `DependencyEvidenceRunner`) этой задачей не меняются; в отчёте перечислить,
   какие из них упадут на новом формате тем же способом.

## Проверки поведения

Структурные проверки через текст исходника запрещены
([правило](../../../.cursor/rules/roslyn-mcp-structural-tests.mdc)); поведение
проверяется выполнением.

- **Файл новее reader.** Fixture строится в тесте: настоящий binlog текущего
  формата, у которого в распакованном заголовке версия формата заменена на
  `текущая + 1`, а minimum reader version оставлена. Без правки replay этот
  файл даёт `NotSupportedException` (тест сначала воспроизводит отказ на
  старом поведении или фиксирует его в отчёте), с правкой — `Complete`
  и те же contexts/items, что у исходного файла.
- **Recoverable error.** В fixture добавлена запись неизвестного вида (или
  обрезан известный record так, что reader сообщает recoverable error): статус
  файла — отказ с кодом `replay_recoverable:…`, итог загрузки `Incomplete`, если
  есть другой успешный файл, и `Failed`, если нет.
- Существующие seams `Missing` / `Corrupt` / `MixedValidAndCorrupt` дают прежние статусы.
- Строка лога содержит коды отказа при `Incomplete` и пустое поле при `Complete`.
- Если сконструировать recoverable error в fixture нельзя, это записывается
  с причиной; проверка `Complete` при recoverable error заменяется unit-проверкой
  отображения событий `RecoverableReadError` в статус через выполнение, не через текст.

## Ручная проверка

На PowerToys (Debug|x64, как в task-04) после publish: в логе
`Status="Complete", Files=2`, `ConfirmedBindings` больше нуля или объяснение,
почему 0 при `Complete`; `load_workspace(shadowCopyInSolutionAnalyzers=true)`
больше не отказывает по `provenance-capture-incomplete`.

## Выпуск

- Patch bump `RoslynMcpServer.csproj` (`Version`, `AssemblyVersion`, `FileVersion`);
  строка в `CHANGELOG.md`; версия в `AGENTS.md.sample` и в обзоре правил.
- Новый пункт в `.cursor/rules/roslyn-mcp-pitfalls.mdc`: binlog BuildHost-net472
  пишет MSBuild из VS, формат может быть новее reader SDK; replay — forward-compatible,
  recoverable read error — отказ файла, не успех.
- После правки: Release build, весь SourceStructure, полный `Category!=AnalyzerLifecycle`
  и **AnalyzerLifecycle** с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` — путь provenance
  входит в lifecycle. Известный SDK-specific сбой незакоммиченной работы
  workspace-load-cache называется поимённо и не выдаётся за зелёный прогон.
- Отчёт — `evidence/task-06-provenance-forward-compatible-replay.md`.

## Офисная проверка, 2026-10-22

Сервер 1.5.9, пакет 22:15:22: во всех четырёх запусках Complete, failures=[].
Binlog #0 format 26 / 2833 starts; binlog #1 format 27 / 49 starts.
Получены 2882 contexts, 804 analyzer items, 285 confirmed bindings.
Это дополнительное подтверждение исправления reader на содержательном втором
файле. Свежий PowerToys load с opt-in overlay остаётся manual pending;
офисная обычная загрузка не подменяет эту ручную проверку.
[Подробности](evidence/task-06-provenance-forward-compatible-replay.md#офисная-проверка-159--2026-10-22).

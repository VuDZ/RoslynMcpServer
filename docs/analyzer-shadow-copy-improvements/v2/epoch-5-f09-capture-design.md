# F-09 — захват design-time provenance

Дата: 2026-09-12. Статус: **эскиз принят** ([epoch-5-f09-acceptance.md](epoch-5-f09-acceptance.md));
P0-spike выполнен с вердиктом GO
([результаты](epoch-5-f09-p0-spike-results.md)). Matcher пока не изменён.

Связанные материалы:
[E5-S1](epoch-5-s1-results.md),
[эпоха 5](epoch-5-reference-provenance.md),
[U-ARB-01](UNRESOLVED-v2.md).

## Область решения

Эта работа проектирует только захват связи
`Analyzer item -> source project -> effective Configuration/Platform/inner TFM`
на design-time границе. Она не меняет выбор ссылок, подготовку shadow generation,
диагностику E5-S3 или публичный MCP API.

Не входят в F-09:

- изменение filename/unique-name matcher и реализация таблицы E5-S2;
- автоматический переход на Alt-2 или Alt-3;
- решение для inaccessible path;
- вычисление Configuration/TFM из сегментов пути или повторный разбор
  unevaluated `TargetFrameworks`;
- запуск build ради появления analyzer output.

## Выбранный канал

Основной production-кандидат — **binlog той же design-time загрузки
`MSBuildWorkspace`**, а не отдельная evaluation.

Roslyn 5.9.0 предоставляет overload-ы
`MSBuildWorkspace.OpenSolutionAsync` / `OpenProjectAsync` с
`Microsoft.Build.Framework.ILogger`. Проверка декомпилированной реализации
5.9.0 показала более узкий фактический контракт:

1. `MSBuildProjectLoader` принимает только logger с точным runtime type name
   `Microsoft.Build.Logging.BinaryLogger`;
2. его путь передаётся `BuildHostProcessManager`;
3. для следующих BuildHost создаются отдельные файлы с числовыми suffix;
4. обычный custom `ILogger` этим путём в BuildHost не переносится.

Следовательно, перед `Open*Async` создаётся load-scoped `BinaryLogger`. После
завершения `Open*Async`, но до публикации provenance snapshot, все созданные
binlog replay-ятся через публичный
`Microsoft.Build.Logging.BinaryLogReplayEventSource` той же зарегистрированной
версии MSBuild.

Нужный item читается на стороне consumer из task-output events:

- `ItemType == "Analyzer"`;
- `TaskParameterMessageKind == TaskOutput`;
- metadata содержит непустой `MSBuildSourceProjectFile`;
- build event context связывает output с фактическим consumer project instance.

Это перехватывает результат до того, как Roslyn преобразует analyzer в
`/analyzer:<path>` и создаст path-only `AnalyzerReference`.

На стадии эскиза канал считался реализуемым только на уровне API. P0-spike ниже
подтвердил, что binlog Roslyn BuildHost действительно сохраняет `Analyzer` task
output вместе с custom metadata на поддержанном SDK
([результаты](epoch-5-f09-p0-spike-results.md)); одного факта наличия overload
для этого вывода было бы недостаточно.

## Почему это не второй полный eval

`MSBuildWorkspace` уже выполняет design-time targets для создания compiler
arguments. Binlog подписывается на эти же выполнения. Replay читает события и
не вызывает evaluation, `ResolveProjectReferences`, `GetTargetPath` или build
повторно.

Цена канала не нулевая:

- BuildHost сериализует бинарные события на диск;
- сервер читает и индексирует binlog после load;
- до удаления временного файла binlog удерживает свойства и пути solution.

Это I/O и память порядка объёма событий, но не второй обход graph targets.
Для каждого физического load должны измеряться `capture bytes`, число файлов,
время replay, число project contexts и число полученных analyzer items.
Численный SLA этим эскизом не назначается.

## Входы snapshot

### Идентичность загрузки

Snapshot получает:

- нормализованный абсолютный `.sln` / `.slnx` / `.csproj` path;
- `_loadSessionId`;
- нормализованные requested global properties `Configuration`, `Platform`,
  `TargetFramework`;
- фактический MSBuild toolset: путь и версия зарегистрированного MSBuild,
  dotnet/SDK path и версия;
- фактически открытые inner `Project` из `workspace.CurrentSolution`.

`buildArgs` не входит в идентичность: это suffix последующих CLI build, а не
global property `MSBuildWorkspace`.

### Данные MSBuild event context

Для каждого project context сохраняются:

- абсолютный project file;
- effective global properties context-а, включая фактические
  `Configuration`, `Platform`, `TargetFramework`;
- `TargetPath` и `IntermediateAssembly`, если они присутствуют в том же
  design-time результате;
- parent/child context, достаточный для связи output `MSBuild` task с consumer.

Для каждого captured `Analyzer` item сохраняются:

- нормализованный item identity, но не вывод о происхождении из filename;
- `MSBuildSourceProjectFile`;
- `ReferenceSourceTarget`, `OutputItemType`, `ReferenceOutputAssembly`;
- `NearestTargetFramework`, `SetTargetFramework`;
- `SetConfiguration`, `SetPlatform`;
- `GlobalPropertiesToRemove`, `UndefineProperties` и дополнительные свойства
  project-reference edge, если они дошли до item/event;
- effective globals фактического nested source project context.

Requested parent Configuration не подставляется вместо отсутствующего effective
значения: E5-S1 показал edge, удаляющий `Configuration` и оставляющий nested
project в Debug при parent Release.

## Ключ item → загруженный inner Project

Захват строит immutable snapshot в два шага и публикует его только после
успешного открытия Roslyn solution.

### 1. Привязка consumer

Captured item связывается с загруженным consumer только при одновременном
выполнении условий:

1. нормализованный `Project.FilePath` точно равен project file event context-а;
2. в этом `Project` есть `AnalyzerReference.FullPath`, точно равный identity
   captured item с platform-aware сравнением путей;
3. если один project file представлен несколькими inner projects, effective
   context дополнительно и без противоречий связывается с одним Roslyn `Project`
   по captured `TargetPath`/`IntermediateAssembly` и публичным
   `Project.OutputFilePath`/`CompilationOutputInfo.AssemblyPath`.

Ноль или несколько кандидатов означает отсутствие привязки. Порядок проектов,
первый match, display name и filename не используются.

### 2. Привязка source

Source-кандидаты сначала ограничиваются точным равенством
`Project.FilePath == MSBuildSourceProjectFile`. Затем выбранный nested context
с effective Configuration/Platform/TargetFramework связывается с одним
фактически загруженным inner project.

Для подтверждения inner project используются captured output properties:

- `TargetPath` сравнивается только с `Project.OutputFilePath`;
- `IntermediateAssembly` сравнивается только с
  `Project.CompilationOutputInfo.AssemblyPath`;
- отсутствующее значение не синтезируется из `OutputPath`, `AssemblyName` или
  сегментов каталога;
- конфликт двух непустых точных сравнений делает связь неподтверждённой.

`NearestTargetFramework`/`SetTargetFramework` подтверждают выбранный TFM edge,
но не создают Roslyn `ProjectId` сами по себе. Если несколько loaded inner
projects остаются неразличимы, item остаётся unconfirmed.

Итоговый ключ:

```text
(loadSessionId, consumer ProjectId, normalized original AnalyzerReference.FullPath)
```

Значение содержит source `ProjectId`, source project file, effective globals,
selected inner TFM, captured item identity и основание точных join-ов.
Snapshot не хранит `AnalyzerReference` object identity и не переносится между
load sessions.

## Lifetime и пересчёт

Provenance snapshot создаётся на каждом **физическом** открытии workspace и
публикуется атомарно рядом с raw solution/load session:

- первый `load_workspace`;
- reopen после `_projectGraphStale`;
- смена path, `Configuration`, `Platform` или `TargetFramework`, уже меняющая
  cache key;
- load после `reset_workspace`.

Graph-stale включает наблюдаемые `.csproj`, `.sln`, `.slnx`,
`Directory.Build.props`, `Directory.Build.targets`,
`Directory.Packages.props` и `global.json`. Изменение произвольного импортированного
`.props/.targets` вне наблюдаемого набора требует явного `reset_workspace`/load;
эскиз не обещает универсальный MSBuild import watcher.

Cached load с тем же ключом и актуальным graph повторно использует snapshot.
Обычный `.cs` edit, watcher flush, reconciliation, semantic query и artifact
refresh snapshot не пересчитывают.

Захват выполняется при каждом физическом open независимо от текущего значения
`shadowCopyInSolutionAnalyzers`. Это позволяет последующему cached
`false -> true` использовать тот же load snapshot без скрытой evaluation и не
решает отдельно sticky-семантику U-ARB-05.

`ClearWorkspaceAsync` удаляет ссылку на snapshot. Временные binlog удаляются
после replay в `finally`; они не входят в shadow cache и не сохраняются для
диагностики по умолчанию.

## Отказы и fail-closed правила

Workspace load и provenance capture имеют разные результаты. Ошибка capture не
выдаётся за ошибку Roslyn load, но snapshot получает явный incomplete/failed
status. После будущего rollout такая ссылка не может быть переписана по
provenance.

- `Open*Async` завершился ошибкой: snapshot не публикуется, load следует
  существующему failure contract.
- Нет `MSBuildSourceProjectFile`: `unconfirmed`; это не `foreign`.
- Source project file не загружен: confirmed item origin, но нет допустимого
  loaded source; rewrite запрещён.
- Нет effective TFM при нескольких inner candidates: ambiguous/unconfirmed.
- Несогласованные edge metadata и nested global properties: unconfirmed.
- Binlog отсутствует, повреждён, несовместим по версии или replay отменён:
  snapshot failed; не включать частично разобранный файл.
- Один binlog разобран, другой нет: snapshot incomplete; результаты успешных
  файлов можно хранить для диагностики, но rollout не должен делать
  положительный выбор для consumer из failed context.
- Original path inaccessible: этот факт хранится отдельно; F-09 не выбирает
  действие и не наследует missing policy.
- Source output отсутствует: provenance может быть подтверждён, но подготовка
  запрещена как `source_output_missing`.

Пустая metadata не запускает unique-name fallback. Explicit analyzer без
`MSBuildSourceProjectFile` не объявляется foreign только по отсутствию metadata.
Alt-2, Alt-3 и inaccessible остаются ровно в состоянии U-ARB-01.

Binlog может содержать environment/global properties и секретные значения,
пришедшие из MSBuild. Каталог должен быть process-private, имя — случайным,
содержимое не логируется и удаляется best-effort при success, failure и
cancellation. Публичный load response не получает новые структурированные поля.

## Оценка альтернатив

### `ProjectInstance` / `ProjectGraph`

`ProjectGraph` даёт nodes с effective global properties, а source
`ProjectInstance` — evaluated `ProjectReference`. Но pure graph construction не
создаёт project-produced `@(Analyzer)`: всё равно нужно выполнить
`ResolveProjectReferences`/nested `GetTargetPath`.

Это второй evaluation/target pass, требующий точно воспроизвести solution
configuration mapping, toolset и inner-TFM negotiation Roslyn load. Он также
добавляет in-process MSBuild lifetime/изоляцию рядом с BuildHost. Поэтому этот
вариант не выбран production-каналом F-09.

### Второй `dotnet msbuild`

Отдельный процесс проще как диагностический oracle и уже доказал наличие metadata.
Измерение E5-S1 на маленькой fixture: в среднем 911.6 ms для design-time
`ResolveProjectReferences`, включая process startup.

Такой prototype допустим для сравнения captured items, но не является
production fallback: он повторяет evaluation, требует передачи solution mapping
и масштабируется по graph. Его нельзя незаметно включить на каждый load.

## P0-spike и отдельная приёмка

До любого E5-S2 кода нужен узкий capture spike без matcher:

1. Вызвать `OpenSolutionAsync` и `OpenProjectAsync` с `BinaryLogger` на fixtures
   E5-S1.
2. Доказать наличие task-output `Analyzer` с
   `MSBuildSourceProjectFile`, exact consumer context и metadata выбранного TFM.
3. Для Debug/Release и двух inner TFM получить разные effective contexts и
   привязать каждый item ровно к одному loaded `ProjectId`.
4. На foreign same-name получить project-produced captured item и не объявить
   explicit item без metadata foreign/confirmed.
5. Доказать отсутствие compilation/build side effects по неизменным output
   timestamps/hash.
6. Измерить размер binlog и дополнительное wall time capture/replay на fixture и
   реальном multi-project solution.
7. Проверить lifecycle: один capture на physical load, ноль на cached load,
   `.cs` edit, semantic query и artifact refresh; новый capture после graph-stale
   и смены load globals.
8. Проверить corrupt/missing binlog, cancellation, partial files и удаление
   временных данных.

Приёмка фиксирует поддержанные Roslyn/MSBuild версии и observed event shape.
Если BuildHost binlog не содержит item metadata либо exact join к loaded inner
projects не получается без path semantics/эвристики, этот эскиз не принимается,
matcher не меняется. Нельзя молча перейти на `ProjectInstance`, второй
`dotnet msbuild`, Alt-2 или Alt-3. Владелец требования отдельно фиксирует
следующий выбор в U-ARB-01; при отказе от capture это должен быть Alt-2 либо
Alt-3 до изменения тестов или matcher.
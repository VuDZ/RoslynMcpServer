# E0/task-03 — oracle fixtures

Дата: 2026-10-04. Статус: **accepted — isolated fixtures, negative expectations only; R03-01..R03-06 закрыты**.
Исполнитель — сессия выполнения task-03 (Luna/medium); координатору принадлежат final
validation и приёмка. Production C#, spec/contract, packets и версии не менялись.
Текущий итог — [финальная coordinator validation и приёмка](#финальная-coordinator-validation-и-приёмка-2026-10-04).
Предыдущие review/validation-секции ниже сохраняют историю промежуточных результатов;
их verdicts заменены финальным решением только в пределах принятого scope.

## Результат и граница вывода

Реализованы 13 фикстур по конечному manifest [packet-03](../design/implementation-packet-task-03.md)
и builder-тесты обязательных проверок (confinement, точные байты, восстановление после
mutation, независимый fresh-MSBuild marker check). Все ожидания admission честно
отрицательные: `unknown` с наблюдёнными reason-категориями task-02. Положительная ветка
admission/equivalence не доказана и в фикстуры не вписана — hold из [task-02
findings](task-02-dependency-admission.md) сохраняется: workload-manifest Target вне
каталога SDK, analyzer inventory 8/6 и незакрытые restore/toolset/analyzer-dependency
категории делают каждый текущий request `unknown`. Приёмка этой реализации не открывает
positive fixtures, task-04 equivalence или public activation.

Golden-значения — только независимо наблюдённые данные task-02, literal в builder:

- Полный текст сгенерированного Razor-документа (включая `#pragma checksum` содержимого
  `.razor` = `415790B2…6DE01` и `#line`-диапазоны) — из `baseline.observation.json`
  witness-артефактов task-02. Абсолютные пути fixture-root в golden заменены
  документированным placeholder `{fixtureRoot}`, который builder подставляет при создании
  корня; байты `.razor` сохранены идентичными (`<h1>Witness</h1>\n@code { … }`), поэтому
  checksum воспроизводится. Проверено fresh MSBuildWorkspace в текущем прогоне: текст
  совпал полностью.
- Байты `obj/Debug/net10.0/App.AssemblyInfo.cs`,
  `.NETCoreApp,Version=v10.0.AssemblyAttributes.cs` и
  `App.GeneratedMSBuildEditorConfig.editorconfig` — из standard-фикстуры task-02;
  editorconfig содержит `build_property.ProjectDir = {fixtureRoot}\App\`.
- CS0219 → Error через `.editorconfig` и CS0542 (конфликт member/type `Marker`) —
  наблюдённые ожидаемые диагностики фикстуры, не признак прохода host build:
  ordinary build обязан падать на CS0542.

## Сценарии и ожидания

Каждый корень: `global.json` SDK 10.0.300 rollForward disable, `NuGet.config` с пустыми
feeds; проекты по умолчанию net10.0/Debug/nullable/LangVersion 14. Запрос —
`ExperimentRequest(project, "Debug", "AnyCPU", null, roots, "full", false)`.

| Сценарий | Layout | Disposition / reasons | Ключевые ожидания |
|---|---|---|---|
| ProjectGraph (V01) | Lib+App, reference | unknown + core | 2 instance keys, 1 edge, `Lib.Value.Number=42` в обоих контекстах |
| Encodings (V02) | Utf8Bom/Utf8/Utf16 + `.editorconfig` | unknown + core | literal преамбулы EF BB BF / без BOM / FF FE, `Привет Ω`, chars/bytes round-trip, CS0219 |
| MultiTarget (V03) | TargetFrameworks net9;net10, conditional defines | unknown + core | точные 2 instance keys, `App.Flavor.Value`=9/10 |
| RazorGenerator (V04) | byte-точная фикстура task-02 | unknown + core | полный generated golden (1 документ, `…RazorSourceGenerator\|Marker_razor.g.cs`), `E0Razor.Marker.Marker=E0RazorMarker`, CS0219+CS0542 |
| GeneratedObj (V05) | App/Value.cs | unknown + core | 3 наблюдённых obj-входа с точными байтами как membership-ключи |
| LinkedImport (V06) | A→B, оба Link `../shared/Shared.cs`, common.props без Target | unknown + core | 2 instance keys, 1 edge, одна физическая membership в двух контекстах, `Shared.Link.Number=42` |
| AbsentImport (V12) | Import Local.props + Exists, файл отсутствует | unknown + core | known-absent путь входит в layout; появление — документированная mutation |
| CustomTarget | Target перед CoreCompile + target-input.txt | unknown + core (custom-target) | исполнение target отделено от admission |
| ExplicitCompile (V07) | default items off + явный Include | unknown + core | capability-layout; новых admission правил нет |
| WebAssets | Web SDK, wwwroot/index.txt, `.well-known/e0.txt` | unknown + core | web-asset region члены в layout |
| ExternalAsset | linked Content `../external/asset.txt` | unknown + core + `external-static-asset` | весь request отвергнут |
| MissingReference | ProjectReference на отсутствующий проект | unknown + core | один instance, ноль edges |
| EmptyProject | GenerateAssemblyInfo/TFMAttr off | unknown + core | ноль пользовательских compile-входов; это не zero-project success |

Core reasons (все сценарии, из runner-кода task-02 и findings):
`analyzer-dependency-closure-unproven`, `custom-target`,
`restore-resolution-closure-unproven`, `toolset-identity-input-set-unproven`.
Reason-категория считается покрытой reason, равным ей или с префиксом `…:` — соглашение
задокументировано в `FixtureExpectation`. `RequiredOptionValues` ограничены независимо
выводимым `NullableContextOptions=Enable`; canonical-порядок `PreprocessorSymbolNames`
не наблюдён независимо и в goldens не вписан (см. ограничения).

## Файлы

- `RoslynMcpServer.Tests/WorkspaceLoadCache/Models/FixtureScenario.cs`,
  `Models/FixtureExpectation.cs` — точные объявления packet.
- `OracleFixtureCase.cs` — owned root: `FilePath` confinement (ArgumentException на
  rooted/escape), `RestoreAsync` (восстановление байтов, удаление scenario-файлов и
  bin/obj внутри Root, parent не трогается), `VerifyRestoredAsync` (точные байты +
  неожиданные пути; bin/obj игнорируются только как документированный build-stage output),
  `DisposeAsync` (удаление owned root; failure не подавляется).
- `OracleFixtureBuilder.cs` — materialization 13 сценариев; builder не build/restore/
  MSBuild/DLL/oracle; cleanup on failure; OCE пробрасывает token. Никаких production
  правок.
- `OracleFixtureBuilderTests.cs` — 48 тестов: confinement, точные байты диска против
  expectations, restore после полной mutation (все файлы + удаление + мусор + Local.props
  + bin/obj leftovers, включая вложенные друг в друга bin/obj), отрицательность
  expectations, encoding preambles/chars, GeneratedObj goldens, отчётность
  cleanup-failure (прямой тест хелпера и fault-тест catch-ветки после начала
  materialization), свежий MSBuild marker check с restore после build.
- Этот отчёт.

## Проверки и evidence

Повтор focused: `run_specific_test` className `OracleFixtureBuilderTests`, Release —
первый прогон **45 total / 42 passed / 3 failed** (mutation без каталога `App/` на
LinkedImport; не-ordinal порядок instance keys; потерянный `\`-разделитель после
`{fixtureRoot}` в razor golden — дефект извлечения шаблона). После исправлений —
**45/45 passed**, включая байт-точное совпадение fresh generated текста с golden,
константу `E0RazorMarker`, CS0219|Error и CS0542|Error в fresh workspace, ожидаемый
падающий ordinary build (`error CS0542:`) и полное восстановление корня после build.

Retained marker evidence (локальные private artifacts, не коммитятся):
`C:/Users/VuDZ/AppData/Local/Temp/roslyn-oracle-fixtures/d471047fda714488813fb6da7412b94f/oracle-fixture-RazorGenerator-d48f86547f634e68ae2eb8e450067b48-evidence/`
— `marker-restore.*`, `marker-build.*` (command.json + stdout/stderr). SHA-256:
`marker-restore-command.json` `C2E9BB9F…C4990`, `marker-restore.stdout.txt`
`79BA93FD…AABB`, `marker-build-command.json` `708970BB…BD66`, `marker-build.stdout.txt`
`F8C951B3…24A5`. Наличие после очистки temp не гарантируется.

**Первичная validation исполнителя до review (2026-10-04):**

- `run_dotnet_build` RoslynMcpServer.sln, Release, noIncremental — exit 0; только 4
  базовых предупреждения (CS8603 ×2, CS8601, xUnit1031), предупреждений новых файлов нет.
- `run_test_by_filter` `Category!=AnalyzerLifecycle`, Release, noBuild/noRestore —
  **1177/1177 passed, 0 failed** (1132 базовых + 45 новых; suite включает SourceStructure).
- AnalyzerLifecycle — **not-run / not-required**: analyzer/shadow-copy lifecycle,
  workspace load/prepare/publication и lifecycle test/build configuration не менялись.
- `git diff --check` — чисто.

## Ограничения и пробелы (возвращаются координатору/design owner)

- Positive admission/equivalence не доказаны и фикстурами не заявлены: до remediation
  workload-manifest custom-target, analyzer closure 8/6 и restore/toolset категорий
  disposition остаётся `unknown` у всех сценариев. Packet item 1 «Expected supported
  after dependency evidence» остаётся целью, а не текущим ожиданием.
- MultiTarget: net9 pack недоступен; positive exact mapping/defines/graph-edge
  comparison — not-run. Определяемый через defines исход закодирован константами 9/10;
  canonical `PreprocessorSymbolNames` (порядок `E0_…;TRACE;DEBUG;NET;…`) независимо не
  наблюдён и в `RequiredOptionValues` не вписан.
- CS0219→Error закодирован обязательной диагностикой (`RequiredDiagnosticIds`) и
  наблюдаемой severity, а не golden `SpecificDiagnosticOptions`: механизм трансляции
  editorconfig→compilation options независимо не наблюдён.
- Admission самой Razor-фикстуры через `InspectAsync` в task-02 не прогонялся (только
  fresh witness); ожидание RazorGenerator — та же honest-unknown модель, что у остальных.
- GeneratedObj golden bytes детерминированы только при pinned SDK 10.0.300 и локальных
  default properties; drift поймает task-04 сравнением.
- EmptyProject: сгенерированные роли не подтверждены task-02 полностью — ожидания пусты
  by design, «явно допущенный пустой проект» остаётся открытым.
- Scoped-CSS counterexample (scope-атрибут вне выбранных регионов) не закрыт: фикстура
  CSS-глобы не добавляет и случай не подменяет.
- Marker check — negative fixture witness (CS0542 обязателен), не positive fixture build;
  skip при недоступном pinned SDK/AspNetCore отражается как not-run, никогда не pass.

Текущий verdict: task-03 принят как isolated fixture infrastructure для task-04
с honest-unknown ожиданиями. Это закрывает fixture-зависимость task-03;
implementation/positive dispatch остаются hold до design/evidence remediation
task-02. Public activation не разрешена.

## Проверка результата (2026-10-04)

Reviewer: текущая сессия `/root`, которая не реализовывала проверяемые пять C#
файлов. Точный author session ID в исходном отчёте не записан (есть только
Luna/medium); полная проверка идентификаторов для формальной независимой приёмки
не заявляется. Verdict технического review: **CHANGES REQUIRED**. Принятого
scope пока нет; реализация остаётся review до устранения двух дефектов.

Независимо повторена coordinator validation на проверенном коде:

- `run_dotnet_build`, `RoslynMcpServer.sln`, Release, `noIncremental=true`:
  exit 0, те же четыре базовых предупреждения, новых нет.
- `run_test_by_filter`, `RoslynMcpServer.Tests.csproj`, Release,
  `Category!=AnalyzerLifecycle`, `noBuild=true`, `noRestore=true`:
  **1177 total / 1177 passed / 0 failed / 0 skipped**, включая 45 fixture-тестов
  и fresh Razor marker check.
- AnalyzerLifecycle не запускался: его production paths, host и конфигурация
  не менялись.

Зелёная suite не покрывает следующие воспроизведённые случаи. Repro выполнены
в отдельном PowerShell процессе через reflection финальной Release test DLL;
production и C# файлы не редактировались. Для каждого repro использован свой
уникальный temp root; fixture/root удалён после проверки.

### R03-01 — P2: восстановление падает на вложенных bin/obj

`OracleFixtureCase.cs:135–148`: список всех каталогов материализуется до удаления,
затем `Directory.Delete(..., recursive: true)` удаляет родителя раньше потомка.
Repro: создать ProjectGraph через `CreateAsync`, добавить каталог
`App/bin/nested/obj`, вызвать `RestoreAsync`. Получен
`DirectoryNotFoundException` на `App/bin/nested/obj`, stack указывает строку 148.
Удаление `App/bin` уже удалило этот путь. Восстановление исходных байтов прерывается.

Нужно удалять только верхние output-корни либо обрабатывать вложенные каталоги
в корректном порядке. Добавить regression с вложенными bin/obj, изменёнными
исходными байтами и проверкой полного восстановления. Текущий mutation-тест
создаёт только соседние `App/bin` и `App/obj` и потому дефект пропускает.

### R03-02 — P2: builder подавляет failure cleanup

`OracleFixtureBuilder.cs:51–57,522–537`: при ошибке materialization вызывается
`TryDeleteRoot`, который подавляет `IOException` и `UnauthorizedAccessException`.
Это нарушает packet-03: «do not suppress cleanup failure». При cancellation
или I/O failure можно получить только исходную ошибку, оставив частично созданный
owned root без информации о неудавшемся удалении.

Repro helper: создать уникальный root, открыть `locked.txt` с `FileShare.None`,
вызвать приватный `TryDeleteRoot` через reflection. Метод вернулся без исключения;
`Directory.Exists(root)` остался `true`. После снятия блокировки root удалён.
Нужно сделать cleanup failure наблюдаемым с сохранением исходной причины;
при успешном cleanup cancellation должна сохранять предоставленный token.
Добавить fault-проверку отказа cleanup после начала materialization. Текущий
тест cancellation отменяет token до `CreateAsync`, то есть до создания root,
и вообще не исполняет проверяемый `catch`.

Отрицательные admission-ожидания, неизменённая Razor-фикстура с CS0542 и явно
описанные not-run/hold task-02 сами по себе не являются замечаниями к task-03.
После исправлений требуются affected tests и полная Release main suite на
финальном коде. Приёмка task-03 и открытие task-04 пока отложены.

## Исправление R03-01/R03-02 (2026-10-04, исполнитель task-03)

Оба замечания исправлены тем же исполнителем; новые тесты воспроизводят каждый
случай до исправления.

- **R03-01:** `DeleteGeneratedOutputDirectories` теперь пропускает каталоги, уже
  исчезнувшие вместе с удалённым внешним bin/obj (recheck `Directory.Exists` перед
  delete) — порядок материализации списка больше не может привести ко второму
  удалению. Регрессия `RestoreAsync_removes_bin_and_obj_nested_inside_each_other`:
  вложенные `App/bin/nested/obj` и `App/obj/deep/bin` **плюс изменённые исходные
  байты** `App/App.csproj`, полная проверка восстановления через
  `RestoreAsync`/`VerifyRestoredAsync`/snapshot. Вложенные bin/obj добавлены и в
  общую mutation-теорию всех 13 сценариев.
- **R03-02:** `TryDeleteRoot` больше не глотает `IOException`/
  `UnauthorizedAccessException`: возвращает failure вызывающему, а `CreateAsync`
  агрегирует его с исходным исключением (`AggregateException`, original первичен;
  при успешном cleanup cancellation по-прежнему пробрасывает OCE с исходным
  token). Тест `Failed_cleanup_of_the_error_path_is_reported_not_swallowed`
  (FileShare.None внутри root: failure возвращается, root остаётся видимым, после
  снятия блокировки cleanup успешен). Указанный ревьюером пробел «catch не
  исполняется после начала materialization» закрыт тестом
  `CreateAsync_reports_failed_cleanup_after_materialization_started`: отмена
  token после появления первого записанного файла (`App/App.csproj`) при
  заблокированном файле даёт `AggregateException(OCE с token, cleanup failure)`,
  root остаётся; тест устойчивость проверена тремя последовательными прогонами
  (в составе класса и два одиночных). Механика синхронизации — spin-ожидание
  первого файла с дедлайном 30 s; гонка исключена порядком записи builder-а
  (первый файл ordinal-первый, дальше минимум четыре awaited-записи до возврата).

Validation исполнителя после исправлений (до повторного review):

- `run_specific_test` `OracleFixtureBuilderTests`, Release — **48/48 passed**
  (45 прежних + 3 новых/усиленных).
- `run_dotnet_build` RoslynMcpServer.sln, Release, noIncremental — exit 0, те же
  4 базовых предупреждения, новых нет.
- `run_test_by_filter` `Category!=AnalyzerLifecycle`, Release, noBuild/noRestore —
  **1180/1180 passed, 0 failed** (1132 базовых + 48 fixture-тестов).
- AnalyzerLifecycle — not-run/not-required (lifecycle-пути не менялись).

Статус остаётся **review** по результатам повторной проверки ниже; приёмка
task-03 и dispatch task-04 отложены до R03-03/R03-04.

## Повторная проверка исправлений (2026-10-04)

Reviewer — та же текущая сессия `/root`. **R03-01 и R03-02 закрыты**:
вложенные output-каталоги, уже удалённые вместе с родителем, пропускаются;
cleanup failure возвращается и агрегируется с исходной причиной. Новые regression
и усиленная mutation-теория проверяют эти изменения. Принятие task-03 целиком
пока невозможно: **CHANGES REQUIRED**, принятого scope задачи нет.

Независимая validation на проверенном коде:

- Release solution build, `run_dotnet_build`, `noIncremental=true`: exit 0,
  те же четыре базовых предупреждения.
- Focused `run_specific_test`, `OracleFixtureBuilderTests`, Release,
  `noBuild=true`, `noRestore=true`: **48/48 passed**.
- Полная main suite, `run_test_by_filter`, `Category!=AnalyzerLifecycle`, Release,
  `noBuild=true`, `noRestore=true`: **1180 total / 1179 passed / 1 failed**.
  Упал `CreateAsync_reports_failed_cleanup_after_materialization_started`:
  `Assert.IsType() Failure`, expected `OperationCanceledException`, actual
  `TaskCanceledException`, `OracleFixtureBuilderTests.cs:210`.
- Дополнительные 20 отдельных запусков этого метода: **20/20 passed**.
  Это подтверждает отличие focused и полной suite, а не отменяет её failure.
- AnalyzerLifecycle не запускался и не требуется: production lifecycle paths,
  host и конфигурация не менялись. C# в рамках review не редактировался.

### R03-03 — P2: cancellation assertion отвергает допустимый subtype

`OracleFixtureBuilderTests.cs:210`: `Assert.IsType<OperationCanceledException>`
требует точный тип. При отмене во время awaited файловой записи исходная причина
может быть `TaskCanceledException`, производным от `OperationCanceledException`.
Builder корректно сохраняет эту причину в `AggregateException`, но тест отвергает
её и ломает main CI. Этот исход наблюдён в полной suite выше. Проверять нужно
принадлежность к OCE с сохранением проверки предоставленного token, а не точный
runtime type.

### R03-04 — P2: первый файл не является барьером materialization

`OracleFixtureBuilderTests.cs:200–207,328–342`: между `CreateAsync`, polling
первого файла, его блокировкой и cancellation нет handshake с builder. После
появления `App/App.csproj` builder может закончить все записи раньше теста.
Несколько `await` не гарантируют, что observing thread будет запланирован
до завершения операции. Тогда `creating` уже успешно завершён и ожидание
`AggregateException` неверно, а failure-path вообще не исполнен.

Независимый scheduling counterexample выполнен в отдельном PowerShell процессе
через reflection финальной Release test DLL: создать ProjectGraph с token;
приостановить observing thread на 50 мс сразу после вызова `CreateAsync`;
обнаружить root/первый файл; открыть первый файл с `FileShare.None`; отменить
token; получить результат `creating`. Наблюдение:
`Materialized first file visible: True`,
`Builder completed before observer locks file: True`,
`Actual completion after cancellation: success, no AggregateException`.
Это допустимый порядок планирования потоков и противоречит утверждению
исполнителя, что порядок awaited-записей исключает гонку. Полученная fixture
после снятия блокировки удалена через `DisposeAsync`.

Fault-тесту нужен детерминированный способ остановить materialization до
завершения и затем вызвать failure при заблокированном cleanup. Увеличение
задержек/числа записей или повторный зелёный прогон гонку не устраняет.
После исправлений повторить affected tests и полную Release main suite.
Task-03 остаётся review; task-04 по этой зависимости не открыт. Positive
admission/equivalence и public activation по-прежнему не доказаны/не разрешены.

## Исправление R03-03/R03-04 (2026-10-04, исполнитель task-03)

Оба замечания приняты; scheduling counterexample ревьюера воспроизводит
допустимый порядок, который polling-вариант не исключал.

- **R03-04:** polling первого файла заменён детерминированным handshake.
  `OracleFixtureBuilder` получил internal-перегрузку `CreateAsync(…,
  Action<string>? afterFileWritten)` поверх того же materialization-цикла
  (публичная сигнатура packet не изменилась; соглашение то же, что у internal
  helper-ов task-02). Колбэк выполняется синхронно после каждой записи файла,
  пока builder приостановлен; исключения колбэка попадают в тот же
  cleanup-on-failure путь. Тест
  `CreateAsync_reports_failed_cleanup_after_materialization_started` теперь
  отменяет token и берёт `FileShare.None`-лок на записанный файл внутри колбэка —
  builder физически не может продолжить или завершиться до выхода из колбэка,
  окно планировщика отсутствует по построению (spin/`Thread.Sleep`/`Stopwatch`
  удалены). Root остаётся видимым при агрегате; после снятия лока cleanup
  успешен.
- **R03-03:** точный `Assert.IsType<OperationCanceledException>` заменён на
  `Assert.IsAssignableFrom<OperationCanceledException>` с сохранением проверки
  `original.CancellationToken == cancellation.Token`: исходной причиной допустимы
  и `OperationCanceledException`, и производный `TaskCanceledException` в
  зависимости от того, какой await наблюдал token — что и наблюдала полная suite.

Validation на финальном коде:

- `run_specific_test` `OracleFixtureBuilderTests`, Release — **48/48 passed**
  (дважды: с pre-test build и no-build).
- `run_dotnet_build` RoslynMcpServer.sln, Release, noIncremental — exit 0, те же
  4 базовых предупреждения, новых нет.
- `run_test_by_filter` `Category!=AnalyzerLifecycle`, Release, noBuild/noRestore —
  **1180/1180 passed, 0 failed**: fault-тест детерминирован, прежний
  timing-зависимый исход в suite больше не воспроизводится по построению.
- AnalyzerLifecycle — not-run/not-required (lifecycle-пути не менялись).

Статус остаётся **review** по результату проверки ниже: функциональные замечания
закрыты, до завершения task остаются две правки обязательного стиля.

## Проверка детерминированного fault-теста (2026-10-04)

Reviewer — та же текущая сессия `/root`. **R03-03 и R03-04 закрыты**:
`Assert.IsAssignableFrom<OperationCanceledException>` сохраняет проверку token;
синхронный `afterFileWritten` исполняется после закрытия записанного файла
внутри materialization-цикла. Callback блокирует файл и отменяет token до
следующей итерации. Builder не может завершиться между наблюдением файла и
отменой; polling и его окно планирования удалены. R03-01/R03-02 остаются закрыты.
Принятый scope этой перепроверки — исправления всех четырёх функциональных
замечаний и поведение 13 фикстур с отрицательными admission-ожиданиями.
Полная задача остаётся review до двух исправлений обязательного стиля ниже.

Coordinator validation на проверенном коде:

- `run_dotnet_build`, `RoslynMcpServer.sln`, Release, `noIncremental=true`:
  exit 0, те же четыре базовых предупреждения, новых нет.
- `run_specific_test`, `OracleFixtureBuilderTests`, Release, `noBuild=true`,
  `noRestore=true`: **48 total / 48 passed / 0 failed / 0 skipped**.
- `run_test_by_filter`, `RoslynMcpServer.Tests.csproj`, Release,
  `Category!=AnalyzerLifecycle`, `noBuild=true`, `noRestore=true`:
  **1180 total / 1180 passed / 0 failed / 0 skipped**, включая SourceStructure
  и fresh Razor marker check. Прежний failure main suite устранён.
- AnalyzerLifecycle — not-run/not-required: production lifecycle paths, host
  и test/build configuration не менялись. Reviewer C# не редактировал.

### R03-05 — P3: CancellationToken должен быть последним

`OracleFixtureBuilder.cs:33–37`: новая internal-перегрузка принимает token
перед `afterFileWritten`. [Обязательный стиль](../../../code-style.md#formatting-and-naming)
требует «Put CancellationToken last». Поменять порядок двух параметров в
internal-перегрузке, её публичном wrapper и fault-тесте; публичная трёхпараметровая
сигнатура packet остаётся прежней. Это стилевое замечание; функциональной ошибки
в handshake не найдено.

### R03-06 — P3: internal helper размещён после private-методов

`OracleFixtureBuilder.cs:546`: `TryDeleteRoot` имеет internal-accessibility,
но расположен после private-методов. [Обязательный порядок членов](../../../code-style.md#class-member-order)
требует public → internal → private внутри группы methods. Перенести helper
перед `Compose` и остальными private-методами. Другие файлы/типы не перестраивать.

Технический verdict: функциональные R03-01..R03-04 **ACCEPT**, новых функциональных
замечаний нет; task-03 overall **review** до R03-05/R03-06. Это не разрешает
positive admission/equivalence или public activation. После правок C# требуются
affected tests и полная Release main suite на финальном коде по AGENTS.md.

## Финальная coordinator validation и приёмка (2026-10-04)

По запросу пользователя на commit координатор `/root` завершил две механические
стилевые правки: R03-05 — `afterFileWritten` перед token в internal-перегрузке
и её вызовах; R03-06 — `TryDeleteRoot` перед private-методами. Поведение,
публичная сигнатура packet и fixture goldens не изменены. Все R03-01..R03-06
закрыты; новых замечаний по принятому scope нет. Эти две правки — coordinator
integration, а не дополнительная независимая приёмочная сессия эпохи.

Проверки после последних C# изменений:

- `run_dotnet_build`, `RoslynMcpServer.sln`, Release, `noIncremental=true`:
  **exit 0**, прежние четыре базовых предупреждения, новых нет.
- `run_specific_test`, `OracleFixtureBuilderTests`, Release, `noBuild=true`,
  `noRestore=true`: **48 total / 48 passed / 0 failed / 0 skipped**.
- `run_test_by_filter`, `RoslynMcpServer.Tests.csproj`, Release,
  `Category!=AnalyzerLifecycle`, `noBuild=true`, `noRestore=true`:
  **1180 total / 1180 passed / 0 failed / 0 skipped**, включая SourceStructure
  и fresh Razor witness.
- AnalyzerLifecycle — **not-run/not-required**: production lifecycle paths,
  lifecycle host и test/build configuration не менялись.
- `git diff --check` и staged diff check — чисто.

**ACCEPT** для task-03 в конечном scope: пять новых fixture/model/test файлов,
13 disk layouts по manifest, отрицательные admission-ожидания, independently
observed goldens, восстановление байтов/owned outputs, детерминированный
cleanup-failure witness и task/index/evidence bookkeeping. В commit включаются
только эти пять C# файлов и три task-03 документа; посторонние локальные файлы
не входят в scope.

Positive admission/equivalence, отсутствующие net9 prerequisite и прочие
task-02 limitations остаются как описано выше. Task-04 не выполнялся; приёмка
task-03 не выдаёт неизвестные случаи за supported, не завершает эпоху и не
открывает public activation.

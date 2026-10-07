# E0/task-08 — S-001: public reconstruction реального контроля

Дата: **2026-10-08** (Europe/Moscow; запуск начат 2026-10-07).
Исполнитель: Codex (GPT-6; точный runtime model ID недоступен).
Статус задачи: **accepted, 2026-10-08**; round 2 закрыл T08-R1-001. Принятый scope — isolated supported base-query slice. [Ревью](task-08-review-round-2.md).
Экспериментальный verdict: **supported для наблюдённого base-query slice** по H-001/H-002 (B).
Это результат isolated capability, не приёмка E0, reusable cache или production admission.
Dependent positive implementation требует принятия этого результата.

## Контроль и воспроизведение

HEAD контроля: `5d4a86c00e84c859ddd008ef2d6a1671a9d9a7c8`.
Неизменённый `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj` и его project dependency
`RoslynMcpServer.csproj`; контроль выбран принятой [task-00](task-00-controls.md).
Исходники/csproj/refs/signing flags ради pass не редактировались.
SHA256 двух csproj и двух исходных plugin-файлов проверяется до/после scenario.
Это не closure/ABA proof всех файлов; S-002 и E1 остаются отдельными задачами.

Windows 10.0.26200, win-x64; .NET SDK 10.0.300, CLI runtime 10.0.8,
MSBuild 18.6.3+caa81fa49, Microsoft.CodeAnalysis 5.9.0.
Тестовый harness собирается в Release; **контроль ordinary load — Debug**, без
переданного словаря globals: request Configuration/Platform/TargetFramework отсутствуют.
Публичному MSBuildProjectLoader globals также не передаются. Это не утверждение,
что effective MSBuild property bag пуст: полный evaluation bag не извлекался.
`AssemblyConfigurationAttribute("Debug")` и
`TargetFrameworkAttribute(".NETCoreApp,Version=v10.0")` наблюдаются у обоих проектов.
По одному loaded instance на каждый project path, оба single-target net10.0;
направленная связь plugin → server однозначна. Multi-target scope не проверен.
Будущая schema должна различать request/loader/evaluated properties.

Исполняемый сценарий и checks:
[RealControlCapabilityTests](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/RealControlCapabilityTests.cs),
[reconstruction](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/RealControlReconstruction.cs),
[isolated operation gate](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/CapabilityOperationGate.cs),
[result model](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/Models/ReconstructedControl.cs).

Команды воспроизведения из корня репозитория:

```powershell
dotnet build RoslynMcpServer.sln -c Release --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --filter FullyQualifiedName~RealControlCapabilityTests
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --filter 'Category!=AnalyzerLifecycle'
python docs/workspace-state-cache/validate-artifacts.py
```

Тест повторно создаёт `TestResults/task-08/inspection.json` и `comparison.json`.
Зафиксированный inventory и comparison: [observation](task-08-observation.json).
Validation и точные результаты прогонов: [validation](task-08-validation.json).

## Что действительно восстановлено

Ordinary baseline получен через `MSBuildWorkspace.OpenProjectAsync`. Второй ordinary
load через публичный `MSBuildProjectLoader.LoadProjectInfoAsync` даёт `ProjectInfo`
и `DocumentInfo` **до** помещения в workspace. Между двумя ordinary loads сверяются
project/document paths, metadata properties, directed project targets и diagnostics.
Это два ordinary наблюдения, а не extraction скрытого state из первого workspace.
Production seam сейчас не публикует эти descriptors; интеграция раннего capture
остаётся будущему adapter, существующий private-reflection helper не использован.

Новый `AdhocWorkspace`/`Solution` получает новые ProjectId/DocumentId и заново
созданные descriptors, file references и options. IDs исходного host служат только
ключами same-process oracle, никуда не сериализуются. Затем `CSharpCompilation.Create`
создаёт base compilations по dependency order; hydrated `Project.GetCompilationAsync`
не вызывается. Analyzer/generator DLL execution в восстановленном host запрещён
throwing loader. Это finite base compiler query, не готовый generator-aware workspace.

- **Projects и memberships:** публичные ProjectInfo/Project, paths/name/language/
  assembly/output, default namespace/output ref/CompilationOutputInfo; полные
  document memberships и directed ProjectReference с aliases/EmbedInteropTypes.
  Plugin: 5 C# documents, 226 metadata refs, 1 project ref, 13 analyzer refs,
  2 analyzer configs. Server: 202 C# documents, 236 metadata refs, 13 analyzer refs,
  2 analyzer configs. AdditionalFiles — 0 у обоих; положительный additional-text
  case этим контролем не доказан. Всего 207 source documents, 462 metadata occurrences,
  26 analyzer occurrences; references не сокращены до трёх DLL из task-00.
- **Source state:** file path/name/folders/SourceCodeKind/IsGenerated из public
  loader descriptors; текст читается с диска. Проверяются characters, encoding
  code page/preamble и checksum algorithm/bytes каждого source/config document
  относительно ordinary. Исходный encoding/checksum также передаётся при чтении
  source для manual compilation. Ни source, ни generated text не сериализуются.
  Проверка поймала несовпадение checksum при стандартном FileTextLoader: fresh host
  выбирал другой алгоритм. Исправлено собственным file-backed TextLoader, который
  сохраняет только публичные encoding/algorithm и заново читает bytes. Проверка
  не ослаблена: characters, encoding и checksum обязаны совпадать.
- **Generated/ownership boundary:** все наблюдённые `DocumentInfo.IsGenerated=false`,
  включая файлы из obj. Это не доказательство UserInput или SDK producer identity.
  Suspicious generated paths остаются Unknown по существующей input-map политике;
  producer не угадывается из имени. Переносится только реальный descriptor flag.
  Source-generated documents ordinary compilation — пусто у обоих проектов.
  Generator-dependent controls этим positive не покрыты.
- **Parse options:** public CSharpParseOptions properties, включая specified
  language version, symbols и features; fresh constructor + public Equals.
- **Compilation options:** все публичные scalar options перенесены, включая
  nullable/diagnostic/optimization/platform/crypto properties. Простого constructor
  с теми же public properties оказалось недостаточно для Equals. Для observed Debug
  profile публичный `CSharpCommandLineParser` с `/debug+` воспроизводит parser-owned
  state, после чего применяются public properties и проверяется Equals. Это
  объявленный finite construction witness, не чтение private field и не обещание
  восстановления любого профиля. Несовпадение прекращает reconstruction.
- **Metadata:** file-backed PortableExecutableReference создаётся заново из каждого
  path + всех MetadataReferenceProperties. Assembly/module binding каждого файла
  сверяется с ordinary; project refs получают восстановленную dependency compilation.
  IdentityComparer — наблюдённый DesktopAssemblyIdentityComparer.Default;
  unsupported comparer/resolver types вызывают отказ.
- **Resolvers:** SourceFileResolver из public search paths/base/path map,
  XmlFileResolver из public base directory. Для внутреннего
  WorkspaceMetadataFileReferenceResolver используется публичная фабрика
  `CommandLineProject.CreateProjectInfo`. Equals включает host service identity:
  factory inputs проверяются в ordinary host, после чего другой resolver создаётся
  в новом host. Original resolver не передаётся в восстановленную compilation.
- **Analyzer config:** все configs сохраняются как file-backed documents;
  fresh Solution создаёт свой SyntaxTreeOptionsProvider. Только для scalar Equals
  oracle provider нормализуется к original; actual restored options используют
  новый provider. Полные compiler diagnostics совпадают; это не доказательство
  равенства произвольного стороннего analyzer execution.
- **Analyzer identities:** все public file paths сохранены отдельными
  AnalyzerFileReference. Shadow-copy preparation, binary closure и provenance
  admission не реализуются. Наличие descriptors не разрешает запуск DLL из DTO.
- **H-001:** external metadata DocumentationProvider явно заменён на Default;
  наблюдался непустой ordinary XML для Newtonsoft.Json.Linq.JToken и пустой restored
  XML. Source XML IRoslynMcpPlugin совпадает дословно. XML consumers исключены gate.
- **H-002:** у обеих compilations DesktopStrongNameProvider, key file/container
  отсутствуют, public key пуст, PublicSign=false, DelaySign=null, own public-key token
  null. Проверены effective options, assembly attributes и signed-friend declarations.
  Server имеет unsigned InternalsVisibleTo. Объявленная restoration policy — fresh
  DesktopStrongNameProvider с пустыми search paths только для выбранных запросов;
  точное равенство private provider state не заявляется. Подписанные внешние DLL
  сохраняются. Release-подпись на конвейере не приписывается Debug developer instance.

## Независимые expected results и exclusions

Заранее зафиксированные assertions проверяют и ordinary, и restored compilation:

1. SamplePlugin реализует `RoslynMcpServer.Plugins.IRoslynMcpPlugin` из server assembly.
2. Реальный вызов `JToken.FromObject` связывается с Newtonsoft.Json.Linq.JToken
   из Newtonsoft.Json, а LoadedWorkspace имеет McpServerToolAttribute.
3. Все compiler diagnostics (не только errors) совпадают; errors отсутствуют.
4. In-memory замена SamplePlugin.Name на `"capability-edited"` даёт эту constant value
   без compiler errors с обеих сторон. Это **не** production write/Apply/preflight
   test и не замена disk edit + cross-process task-06.

Isolated gate имеет отдельные controls для signing options, AssemblyKeyFile,
signed friend, unresolved attribute/unknown state, emit и documentation-dependent
operations. Hydrated action не выполняется; reason записывается **до** ordinary
callback. Без callback проверяется actual refusal. Лог содержит stage, project,
configuration, tfm, operation, reason, route. Для synthetic compilations configuration
и tfm честно `unknown`, а не выдуманные effective values. Например:

```text
stage=operation project=ExcludedControl configuration=unknown tfm=unknown operation=selected-symbols reason=signing-dependent route=ordinary-load
```

Callback witness доказывает выбор/порядок веток; это не запущенный production
whole-request fallback. Runtime policy и полноценные E1 log records ещё предстоят.
Gate не предназначен для допуска произвольных операций, analyzers или проектов.

## Validation и границы verdict

Результаты build, focused и main suite записаны в [validation JSON](task-08-validation.json).
Release build: exit 0, четыре предупреждения в неизменённых файлах (два CS8603,
один CS8601 и xUnit1031). Focused S-001: **13/13** после исправления checksum.
Один общий прогон окончательного кода дал **1277/1278**: существующий
`WorkspaceNonCSharpDiskSyncTests.Saved_additional_file_and_editorconfig_reach_the_published_snapshot`
ожидал ON, получил OFF. Повтор всего этого класса без изменений дал **4/4**.
Причина нестабильности не установлена; ошибка не скрыта и сохранена в validation JSON
вместе с результатом повторного полного main suite.
Повтор полного `Category!=AnalyzerLifecycle` против окончательного кода:
**1278/1278 passed, 0 failed, 0 skipped**. Код между падением и повтором не менялся.
Изменены только новые isolated tests и docs; production load/prepare/publication,
analyzer lifecycle host и общая test/build configuration не менялись. Поэтому
специализированный AnalyzerLifecycle не запускался. SourceStructure входит в main
suite; отдельный обязательный запуск после production правок неприменим.

Независимое ревью: round 2, 2026-10-08, Grok 4.7; T08-R1-001 closed / fixed; подтверждённых исправлений 1. [Отчёт](task-08-review-round-2.md).
Self-check и зелёные тесты не означают independent acceptance.
Benchmark/performance, переносимый DTO/codec, новый PID, persistent store,
disk validity/closure, production writes и public activation не проверялись.
Нет утверждения об ускорении или о production capture-ready.
Исходный real positive достигнут в указанном finite slice; требования вне него
не снимаются и остаются задачам E0/E1. H-001/H-002 не расширены.

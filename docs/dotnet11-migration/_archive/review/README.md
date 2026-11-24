# Review: falsification of dotnet11-migration

Каталог: замечания к спецификациям, не к тону документов.
Сверка: дерево на `109560b` (база плана `6f84e897`, сервер 1.5.25); `global.json`, `Services/Build/GlobalJsonSdkReader.cs`, `Services/Build/DotNetCliRunner.cs`, `Services/Workspace/UnregisteredBuildHostReaper.cs`, `RoslynMcpServer.Tests/WorkspaceLoadCache/PinnedSdk.cs`, `forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj`, `.github/workflows/release.yml`, `.github/workflows/test-suite.yml`, `docs/ARCHITECTURE.md`, AR-18. Релиз .NET 11 от 2026-11-10: [announcing](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/), [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [SDK/MSBuild/VS versioning](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs), [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), [breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11), [what's new in the SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk), [MSBuild Server](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-server), [C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15), [language versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning). NuGet: [Workspaces.MSBuild 5.12.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.12.0), [Microsoft.Build.Framework 18.7.1](https://www.nuget.org/packages/microsoft.build.framework/18.7.1). Срок поддержки и соответствие C# 15 `net11.0` с этими страницами сходятся; ниже только опровергнутые гарантии.

Цель ревью — опровергнуть предложенную архитектуру, а не улучшить её изложение.

| ID | Sev | Spec |
| --- | --- | --- |
| R-01 | Blocker | README.md, task-02 |
| R-02 | Blocker | README.md, task-00 |
| G-01 | High | README.md, task-03 |
| R-03 | Medium | README.md |

---

ID: R-01
Severity: Blocker
Category: Support profile

Target:
README.md / «Проверенная отправная точка», абзац про CI и корневой pin; task-02-runtime-and-language-hi.md / «Реализация», пункт 3

Claim:
В корне репозитория SDK pin отсутствует. Release создаёт локальный `global.json` с точной версией и `rollForward=disable`. На этапе 2 корневой `global.json` не добавляют: pin по-прежнему создаётся только в release checkout. При этом сервер на `net11.0` собирается SDK 11, а SDK 10 остаётся установленным для прежних сценариев.

Evidence:
`global.json` в корне отслеживается с `59f6936` (2026-11-15), это предок базы `6f84e897`. Содержимое: `version` `10.0.100`, `rollForward` `latestFeature`, `allowPrerelease` false. По [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) `latestFeature` выбирает наивысший установленный feature band той же major.minor и при отсутствии такого SDK завершается ошибкой, а не переходит на следующий major. Пока установлен любой SDK 10.0 не ниже 10.0.100, CLI не выберет SDK 11.

`GlobalJsonSdkReader.TryGetPinnedSdkVersion` читает только `sdk.version` (`Services/Build/GlobalJsonSdkReader.cs:26-31`). `TryResolveSdkDirectory` при существующей папке с этим номером возвращает её сразу (`:72-76`) и до политики не доходит. `GetRollForwardPrefix` (`:140-147`) для `10.0.100` даёт `10.0.100.`. Имена папок SDK (`10.0.402`, `11.0.100`) с этого префикса не начинаются. `rollForward` и `allowPrerelease` не читаются. `SdkMismatchDiagnostics` тогда сравнивает `dotnet --version` с буквальным номером из файла (`Diagnostics/SdkMismatchDiagnostics.cs:34-43`).

`docs/ARCHITECTURE.md:196` и AR-18 фиксируют тот же разрыв: разрешающий корневой pin отложен, потому что сервер и CLI выбирают разный MSBuild и сервер пишет ложный `MCP_MSBUILD_SDK_MISMATCH`. `release.yml:123-129` по-прежнему перезаписывает `global.json` только в checkout релиза (`10.0.402`, `rollForward=disable`) и прямо ссылается на этот разрыв. `test-suite.yml:74-77` ставит `10.0.x` без `global-json-file`; выбор CLI всё равно делает корневой файл.

Failure scenario:
1. Исполнитель следует README и пункту 3 этапа 2: корневой файл не трогает, в `release.yml` ставит pin SDK 11, в CI ставит SDK 11 и оставляет SDK 10.
2. `dotnet` в тестовом job читает корневой `10.0.100` + `latestFeature` и остаётся на полосе 10.0. Проект сервера с `net11.0` этим SDK не собирается.
3. Чтобы получить зелёную сборку, исполнитель переписывает тот же файл на `11.0.100` + `latestFeature`. На машине, где есть и названная папка, и более новый патч 11.0, CLI уходит на патч, а сервер закрепляет MSBuild из `11.0.100` и сообщает `MCP_MSBUILD_SDK_MISMATCH` на собственном репозитории. Это отказ, из-за которого корневой разрешающий pin был запрещён.

Suggested change:
В отправной точке назвать фактический корневой `global.json`. Этап 2 меняет его на точный SDK 11 с `rollForward: disable` — единственную политику, которую текущий reader выполняет одинаково с CLI, — либо сначала закрывает AR-18 и только потом разрешает нестрогий pin. Формулировка «pin отсутствует, корневой файл не добавлять» несовместима с переводом сервера на `net11.0` при сохранённом SDK 10.

Confidence:
High

---

ID: R-02
Severity: Blocker
Category: Support profile

Target:
README.md / «Матрица совместимости», первый пункт и запрет менять знак старого сценария; README.md / `RequiresPinnedSdk`; task-00-baseline-and-dependencies-med.md / «Результат и gate»

Claim:
«SDK 10» — один поддерживаемый случай: точный pin, только SDK 10, рядом с SDK 11. Фикстуры `RequiresPinnedSdk` остаются на точном `10.0.300`, их pin нельзя сменить ради зелёного прогона. Раньше положительный сценарий нельзя сделать отрицательным без явного изменения контракта. Единственный описанный выход, если новый MSBuild не стыкуется со старым порядком этапов, — host требует runtime 11.

Evidence:
План и страница [Workspaces.MSBuild 5.12.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.12.0) требуют `Microsoft.Build.Framework` >= 18.7.1. [Таблица SDK](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs): `10.0.1xx` — MSBuild 18.0, полоса поддерживается до ноября 2028; `10.0.3xx` — MSBuild 18.6, срок полосы закончился в августе 2026; `10.0.4xx` — MSBuild 18.9, поддерживается до ноября 2028; `11.0.1xx` — MSBuild 18.12. 18.0 и 18.6 ниже пола 18.7.1. 18.9 и 18.12 его покрывают.

`PinnedSdk.Version` — `10.0.300` (`RoslynMcpServer.Tests/WorkspaceLoadCache/PinnedSdk.cs:11-14`), `rollForward: disable`. README запрещает менять этот pin. Корневой `global.json` называет `10.0.100`: при установленной папке reader закрепляет именно её (`GlobalJsonSdkReader.cs:72-76`), то есть MSBuild 18.0, тогда как CLI с `latestFeature` может взять `10.0.4xx`.

Сборка форка ссылается на `ref\net8.0\` пакета MSBuild (`forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj:59-63`). У [Microsoft.Build.Framework 18.7.1](https://www.nuget.org/packages/microsoft.build.framework/18.7.1) опубликованные TFM — `net10.0`, `netstandard2.0`, `net472`. Этап 1 называет строки `net8.0` в `BuildHostForkCommon.ps1`, не этот `Reference`.

Выход этапа 0 сформулирован так: если host требует runtime 11, описать смену границ. Смена feature band внутри SDK 10 там не названа. Отключение старого SDK-сценария тем же абзацем запрещено (task-00, «Результат и gate»).

Failure scenario:
1. Исполнитель обновляет Roslyn до 5.12, оставляет строку матрицы «SDK 10» и фикстуры `10.0.300` как есть.
2. Прогон на `10.0.300` (MSBuild 18.6) или на всё ещё поддерживаемом `10.0.1xx` (MSBuild 18.0) не проходит пол 18.7.1. Ссылка `ref\net8.0` на пакет 18.7.1 не резолвится по опубликованным TFM.
3. Поднять pin фикстур план запрещает. Объявить прежний положительный SDK 10 отрицательным без смены контракта тоже запрещает. Поставить runtime 11 этот пол не закрывает: MSBuild 18.0/18.6 лежит в каталоге SDK 10. Прогон только на `10.0.402`, записанный как «SDK 10», закрывает строку матрицы, пока поддерживаемая полоса `10.0.1xx` сломана.

Suggested change:
Разделить «SDK 10» по feature band и версии MSBuild. Если пол 18.7.1 подтверждён, минимальная поддерживаемая полоса SDK 10 — `10.0.4xx` (MSBuild 18.9). Отказ от `10.0.1xx` и от точного `10.0.300` — явный допустимый исход этапа 0, а не скрытое отключение сценария и не «host требует runtime 11». Путь reference assemblies форка указать на TFM, который есть в новом пакете.

Confidence:
High

---

ID: G-01
Severity: High
Category: Lifecycle

Target:
README.md / «Матрица совместимости», абзац про отмену и оставшиеся BuildHost; task-03-packaging-and-acceptance-hi.md / «Native smoke», процессы после shutdown

Claim:
Чистое завершение на SDK 11 доказано тем, что после отмены, kill, shutdown и обрыва stdin не остаётся собственных процессов BuildHost. Другие процессы сборки в эту приёмку не входят.

Evidence:
С .NET 11 SDK MSBuild Server включён по умолчанию для `dotnet build` и `dotnet msbuild`. Он живёт между вызовами CLI и гаснет после 15 минут простоя. Выключение для одного запуска — `/nr:false`. Выключение переменной — `DOTNET_CLI_USE_MSBUILD_SERVER=false` или `MSBUILDUSESERVER=0`. Раньше CLI сам записывал `MSBUILDUSESERVER=0`, если `DOTNET_CLI_USE_MSBUILD_SERVER` не задан; в .NET 11 не задаёт. Источники: [what's new in the SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk) (разделы «MSBuild server enabled by default» и «MSBuild server and OpenTelemetry environment variables»), [MSBuild Server](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-server). В списке [breaking changes .NET 11](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11), на который этап 2 отправляет исполнителя, этого пункта нет.

`DotNetCliRunner.CreateProcessStartInfo` ставит `MSBUILDDISABLENODEREUSE=1` (`Services/Build/DotNetCliRunner.cs:343`) и не ставит `DOTNET_CLI_USE_MSBUILD_SERVER`, `MSBUILDUSESERVER` и не передаёт `/nr:false`. `Kill(entireProcessTree)` убивает дерево этого `dotnet`, не отдельный сервер сборки. `UnregisteredBuildHostReaper` не считает дочерний `dotnet build` / `dotnet test` своим BuildHost и не трогает его (`Services/Workspace/UnregisteredBuildHostReaper.cs:18-20`). Имя процесса сервера сборки в приёмке не названо. Команда остановки из документации — `dotnet build-server shutdown`.

Совпадает ли уже стоящий `MSBUILDDISABLENODEREUSE=1` с `/nr:false` именно для нового сервера, опубликованные страницы не сводят в одно правило: страница сервера называет ключ `/nr:false`, страница переменных — `MSBUILDDISABLENODEREUSE` как запрет оставлять старые node-reuse процессы. На SDK 11 это не прогонялось.

Failure scenario:
1. Исполнитель не меняет запуск `dotnet` и принимает этап 3 по тексту: после smoke нет процессов BuildHost.
2. `run_dotnet_build` / `run_dotnet_test` на SDK 11 поднимают MSBuild Server. Runner не передаёт документированный для этого сервера opt-out.
3. Проверка находит ноль BuildHost и ставит PASS. Тёплый сервер ещё до 15 минут держит выходы сборки. Следующий publish/build на той же машине получает блокировку файлов, а откат и «процессов не осталось» уже записаны как пройденные.

Suggested change:
В матрице и native smoke отдельно наблюдать MSBuild Server (`dotnet build-server shutdown` и отсутствие его процесса после build/test/cancel/shutdown). Пока прогон не показал, что `MSBUILDDISABLENODEREUSE=1` его гасит, runner должен передавать документированный opt-out .NET 11. Отсутствие BuildHost это не заменяет.

Confidence:
Medium

---

ID: R-03
Severity: Medium
Category: Support profile

Target:
README.md / «Матрица совместимости», пункт про репрезентативный проект C# 15

Claim:
Поддержка C# 15 доказана одним проектом `net11` / C# 15 с пятью конструкциями: union types, closed hierarchies, collection expression arguments, extension indexers, labeled break/continue. Memory safety в стабильную поддержку не входит.

Evidence:
[What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15) и [история версий](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history) перечисляют те же пять конструкций и ещё одну стабильную, без `preview`: non-virtual static interface members on older runtimes. Отдельного синтаксиса у неё нет. Она снимает `CS8701` у невиртуального статического члена интерфейса, когда у целевого runtime нет default interface implementation (`net472`, более старые TFM). [Ошибки версии языка](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/feature-version-errors) прямо называют это исключением: SDK с C# 15 и `LangVersion` 15 убирает ошибку без смены TFM. Проект `net11` эту диагностику не показывает: у `net11` runtime-поддержка интерфейсов уже есть. Исключение memory safety как preview совпадает с той же страницей и с [объявлением релиза](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/).

Строка матрицы про net472 — это WPF temporary projects и fallback, не компиляция этого члена. Прежние фикстуры C# 14 / `net10` по умолчанию не включают `LangVersion` 15.

Failure scenario:
1. Исполнитель собирает названный `net11`-проект, проходит parse, binding, rename и компиляцию пяти конструкций и закрывает языковую строку матрицы.
2. Проект `net472` или `netstandard2.0` с `LangVersion` 15 и статическим методом в интерфейсе в матрицу не входит.
3. Этап принят как поддержка C# 15. Ошибка `CS8701` на таком проекте остаётся, если язык для не-`net11` TFM не поднялся до поведения 5.12, и приёмка этого не видит.

Suggested change:
В языковую строку добавить целевой runtime без default interface implementation (`net472` или `netstandard2.0`) с `LangVersion` 15 и невиртуальным статическим членом интерфейса. Ожидание — отсутствие `CS8701` и успешные binding/navigation. Пять конструкций на `net11` эту проверку не покрывают.

Confidence:
High

---

## Для следующего агента

Каталог закрыт 2026-11-18. ID стабильны, новых карточек в этом файле нет: `R-01`, `R-02`, `G-01`, `R-03`.

Прочитаны и карточек не получили: `task-01-roslyn-and-buildhost-hi.md`, `evidence/preparation-2026-11-18.md`. Это не пропуск ревью.

Этот каталог и требования плана не править. Защита записана в `docs/dotnet11-migration/_archive/response/`: один ответ на каждый ID.
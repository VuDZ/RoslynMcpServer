# Response: защита/разбор review dotnet11-migration

Ответы на [review/README.md](../review/README.md), раунд 1, Grok 4.7, 2026-11-18.
Статус прохода: **completed, 2026-11-18**. Scope: по одному ответу на R-01, R-02, G-01 и R-03,
сводка требуемых изменений и [проверка результата](validation.md).
План, его этапы, отчёт подготовки и каталог review не менялись.

Опора (в этом порядке): запрос владельца о постепенном переходе со всеми проверками;
его запрет менять план/review в этом проходе; [предложение](../README.md) и четыре этапа;
[AGENTS.md](../../../../AGENTS.md), текущий код и ADR. Прочитаны все семь файлов входного пакета,
включая task-01 и отчёт подготовки, по которым карточек нет.

Рабочая база: `109560b` с уже сохранёнными пользователем изменениями раздела ревью
и каталогом review; это не чистый commit нового ревью.
ACCEPT означает согласие автора с необходимой будущей правкой, а не закрытие finding:
изменения требований и реализация остаются впереди.

Сводка: [summary.md](summary.md).

| ID | Sev | Verdict | Spec |
| --- | --- | --- | --- |
| R-01 | Blocker | ACCEPT | README.md, task-02 |
| R-02 | Blocker | PARTIALLY ACCEPT | README.md, task-00 |
| G-01 | High | PARTIALLY ACCEPT | README.md, task-03 |
| R-03 | Medium | ACCEPT | README.md |

---

ID: R-01
Verdict: ACCEPT

Критика верна: отправная точка ошибочно отрицает существующий корневой pin,
а предписание этапа 2 оставляет CLI на SDK 10 при переводе собственного проекта на net11.

Требования:
Владелец просит постепенный работоспособный переход, а не только повышение TFM.
[README / «Цель и порядок»](../README.md#цель-и-порядок) и
[task-02 / «Реализация», п. 3](../task-02-runtime-and-language-hi.md#реализация)
требуют согласовать SDK сервера, CI и release, сохранив пользовательские SDK 10 сценарии.

В [global.json](../../../../global.json) действительно стоят `10.0.100`,
`latestFeature`, `allowPrerelease: false`.
[Правила CLI](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
удерживают такой выбор внутри 10.0: наличие SDK 11 проблему не исправляет.
[GlobalJsonSdkReader](../../../../Services/Build/GlobalJsonSdkReader.cs), строки 26–31, 72–85 и 140–147,
читает номер и предпочитает буквальную папку, а не реализует политику CLI.
[SdkMismatchDiagnostics](../../../../Diagnostics/SdkMismatchDiagnostics.cs) затем сравнивает
результат с этим буквальным номером.
[AR-18](../../../architecture-review/findings.md#ar-18) подтверждает тот же механизм.
Текст ARCHITECTURE о якобы отсутствующем global.json здесь тоже устарел;
он не опровергает файл на диске.

Что менять:
В следующей ревизии README / «Проверенная отправная точка» назвать фактический pin.
В task-00 включить его в baseline выбора CLI/MSBuild, не приписывая текущему reader
поддержку latestFeature. В task-02 / «Реализация», п. 3, заменить предписание
о несуществующем файле обновлением существующего корневого global.json на точный
стабильный SDK 11 с `rollForward: disable` и `allowPrerelease: false`.
Точный номер подтверждается доступностью на всех трёх платформах.
CI устанавливает этот SDK и отдельно необходимые SDK 10 для consumer fixtures;
release pin согласуется с выбранным номером.

Проверка: при совместно установленных SDK 10, точном SDK 11 и более новом SDK 11
сборка репозитория, `dotnet --version`, MSBuild workspace и build/test через MCP
выбирают один ожидаемый toolchain без ложного MCP_MSBUILD_SDK_MISMATCH.
Без точного SDK — явный воспроизводимый отказ, не скрытый переход на другой.
Полное исправление AR-18 не обязательно для этого минимального решения
и не объявляется выполненным благодаря строгому pin.

Последствия:
Меняются root build profile, setup CI/release и инструкция установки SDK для разработки.
Пользовательские global.json не переписываются; поддержка permissive policies остаётся
отдельным контрактом AR-18. В task-03 нужно синхронизировать соответствующий reference-текст
ARCHITECTURE, чтобы будущая проверка снова не приняла его за состояние дерева.

Шире finding:
нет.

---

ID: R-02
Verdict: PARTIALLY ACCEPT

Критика частично верна: строка «SDK 10» слишком груба для gate совместимости;
однако нижняя NuGet-зависимость клиента сама по себе не доказывает конкретный
runtime-сбой каждой старой полосы и не задаёт автоматически reference-пакеты отдельного форка.

Требования:
Исходная цель — поэтапный переход с проверками. Уже существующий
[README / «Матрица совместимости»](../README.md#матрица-совместимости)
разрешает явное изменение контракта, запрещая молчаливое превращение положительного случая
в отрицательный. [Task-00](../task-00-baseline-and-dependencies-med.md) должен дать
точный inventory dependencies и решение gate, а не скрыть несовместимость установкой SDK 11.

[Таблица SDK/MSBuild](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs)
различает 10.0.1xx / 18.0, 10.0.3xx / 18.6, 10.0.4xx / 18.9 и 11.0.1xx / 18.12.
[Пакет Workspaces.MSBuild 5.12.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.12.0)
указывает Framework >=18.7.1.
Кроме того, [рекомендация Microsoft для Locator](https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions)
требует выбирать compile-time MSBuild package major/minor не выше минимально поддерживаемого
toolchain. Поэтому при новых references 18.7.1 полосу 10.0.4xx следует считать
консервативным кандидатом нового минимального SDK 10, а не обещать 10.0.1xx без основания.

Но процессы и references нужно различать. Сейчас
[сервер](../../../../RoslynMcpServer.csproj) имеет compile-only ссылки 18.6.3,
а [проект форка](../../../../forks/roslyn-buildhost/build/BuildHost/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.csproj)
в строках 23 и 59–63 отдельно использует PackageDownload 17.11.48 и ref/net8.0.
Runtime MSBuild выбирается Locator:
[NetCoreBuildHost.FindMSBuild](../../../../forks/roslyn-buildhost/upstream/src/Workspaces/MSBuild/BuildHost/NetCoreBuildHost.cs).
Обновление PackageReference клиента не меняет этот PackageDownload автоматически.
Страница [Framework 18.7.1](https://packages.nuget.org/packages/Microsoft.Build.Framework/18.7.1)
подтверждает другие целевые assets, но точные ref-пути и требуемые API новой копии host
проверяются по nupkg/исходникам, а не только по таблице совместимых TFM NuGet.

Что менять:
В README заменить строку SDK 10 отдельными случаями с точным номером,
feature band, фактической версией/путём MSBuild и ожидаемым результатом.
В task-00 / «Работа», пп. 3–5, и «Результат и gate» добавить три разных выхода:
сохранение прежнего профиля, обоснованное явное повышение минимального SDK/MSBuild,
либо no-go обновления при неприемлемой потере совместимости.
Изменение feature band — отдельное основание; оно не сводится к требованию runtime 11.

При сохранении reference-профиля >=18.7.1 предложить положительные строки
10.0.4xx и 11.0.1xx; результат для 10.0.1xx и точного 10.0.300 оформить явно.
До объявления новой минимальной полосы проверить upstream support requirements,
API/assembly loading, выбор Locator и загрузку/семантические операции в каждой среде.
Ранее работавший профиль подтвердить старым baseline и описать как изменение поддержки,
если он снимается; поддержка Microsoft для SDK сама по себе не равна support promise этого сервера.

Явно включить в inventory четыре Reference-пути ref/net8.0 проекта форка,
его отдельный MsbuildReferenceVersion и все соответствующие package assets.
Если версия/TFM reference-пакетов меняются, новые пути должны существовать и сборка host
должна соответствовать выбранному runtime/upstream.

Что не менять:
Не заменять pin `10.0.300` «под зелёный прогон» и не объявлять все deferred
RequiresPinnedSdk fixtures обязательным положительным public-support gate.
[Их собственный контракт](../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/PinnedSdk.cs)
привязан к измеренному toolchain; основной CI их исключает.
Явное снятие поддержки такого consumer SDK и изменение исторической fixture —
разные действия. Если затронут общий runner/контракт, нужны реальные результаты
затронутых тестов и accurate pending/failure, а не автоматическая смена ожиданий.

Не считать доказанными заявленный конкретный сбой на каждом старом SDK
и обязательность перевода MsbuildReferenceVersion форка именно на 18.7.1.
Ни новый пакет, ни runtime-матрица в этом проходе не исполнялись.
Это не разрешение обещать старую совместимость вопреки документированным требованиям:
при недостатке evidence строка остаётся неподтверждённой.

Последствия:
Уточняются public support range, CI environments, диагностика отказа и release instructions;
при повышении минимума — явное compatibility change в release notes.
Исторические fixtures и baseline сохраняют смысл.
Вопрос выбора диапазона не закрывается одним успешным прогоном 10.0.402.

Шире finding:
нет.

---

ID: G-01
Verdict: PARTIALLY ACCEPT

Критика частично верна: отсутствие BuildHost не проверяет отдельный MSBuild Server;
фактический запуск лишнего сервера и блокировка outputs при текущем запрете node reuse
пока не доказаны.

Требования:
[README / «Матрица совместимости»](../README.md#матрица-совместимости) и
[task-03 / «Native smoke»](../task-03-packaging-and-acceptance-hi.md#native-smoke)
требуют корректных cancel/shutdown и сохранения процессов других сессий.
[DotNetCliRunner.CreateProcessStartInfo](../../../../Services/Build/DotNetCliRunner.cs), строка 343,
устанавливает MSBUILDDISABLENODEREUSE=1; TryKillProcessTree в строке 410 ограничен деревом процесса.
[Reaper](../../../../Services/Workspace/UnregisteredBuildHostReaper.cs) отвечает за workspace BuildHost,
а не является менеджером всех CLI build servers.

[SDK 11 tooling changes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk)
описывают включённый по умолчанию MSBuild Server и изменение обработки environment.
[MSBuild Server](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-server)
отличает сервер по MSBuild.dll и /nodemode:8, обычный worker — по /nodemode:1;
документирует /nr:false и shutdown.
Эквивалентность нашего MSBUILDDISABLENODEREUSE=1 этому запрету для нового server
не проверена этим проходом.

Что менять:
В README / матрице и task-03 / native smoke добавить отдельный gate MSBuild Server
после обычных build/test, отмены, timeout, failed build и shutdown.
В task-02 / проверках CLI runner зафиксировать политику: вызовы инструмента не оставляют
новый постоянный MSBuild Server как скрытый побочный эффект.
Снимать baseline PID/command lines/start time и diagnostic log, затем различать
workspace BuildHost, worker node и MSBuild Server; записывать создание, reuse или отказ от server.
Ноль BuildHost отдельно не даёт PASS этого gate.

Проверить текущий runner без изменений, включая inherited server environment.
Если имеющийся флаг не обеспечивает политику либо это остаётся недоказанным,
для дочерних CLI-вызовов явно задать документированный opt-out
`DOTNET_CLI_USE_MSBUILD_SERVER=false` и подтвердить эффект тем же gate.
Это изменение только child environment, без новой настройки и без изменения environment машины.
Доказанный достаточный текущий запрет не требует дублирующего механизма.

Что не менять:
Не утверждать, что текущий runner уже оставляет server и блокирует следующий publish:
review честно отмечает отсутствие SDK 11 прогона.
Не запускать `dotnet build-server shutdown` автоматически на машине пользователя
как замену ownership-aware cleanup. Такая команда может затронуть другие сборки/серверы.
Она допустима для подготовки/очистки изолированной тестовой среды, с наблюдением до cleanup:
сам cleanup не превращает провал lifecycle в PASS.
UnregisteredBuildHostReaper не расширяется до произвольных dotnet-процессов.

Последствия:
Меняются критерии CLI lifecycle и native smoke, при необходимости — один способ запуска
дочерних команд. Environment tests и реальные process/cancel tests дополняют main/lifecycle.
Если меняется server reuse, повторяются затронутые performance серии
в одинаковом режиме, чтобы warm MSBuild Server не подменил эффект нового runtime.

Шире finding:
нет — защита чужих процессов уже входит в исходный контракт этой же проверки.

---

ID: R-03
Verdict: ACCEPT

Критика верна: положительный net11 fixture не различает старое и новое поведение
невиртуального статического члена интерфейса на reference surface без DIM.

Требования:
Владелец просит проверки перехода на новый C#, а
[README / «Матрица совместимости»](../README.md#матрица-совместимости)
обещает языковой контроль. [C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)
включает это стабильное послабление.
[Описание CS8701](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/feature-version-errors)
явно разрешает такой случай с LangVersion 15.0 без повышения target framework.
Net11 уже предоставляет DIM и не выявляет потерю этой поддержки.

Что менять:
В следующей ревизии README / языковой строки добавить отдельный library fixture
`netstandard2.0` с явным `LangVersion=15.0`, невиртуальным статическим методом
интерфейса с телом и вызовом этого метода. Это переносимый compilation/binding контроль;
сам library TFM не является запускаемым runtime.

SDK 11 и встроенный Roslyn должны одинаково успешно компилировать/анализировать fixture:
нет CS8701, symbol привязан к ожидаемому члену, definition/references доступны.
Проверить effective parse options: LanguageVersion 15.0 не подменён default старого TFM.
Для чувствительности контроля — тот же исходник и references с языком 14 должны
воспроизводить прежнее ограничение; затем повторить на 15.
Не требовать запуска net472 runtime на Linux/macOS ради этой проверки.

Общая матрица применяется на этапах 1–2; этап 3 включает семантический smoke
этого случая, если он остаётся частью публичного promise C# 15.
Это тест пользовательского проекта, не разрешение повышать собственный LangVersion
сервера раньше этапа 2.

Последствия:
Один дополнительный compiler/semantic fixture и явно проверяемая диагностика.
Instance DIM, static abstract/virtual members и весь C# 15 на любом старом TFM
этим не объявляются поддержанными. Исключение memory-safety preview сохраняется.

Шире finding:
нет.

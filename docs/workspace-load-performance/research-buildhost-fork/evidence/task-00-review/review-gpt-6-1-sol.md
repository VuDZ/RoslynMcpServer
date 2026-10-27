# Независимое ревью BuildHost/task-00

- Модель: **GPT-6.1 Sol**.
- Дата: **2026-10-24**.
- Проверенный HEAD: **`972b7a84ebb55b73b47bc8acaa85920bf53cf081`**, ветка `research/workspace-load-performance`.
- HEAD из запроса: `0d6cece`; следующий commit добавляет запрос на ревью. Проверял текущее состояние файлов задачи, а не только последний diff.
- Среда собственных запусков: Windows 11, `10.0.26200.0`, x64; .NET SDK **10.0.402**, runtime **10.0.13**; PowerShell **7.6.5** и Windows PowerShell **5.1**.
- Статус ревью: **завершено; task-00 не принят в полном заявленном объёме**.
- Изменения ревьюера: только этот отчёт среди исходников и документации. Сборки и прогоны создавали ignored artifacts. Существовавшие до ревью посторонние untracked-файлы не изменялись.

## 1. Вердикт

**Принимать task-00 в полном заявленном объёме пока нельзя.** Основная схема форка работоспособна: копия upstream точная, netcore-host собирается, его подмена действительно используется, а проверенные workspace observations и первый поиск совпадают. Собственные прогоны это подтвердили.

Приёмку блокируют воспроизводимость повторного запуска измерителя (F-01), небезопасная область действия sampler (F-02), незакрытая проверка ранней отмены и отсутствие проверки обязательных исходов сценариев (F-03), неописанные границы observation (F-04) и неполный пакет обязательных проверок (F-05). Формат публичной сводки также требует исправления перед применением к частному решению (F-06).

Ранняя отмена оставляет host **у обеих сторон**. Это не доказательство регрессии форка и не основание менять upstream-код в рамках baseline без отдельного решения. Но это существенное ограничение проверки завершения процессов, которое нельзя заменить успешным exit code скрипта.

Этот отчёт не разрешает production activation, не закрывает task-01/task-02 и не меняет требования task-00. Task-файл и индекс не редактировались: запрос прямо разрешает менять только файл отчёта.

## 2. Находки

### F-01 — major: после первого запуска scenarios spike перестаёт собираться

**Место:** `spikes/BuildHostFork/BuildHostFork.csproj:2–12`; `spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1:66–94`; `spikes/BuildHostFork/BuildHostForkCommon.ps1:15`.

**Проблема.** Проект сохраняет стандартный рекурсивный Compile glob SDK, а scenarios создаёт C#-проекты и их `obj` внутри `spikes/BuildHostFork/results/`. `results/` исключён из Git, но не из Compile. Пустые Directory.Build-файлы изолируют сборку самой фикстуры, но не мешают родительскому spike подобрать её исходники.

**Сценарий.** После одного успешного scenarios следующий scenarios или baseline начинает собирать fixture source и generated assembly attributes вместе со spike.

**Доказательство.** Обычная команда из запроса на этом checkout остановилась на сборке spike с **32 CS0579**: дубли TargetFrameworkAttribute и assembly attributes из `results/scenarios-20261024-022138/fixture/.../obj/...` и собственного `obj/Release/net10.0`. Исходники не менялись. Сборка с временным параметром `-p:DefaultItemExcludesInProjectFolder=results/**` прошла.

**Исправление.** Исключить `results/**` из SDK items проекта либо создавать фикстуры вне дерева исходников spike. Добавить проверку именно повторного запуска, не удаляя предыдущие результаты для получения зелёной сборки.

### F-02 — major: sampler может измерять и завершать чужой BuildHost

**Место:** `spikes/BuildHostFork/BuildHostSampler.cs:60–65,91–119,137–140`; `spikes/BuildHostFork/FailureScenario.cs:74–75`.

**Проблема.** Sampler перечисляет процессы всей машины. Фильтр проверяет время старта, но не принадлежность процессу измеряемого runner. Любой новый BuildHost попадает в `_processes`; dotnet с недоступной командной строкой также принимается за host. Затем `FirstHost` используется для убийства, а `WaitForExitAsync` завершает всё оставшееся через `Kill(entireProcessTree: true)`.

**Сценарий.** Во время прогона другая IDE, другой MCP workspace или второй spike начинает загрузку решения. Его host может стать первым обнаруженным: kill-host убьёт чужую загрузку. Даже без kill-host cleanup через 10 секунд может завершить этот процесс. Одновременно CPU, RAM, inventory и момент отмены будут относиться к смешанному набору процессов.

**Доказательство.** В перечислении и фильтре нет parent/ancestor PID или другого идентификатора владения. Путь host не ограничивает набор конкретной стороной; недоступная командная строка возвращает положительную идентификацию. Это вывод из прочитанного кода; чужие пользовательские процессы намеренно не запускались и не завершались для доказательства.

**Исправление.** Ограничить наблюдение потомками данного runner. Неизвестные процессы можно отражать как неопределённость измерения, но убивать только процессы с подтверждённым владением. PID при повторном обращении также проверять вместе со временем старта, чтобы не затронуть переиспользованный PID.

### F-03 — major: ранняя отмена оставляет host, а сценарии не проверяют обязательные исходы

**Место:** `spikes/BuildHostFork/FailureScenario.cs:26–40,55–69`; `spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1:98–124,143–163`; `spikes/BuildHostFork/ObservationComparer.cs:80–90`.

**Проблема.** Runner честно записывает `RemainingBuildHosts` до принудительного завершения, однако скрипт только печатает результаты. Он не отклоняет отсутствие воздействия, утечку, несовпадение диагностик или неверный исход broken-host. В baseline summary остаток процессов вообще не выводится.

**Сценарии и доказательства собственных запусков.**

- При стандартном `DelayMs=1500` на этой машине все четыре строки cancel/kill закончились как `completed`, `DisturbedAtMs=null`: решение успело загрузиться. Скрипт завершился с кодом 0. Это четыре невыполненные проверки воздействия, а не четыре успешных отказных теста.
- В Windows PowerShell 5.1 с `DelayMs=0` отмена действительно произошла. Оригинал: `canceled`, воздействие **427,43 мс**, конец load **437 мс**, `RemainingBuildHosts=1`. Форк: `canceled`, воздействие **429,08 мс**, конец load **439 мс**, `RemainingBuildHosts=1`. После 10 секунд ожидания sampler принудительно завершил эти процессы. Скрипт снова завершился с кодом 0.
- В том же прогоне kill-host обеих сторон завершился ошибкой разрыва соединения и `RemainingBuildHosts=0`; broken-host не перешёл на оригинал.
- Контрольный JSON с неизменными workspace/search и `RemainingBuildHosts=7` даёт baseline summary `identical` без упоминания утечки. Здесь `identical` описывает только сравниваемые поля; сводки недостаточно для приёмки lifecycle.

Прочитанный upstream `BuildHostProcessManager` согласуется с возможной утечкой при отмене до регистрации созданного host в `_processes`, но точную точку возникновения утечки отдельной трассировкой я не устанавливал.

**Исправление.** Проверять, что cancel/kill реально выполнились, ожидаемые исходы получены и остаток процессов соответствует контракту. Невыполненное воздействие обозначать как inconclusive и повторять с подходящей задержкой. Выводить остаток процессов в baseline summary. Зафиксировать раннюю отмену как воспроизводимое поведение обеих сторон и явно решить, какой scope завершения процессов принимается; не объявлять общий критерий закрытым только по поздней отмене.

### F-04 — major: observation не доказывает всю заявленную эквивалентность, ограничения не перечислены

**Место:** `spikes/ParallelLoad/SolutionObservation.cs:16–38,49–73`; `docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md:37–44`; `evidence/task-00-fork-baseline.md:49–55,114–116,142–146`.

**Проблема.** Использование observation из E0 сохраняет существующий измеритель, но не превращает его в полный oracle требований task-00. В baseline-отчёте нет требуемого explicit limitation для отсутствующих полей.

**Что реально не снимается:**

- source-generator outputs через `GetSourceGeneratedDocumentsAsync`, их набор и содержимое;
- содержимое обычных, additional и analyzer-config documents: сравниваются только пути;
- analyzer provenance сверх `AnalyzerReference.FullPath`: нет hash/MVID/identity, происхождения и результатов загрузки;
- полный набор compilation/parse options: снимается выбранное подмножество, например без `GeneralDiagnosticOption`, `WarningLevel`, `ConcurrentBuild` и `Deterministic`;
- отдельный явный effective TFM/global-properties descriptor варианта: ключ — путь проекта и имя. Multi-TFM в фикстуре различается именами, но это не полный снимок effective properties;
- compilation/analyzer diagnostics: поле Diagnostics содержит события `WorkspaceFailed`, а не все диагностики компиляции и анализаторов.

**Сценарий.** Меняется generated source, содержимое файла по прежнему пути, неподхваченное option или DLL анализатора по тому же пути. Если это не затрагивает единственный поисковый запрос, comparer по-прежнему может показать `identical`.

**Доказательство.** `AddDocuments` сериализует только `FilePath`; analyzer references — только `FullPath`; `AddOptions` перечисляет конкретные поля; вызова чтения generated documents нет. Первый поиск сравнивает один ответ, а не всё сгенерированное содержимое.

**Исправление.** Как минимум перечислить эти ограничения в evidence и ограничить формулировку `identical` именно captured schema. Задача прямо допускает такой способ фиксации недостающих полей. Полную эквивалентность generated sources/provenance без дополнительных наблюдений не заявлять.

### F-05 — major: в списке незавершённого потеряны Smoke, PowerToys и обязательные validation receipts

**Место:** `docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md:30–32,42–51`; `evidence/task-00-fork-baseline.md:81–91,215–217`.

**Проблема.** Требование сравнения на Smoke и PowerToys остаётся в постановке. Отчёт описывает `RoslynMcpServer.sln`, полный Roslyn и офисное решение, но не фиксирует замену двух fixtures как отклонение или pending. Финальная фраза «Остаётся по task-00: независимое ревью» поэтому неполна. Локальный Linux baseline также содержит только два измеренных раунда вместо минимальных трёх; это указано в описании прогона, но не учтено в перечне незавершённого.

В отчёте также нет receipts Release solution build и полного main CI suite, обязательных для code/test commits по task-файлу и AGENTS.md. Это отсутствие доказательства в пакете, **не утверждение, что проверки никогда не выполнялись**. Изменён `SourceSetScope.cs`; принятие этого изменения требует предусмотренной проверки, а не только сборки отдельного spike.

**Сценарий.** После получения одного положительного ревью task-00 будет закрыт по текущему списку, хотя два обязательных публичных fixtures и часть validation evidence отсутствуют.

**Исправление.** Вернуть эти пункты в pending и приложить требуемые receipts. Выполнить Smoke/PowerToys либо получить явное изменение принятого scope от владельца. Для каждого обязательного fixture сохранить warmup и минимум три measured fresh-process rounds. Отдельно указать статус AnalyzerLifecycle и основание его применимости/неприменимости; не выдавать отсутствие receipts за pass.

### F-06 — major: публичная scenarios-сводка сохраняет имя частного решения

**Место:** `spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1:128–137,151,168`; `evidence/task-00-fork-baseline.md:127`.

**Проблема.** Заголовок `scenarios.md` напрямую вставляет `Split-Path $Solution -Leaf`, минуя redaction. Замена префиксов в `Format-Redacted` чувствительна к регистру; Windows-путь, возвращённый с другой капитализацией, не будет скрыт. Даже совпавшая замена корня оставляет имена вложенных проектов в тексте исключения. При этом заключительная подсказка предлагает использовать эту сводку для анализа.

**Сценарий.** Для синтетического `E:\Private\ConfidentialReviewFixture.sln` сводка публикует `ConfidentialReviewFixture.sln`. Замена префикса `E:\Private` не изменяет строку `e:\private\InternalProject\Secret.csproj`. Эти два простых случая проверены командами PowerShell. Это примеры, а не реальные офисные имена.

**Доказательство.** Строка 137 обходит `Format-Redacted`; строка 130 использует обычный `string.Replace`. В опубликованном baseline оставлен также буквальный поисковый запрос офисного прогона: это не агрегат, хотя сам по себе он не доказывает идентификацию решения.

**Исправление.** В публичной сводке не выводить имя частного решения и его поисковый запрос; для частных cancel/kill оставлять тип ошибки и агрегаты вместо произвольного exception text. Известные публичные fixture diagnostics можно сохранять отдельно. Если используется redaction путей, учитывать Windows case/separator variants и не считать замену одного корня гарантией удаления внутренних имён.

**Граница вывода.** Идентифицирующих офисных путей или имён проектов в проверенных tracked-файлах этого пакета я не обнаружил. F-06 описывает подтверждённый дефект генератора сводки и нарушение строгого формата «только агрегаты», а не доказанную публикацию названия организации.

### F-07 — minor: естественный разброс диагностик получает вердикт identical

**Место:** `spikes/BuildHostFork/ObservationComparer.cs:38–40,66–72`; `spikes/ParallelLoad/compare.py:10–36`.

**Проблема.** Правила ever/always отлавливают новую диагностику форка и исчезновение стабильной диагностики. Но допустимая разница с первым оригиналом не добавляется в natural differences. Поэтому `identical` может означать, что диагностики различаются.

**Воспроизведение.** При одинаковых Projects/FirstSearch создать `original-round0` с `Diagnostics=[]`, `original-round1` с `Diagnostics=["Warning: sometimes"]` и fork с той же Warning. Реальный comparer напечатал `identical; natural-variance fields: 0; unexplained: 0`. В E0 отличие диагностики от reference учитывается и классифицируется как natural.

**Исправление.** Сохранить ever/always admission, но учесть допустимую симметрическую разницу с reference при выборе `natural variance only`. Добавить небольшой self-check именно на диагностический разброс. Для доступных исторических Roslyn observations диагностики действительно равны, поэтому эта ошибка сама по себе не опровергает их `identical`.

### F-08 — minor: отсутствие замедления и шум CPU заявлены сильнее доступных данных

**Место:** `evidence/task-00-fork-baseline.md:116,144–146`.

**Проблема.** Медианы трёх измерений load ×0,999/×1,001 показывают близость наблюдённых медиан, но не доказывают отсутствие регрессии. Из совпадения load/host CPU и малого числа измерений также не следует, что client CPU ×1,057 — обязательно шум.

**Сценарий.** Стабильная прибавка CPU клиента не влияет на wall из-за ожиданий или параллелизма. Текущая формулировка заранее относит её к разбросу и переносит проверку за следующие оптимизации.

**Доказательство.** В публичном пакете нет дисперсии/парных раундов офисного CPU или повторного эксперимента для объяснения ×1,057. Офисные raw observations мне недоступны, поэтому и наличие регрессии я не утверждаю.

**Исправление.** Написать «в данном прогоне медианы времени загрузки близки», а отличие CPU оставить необъяснённым наблюдением. Для вывода о шуме приложить распределение по раундам или повторный контроль при неизменном scope.

### F-09 — minor: SHA-256 inventory не воспроизводится по файлам обычного Windows checkout

**Место:** `.gitattributes:1–3`; `forks/roslyn-buildhost/UPSTREAM.md:23–131`.

**Проблема.** Защита exact bytes в `.gitattributes` есть для performance docs и `spikes/ParallelLoad`, но отсутствует для upstream-копии. В рабочей копии Windows **105 из 109** файлов не совпадают с опубликованным SHA-256 из-за LF→CRLF.

**Доказательство.** Хеши всех **109 Git blobs HEAD совпали** с inventory, а их Git object ids — с публичным деревом upstream нужного commit. Для всех 105 отличающихся disk files нормализация CRLF→LF дала исходный blob. Изменения смысла исходников или подмены upstream не обнаружены.

**Сценарий.** Рецензент выполняет `Get-FileHash` по checkout и получает массовое несовпадение, хотя Git содержимое корректно.

**Исправление.** Зафиксировать сохранение bytes для `forks/roslyn-buildhost/upstream/**`, либо явно описать проверку Git blobs вместо working-tree bytes. Если заявляется именно побайтный checkout, нужен первый вариант.

### F-10 — minor: общий статус README противоречит выполненной реализации

**Место:** `docs/workspace-load-performance/research-buildhost-fork/README.md:3–4,29,33–35`.

**Проблема.** Заголовок сообщает `planned; реализация форка не начата`, а далее написано, что реализация и новые измерения не запускались. Строка task-00 в том же файле сообщает уже выполненную реализацию и замеры.

**Сценарий.** Следующий исполнитель читает общий execution status и получает неверное состояние исследования, несмотря на правильный `in progress` в строке задачи.

**Исправление.** Согласовать общий статус с task-файлом и evidence, сохранив непринятые проверки как pending. После обработки ревью добавить ссылку на него и точный принятый scope. Сейчас task-00 правильно не помечен accepted; проблема именно в противоречащих общих сводках.

### F-11 — nit: имя файла не совпадает с независимым типом

**Место:** `spikes/BuildHostFork/ProjectMergerKey.cs:7`.

Файл объявляет `ProjectMerger`, тогда как `docs/code-style.md`, раздел Files and types, требует совпадения имени файла и независимого типа. Исключение для `forks/` к spike не относится. Это обнаруживается при поиске типа по имени файла и нарушает явное правило, хотя поведение не меняет.

**Исправление.** Назвать файл `ProjectMerger.cs`; существующая логика shim может остаться без изменений.

## 3. Проверено и в порядке

### Источник, ревизия и полнота

- Nuspec локального пакета `Microsoft.CodeAnalysis.Workspaces.MSBuild/5.9.0` действительно содержит repository commit **`35d9211b841e7613c1d2f8f5af6d628ace696c4c`**, branch `release/insiders` и MIT license.
- Через публичный [GitHub Git Trees API](https://api.github.com/repos/dotnet/roslyn/git/trees/35d9211b841e7613c1d2f8f5af6d628ace696c4c?recursive=1) получено дерево именно этой ревизии: 32 290 blobs, `truncated=false`. Для всех 109 manifest entries Git blob id совпал с upstream; SHA-256 Git blobs совпал с `UPSTREAM.md`. В трёх скопированных поддеревьях BuildHost, Contracts и Dependencies/Contracts нет пропущенных или добавленных файлов относительно upstream. Все 16 compiler links присутствуют. Разницу working-tree bytes описывает F-09.
- `.editorconfig`, License и third-party notices также сверены по blob id. В upstream-копии нет содержательных fork-правок. Поэтому к ней применим стиль Roslyn; переоформление по стилю сервера было бы ошибкой.

### Сборка без Arcade

- Прочитаны оригинальные проекты из проверенной upstream-копии и build settings точной upstream-ревизии: `Directory.Build.props/targets`, `eng/targets/Settings.props`, `Imports.targets`, `TargetFrameworks.props`, `eng/Packages.props`, `global.json`.
- Подтверждены net8.0, `LangVersion=preview`, `Nullable=enable`, `MSBUILDWORKSPACE_BUILDHOST`, reference-only MSBuild **17.11.48** с `Private=false` и host Locator **1.10.2**. Locator **1.11.2** в spike относится к клиенту и одинаков на обеих сторонах; это не версия Locator внутри host.
- MSBuild evaluation Release показала `TRACE;MSBUILDWORKSPACE_BUILDHOST;RELEASE`; .NET TFM symbols формирует SDK. Дополнительного требуемого feature-symbol для проверенных netcore sources не обнаружено. Compile evaluation содержит 43 исходных файла BuildHost, включая 16 compiler links; общий Contracts projitems импортирован. Host и Contracts успешно собраны в Release.
- Имя обеих сборок и AssemblyVersion **5.9.0.0** совпадают. FileVersion отличается: original `5.900.26.35703`, fork `5.9.0.0`; fork informational version явно содержит `fork+35d9211b...`. Это диагностическая маркировка, не изменение JSON-протокола.
- Reflection в отдельных AssemblyLoadContext подтвердила совпадение manifest resource name `Microsoft.CodeAnalysis.WorkspaceMSBuildBuildHostResources.resources` и **обоих** ключей/английских значений ресурса. SDK designer имеет ожидаемые namespace и class name. Нейтральные ресурсы не потеряны.
- У original есть strong name и Authenticode, у fork их нет. Для host measured sizes: **112 464** против **100 864** bytes; PE certificate table **10 064** против **0**, strong-name signature **128** против **0**. Большая часть разницы размера объясняется подписью; оставшиеся metadata/resource-generation differences не являются доказательством потери логики. Полное IL-сравнение не выполнялось.
- В fork Contracts сохранён необходимый IVT для BuildHost; остальные upstream friends и IVT самого host отсутствуют. Для отдельного процесса с JSON RPC это не мешает проверенной границе. Такой Contracts нельзя считать универсальной бинарной заменой исходной signed assembly внутри процессов других Roslyn consumers.
- `runtimeconfig.json` семантически одинаков: net8.0, runtime 8.0.0, `rollForward=Major`, одинаковые runtime switches. В `deps.json` нет runtime closure MSBuild, Locator совпадает. Files не тождественны: версии проектов отличаются, fork содержит дополнительную Contracts.Reference entry на ту же DLL; original перечисляет satellites. Запущенные host разрешили зависимости успешно.
- Xlf локализации не собираются, что явно отмечено в evidence. В фактическом NuGet `BuildHost-netcore` обнаружены только шесть файлов, satellite DLL там нет, хотя deps перечисляет ресурсы. Поэтому потерю локализованных сообщений относительно именно этого deployed package я не установил. Общее обещание эквивалентности локализованному upstream deployment не проверено.

### Подмена и выбор host

Прочитан [BuildHostProcessManager.cs точной ревизии](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs), а также `MSBuildProjectLoader.cs` той же ревизии.

- Для deployed client используется соседний `BuildHost-netcore`; отсутствующая DLL приводит к исключению. При загрузке client прямо из NuGet предусмотрена другая раскладка `contentFiles/any/any`. Это выбор layout, не поиск запасной original DLL после сбоя fork.
- Переменной окружения для переопределения DLL path в проверенном manager нет. Выбор dotnet/SDK может перезапустить host, сохраняя путь его DLL. Один manager переиспользует процессы по kind, но loader создаёт новый manager на load и освобождает его; fresh-process rounds не разделяют manager cache.
- Сохранён original net472/Mono путь. Kind выбирается по структуре project XML, не только по TFM: SDK-style net472 может идти через netcore. Есть fallback Framework→NetCore без usable VS MSBuild и Mono→NetCore без Mono; обратного перехода с отсутствующего fork-netcore на original не обнаружено.
- Инициализация `bin-fork` удаляет старую netcore-папку, копирует fork и проверяет `*fork*`. Сравнение хешей client DLL двух копий и реальные командные строки в собственных successful runs подтверждают использование соответствующих папок. `bin-fork-broken` удаляет именно fork DLL; оба выполненных сценарных прогона дали ожидаемое исключение без запуска запасного host.

### Observation и comparator

- Набор project keys проверяется; дубликат ключа вызывает ошибку. Document paths сравниваются и в исходном порядке, и как множество. Metadata/project references включают aliases и EmbedInteropTypes; output paths и перечисленные options снимаются. Ограничения — F-04.
- Reference — первый original по номеру раунда. Natural value допускается только если конкретное значение того же поля встречалось в original; поле не исключается из сравнения целиком. Синтетические проверки на `A/B` у original и `C` у fork дали `different`; значение `B` дало `natural variance only`; удаление проекта дало `different`; другой search hash дал `different`.
- Встречавшаяся только иногда original-диагностика допускается правилом ever/always, новая fork-диагностика и пропавшая always-диагностика отклоняются. Недостаток маркировки допустимого разброса — F-07. Дубликаты диагностик теряются в set, как и в E0.
- Не обнаружен дополнительный обход через JSON null/пустую строку в текущем Capture: значения полей уже содержат сериализованный JSON, поэтому строка `null` и строковое пустое значение различаются. Вручную повреждённая внешняя schema отдельно не валидируется.
- Медианы используют только раунды >0; round 0 участвует в equivalence. C# comparer не переносит case-only исключение E0 и в этом отношении строже. Поиск также сравнивается строже: один hash требуется для всех original и fork.
- Проверены собственным пересчётом доступные ignored результаты исторического Roslyn baseline `baseline-20261024-012310`: четыре запуска на сторону, **434** проекта в каждом; Projects, Diagnostics и search hash равны во всех original/fork observations. Медианы load **39 970,1972 / 39 934,9072 мс** и search **20 610,8622 / 20 904,8544 мс** согласуются с опубликованной таблицей. `RemainingBuildHosts` в этих старых JSON отсутствует; lifecycle из них не выводится.

### Sampler, interop и failure recording

- Реальные netcore command lines прочитаны на Windows x64 и правильно отнесены к original/fork. Разбор кавычек вокруг host path со spaces соответствует способу передачи ArgumentList; отдельный запуск из каталога со spaces не выполнялся.
- `UNICODE_STRING` с Sequential layout и двумя ushort перед IntPtr имеет ожидаемое выравнивание: 16 bytes с pointer offset 8 на x64, 8 bytes с offset 4 на x86; для ARM64 ожидается 64-bit layout. Класс 60, запрос размера и последующее чтение из возвращённого буфера согласуются с работающим x64-прогоном. Cross-bitness и ARM64 этим не доказаны.
- Фильтр StartTime убирает ранее запущенные процессы. CPU — явно нижняя граница из-за опроса; короткий процесс может целиком попасть между samples. `Timer.Dispose()` не является ожиданием уже исполняющегося callback. Эти свойства не дают точного process inventory или доказательства отсутствия всех кратковременных процессов.
- `peakWorkingSetMb` — максимум пика **одного** обнаруженного процесса, не пик суммы одновременно живых host. Inventory содержит count и distinct folders, без per-PID timelines. Не следует читать таблицу как полную RAM всех descendants, включая MSBuild nodes. Область владения процессами требует F-02.
- `DescribeOutcome` различает completed/canceled/faulted task; сообщения ошибок берутся из первой inner exception. Это не счётчик всех диагностик.
- `DisturbedAtMs` записывается после Cancel/Kill; окончание load на несколько миллисекунд раньше этой отметки допустимо и наблюдалось при kill. Это не отрицательная длительность самого воздействия.
- `WaitForExitAsync` возвращает список alive до добивания, поэтому реально обнаруженные утечки не превращаются в ноль из-за cleanup: ранняя отмена дала единицу. Недостаток находится в приёмочном verdict, не в самом подсчёте перед Kill.
- Пятиминутное ограничение есть у ожидания async load в FailureScenario. У observe внешнего timeout нет; саму ветку timeout на пять минут не запускал.

### PowerShell, isolation и стиль

- Порядок сторон в measured rounds чередуется, каждый observe запускает отдельный dotnet process; fixture warmup + один measured round не выдавался в этом ревью за полный performance baseline.
- Пустые Directory.Build.props/targets действительно изолируют fixture от родительских build settings. Отдельная проблема родительского Compile glob — F-01.
- Сценарий полностью выполнился в Windows PowerShell 5.1 под StrictMode 2.0. Ожидаемый broken-host не оборвал обработку из-за native stderr. Прочитаны `Invoke-Spike` и `Get-GitOutput`, восстанавливающие ErrorActionPreference после временного Continue; отдельный non-Git запуск baseline не выполнялся.
- Production project исключает `forks/**` из Compile, None и EmbeddedResource; SourceSetScope отражает это исключение. Production package references/defaults не переключены на fork. SourceStructure здесь повторно не запускался.
- `docs/code-style.md` прочитан полностью. В новых spike-файлах и изменённых строках SolutionLoader проверены порядок членов, именование, braces и английский текст. Кроме F-11 обязательных стилевых нарушений не обнаружено. Неизменённый старый код соседних spikes не оценивался как новая стилевая обязанность.

### Собственные команды и результаты

Команды запускались из корня репозитория. Ниже `$out` означает отдельную ignored папку результатов; это не обязательный параметр исходных скриптов.

1. Без обходов:

   ```powershell
   pwsh -NoProfile -File spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1 -OutDirectory $out
   ```

   **Fail:** сборка spike, 32 CS0579, F-01. BuildHost fork перед этим собрался.

2. Для исследования поведения без изменения исходников:

   ```powershell
   $env:DefaultItemExcludesInProjectFolder = 'results/**'
   pwsh -NoProfile -File spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1 -OutDirectory $out
   ```

   В sandbox original не смог подключиться к named pipe: UnauthorizedAccessException. Повтор вне sandbox прошёл: fixture **identical**, шесть проектов, пять одинаковых Failure; broken fork завершился ожидаемой ошибкой без host/fallback. Default cancel/kill не успели воздействовать, F-03. Эта команда не является pass неизменённого способа сборки: применён обход F-01.

3. С тем же обходом и вне sandbox:

   ```powershell
   pwsh -NoProfile -File spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1 `
       -Solution RoslynMcpServer.sln -Name SolutionManager -Runs 1 -OutDirectory $out
   ```

   **Workspace/search pass:** пять проектов, два fresh processes на сторону (warmup + один measured), `identical`, natural 0, unexplained 0. Load **1817 / 1868 мс**, первый поиск **1315 / 1324 мс**, measured load ratio **1,028**. Это smoke собственной воспроизводимости, не требуемые минимум три performance rounds и не статистическое доказательство отсутствия регрессии.

4. В Windows PowerShell 5.1, также с наследуемым обходом и вне sandbox:

   ```powershell
   powershell.exe -NoProfile -File spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1 `
       -DelayMs 0 -OutDirectory $out
   ```

   Fixture **identical**; broken-host **failed**, без fallback; cancel обеих сторон **canceled**, но **по одному оставшемуся host после 10 секунд**; kill обеих сторон **failed/disconnected**, остаток **0**. Скрипт вернул 0, поэтому его exit code не принят как общий validation pass.

Ignored receipts этого checkout:

- `spikes/BuildHostFork/results/review-gpt-6-1-sol-scenarios-20261024/` — попытка обычного запуска;
- `spikes/BuildHostFork/results/review-gpt-6-1-sol-scenarios-unrestricted-20261024/` — scenarios PS7 с обходом;
- `spikes/BuildHostFork/results/review-gpt-6-1-sol-baseline-20261024/` — baseline с обходом;
- `spikes/BuildHostFork/results/review-gpt-6-1-sol-ps51-scenarios-20261024/` — scenarios PS5.1 с ранней отменой.

Во время сборок были **NU1900**: недоступны NuGet vulnerability data. Сборка использовала доступный package cache; это не успешная свежая сетевая restore-проверка. Fork source, scripts и project files ради запусков не исправлялись.

## 4. Не проверено и границы вывода

- **Офисные raw results и само решение недоступны.** Их числа, aliases, generated sources, фактическая доля host kinds и CPU variance не проверены. Проверена только внутренняя согласованность опубликованных агрегатов; реальные внутренние имена в этот отчёт не переносились.
- **Linux в этом ревью не запускался.** Исторический Linux report прочитан, его прогоны не воспроизведены.
- **Smoke и PowerToys original/fork не запускались.** Их отсутствие в evidence остаётся F-05; новый прогон не заменяет отсутствие исходной приёмки.
- **Полный Roslyn заново не загружался.** Доступные локальные ignored JSON исторического публичного прогона проверены независимо от summary; это проверка сохранённых наблюдений, не новый эксперимент. В отличие от файлов Git, эти ignored файлы не являются публично сохранённым архивом доказательств.
- **net472-host/Mono, Windows x86, ARM64 и cross-bitness interop не запускались.** Неизменённость net472 binary в двух копиях не доказывает проверку fork на этой ветке исполнения.
- **Release solution build, полный main suite, SourceStructure и AnalyzerLifecycle ревьюером не запускались.** Это ревью с запретом правок кода, а не интеграция исправлений. Успешные сборки host/spike не заменяют обязательную validation исходной задачи; отсутствие receipts отмечено в F-05.
- **Все compilation/analyzer diagnostics, generated-source equality и полный provenance не проверены** — F-04. Нельзя расширять `identical` с captured schema до этих полей.
- **Точный IL diff и reproducible binary build не выполнены.** Размер и подписи исследованы по PE metadata; меньший размер сам по себе не объявлен дефектом.
- **Длительные timeout/hung-target сценарии не запускались.** Прочитан код ограничения FailureScenario; предел observe отсутствует.
- **Проверка публикации ограничена tracked-файлами рассматриваемого пакета на указанном HEAD.** История всего репозитория и внешние архивы не проходили отдельный поиск утечек.

Независимое ревью завершено; необходимые исправления и решения по scope остаются открытыми. Для завершения task-00 нужны обработка major findings, оставшиеся validation receipts и согласованные статусы task/index/evidence с ссылкой на итоговую приёмку.

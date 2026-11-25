# Справочник MCP-инструментов

[English version](tools.md) · [Назад к README](../README.ru.md) · [Установка и настройка](setup.ru.md)

Каждый инструмент описывает себя сам: `get_tool_help` возвращает актуальные параметры зарегистрированного инструмента, а `list_tool_groups` показывает активные группы.

**Имена параметров в JSON:**
- `filePath` — один файл (чтение/правка/диагностика/логи).
- `directoryPath` — корневая папка (`list_directory_tree`, опционально корень для `search_code`).
- `includeExtensions` — опциональный фильтр расширений для `search_code` (по умолчанию `.cs`; `*` = все файлы).
- `caseSensitive` — опционально для `search_code` (по умолчанию `false`; для leftover branding — `true`).
- `workspacePath` — `.sln` / `.slnx` / `.csproj`, а у части тулов ещё и каталог. Только файл: `load_workspace`, `run_dotnet_build` (не каталог), `run_dotnet_run` (исполняемый `.csproj`). Каталог допустим: `run_dotnet_test`, `run_specific_test`, `run_test_by_filter`, `run_format`, `list_nuget_packages`, `list_outdated_packages`, `run_nuget_audit`, `get_changed_files`. Опциональная перезагрузка: `list_projects` / `get_project_graph`. Для multi-config solution предпочитайте `.sln`/`.slnx`.
- `symbolName` — идентификатор C# для `find_symbol_definition`, `find_symbol_references`, `find_usages` и `find_implementations` (точное имя; поиск по solution для definition/usages/implementations и `find_symbol_references` без `filePath` без учёта регистра; `filePath` без `line` у definition и references — порядковое сравнение с учётом регистра).
- `diagnosticId` — код диагностики компилятора из `get_diagnostics_for_file` (например `CS0246`) для `get_code_fixes` / `apply_code_fix`.
- `fixIndex` — индекс (0-based) из `get_code_fixes` для `apply_code_fix`.
- `path` — файл `.cs` или каталог для `get_code_skeleton` (абсолютный путь; с диска, workspace не обязателен).

Зарегистрировано **55** инструментов в профиле `full` (список ниже). Профиль `lite` стартует с **16** core-тулов; остальные группы — `ROSLYN_MCP_TOOL_GROUPS` или `enable_tool_group`. Девять методов с атрибутом не входят в `tools/list` до v2 и не вызываются: `remove_using`, `organize_usings`, `add_property_to_class`, `add_field_to_class`, `add_type_to_class_bases`, `remove_member`, `implement_interface`, `extract_interface`, `move_type_to_new_file`.

### Workspace / Roslyn

<details>
<summary><code>load_workspace</code> — Загружает .sln/.slnx/.csproj в MSBuildWorkspace.</summary>

**Параметры:**
- `workspacePath: string` — файл `.sln`, `.slnx` или `.csproj` (не каталог). Для multi-config — предпочтительно solution.
- `configuration: string?` — опционально MSBuild `Configuration` (например `Sit-Debug`, `kart`). Наследуют build/test, если не передали `-c`.
- `platform: string?` — опционально MSBuild `Platform` (`Any CPU` → `AnyCPU`).
- `targetFramework: string?` — опционально MSBuild `TargetFramework` (например `net10.0`). Нужен, когда в решении `TargetFrameworks` (inner TFM с target `Compile`). Build/test это не наследуют.
- `buildArgs: string?` — опциональные extra-аргументы для последующих `dotnet build` (probe и пребилд тестов). Пропуск или пробелы не сбрасывают уже загруженное значение. `reset_workspace` сбрасывает сессию, включая `buildArgs`. Не класть `-c`, `-p:Platform`, `-v`, `--no-incremental`.
- `briefOutput: bool = false` — `true` сворачивает предупреждения MSBuild/NuGet успешного load в счётчики категорий и кодов. По умолчанию полный дамп. Ошибки load всегда печатаются целиком.
- `logProjectOutputDiagnostics: bool = false` — диагностически пишет в MCP-лог resolved output и состояние `AnalyzerReference`; в ответ tool эти данные не попадают. Используйте для in-solution analyzer/generator проектов, если `Directory.Build.props` переопределяет `OutputPath`.
- `shadowCopyInSolutionAnalyzers: bool = false` — заменяет только те `AnalyzerReference`, которые complete provenance snapshot текущей load-сессии exact-join-ит к одному загруженному in-solution проекту. Filename/`AssemblyName` не считаются evidence; external same-name, unconfirmed и ambiguous ссылки сохраняются без изменения. Недоступный original path — skip (U-ARB-01; `access_failure`; не missing; не rewrite); см. [решение](archive/analyzer-shadow-copy-improvements/v2/u-arb-01-inaccessible-skip.md). Rewrite/fail-closed остаются альтернативами, если полевые репозитории потребуют смены. Missing/Failed/Incomplete или чужой session capture не публикует semantic snapshot (MSBuild graph при этом может открыться); cached `true` переиспользует этот capture — нужны `reset_workspace` и повторный `load_workspace`. Неуспешный opt-in остаётся fail-closed после edit/flush/reconciliation: raw workspace может хранить original refs для persistence, но не публикует и не исполняет их. Активация session-sticky: после включения same-key cached `false`/omitted сохраняет активный overlay без preparation/refresh. Analyzer-проект должен быть предварительно собран. Поколения адресуются по содержимому и повторно применяются при edit без копирования analyzer-файлов. После пересборки генератора с той же assembly identity нужен **новый процесс MCP** (`reset_workspace` не выгружает CLR). Приватные helper DLL отклоняются (main-only). Краткая сводка возвращается в ответе, детали доступны через `tail_tool_log` / `read_log_tail`.

**Активация и побочные эффекты analyzer shadow copy:** флаг opt-in; MCP-клиент и агент автоматически его не включают. Для репозитория с in-solution analyzer/generator и custom `OutputPath` добавьте условное правило в его `AGENTS.md`. При включении versioned DLL/PDB копируются в `v2-main-only/` под OS temp; overlay повторно применяется после обновлений документов. Активация session-sticky: cached `false`/omitted сохраняет уже активный overlay и не обновляет analyzer-файлы; отключение — `reset_workspace`, затем load без флага. Старые поколения (включая timestamp-каталоги) автоматически не очищаются; reset их не удаляет. Реальные `.csproj` и build output не изменяются; на поддержанных overlay-путях MCP-процесс блокирует shadow copy, а не real output. Path resolution и anti-lock независимы. `find_symbol_definition` по имени generated-типа — ограничение Roslyn, не отказ overlay.

**Atomic anti-lock boundary (U-ARB-04):** physical load/cache lookup, opt-in prepare, execution gate и публикация итогового snapshot выполняются как одна сериализованная manager-операция. Production semantic readers ждут эту boundary (`GetPublishedSolutionAsync` / `FindDocumentAsync`) и не компилируют промежуточные raw analyzer references. `GetCurrentSolution()` возвращает только опубликованный snapshot. Если opt-in вошёл первым, конкурентный semantic получает shadow overlay; prepare failure/cancellation новой сессии публикует только fail-closed snapshot и позже не возвращает confirmed real refs. Semantic, завершённый до более позднего enable, остаётся честной no-overlay операцией и может привести identity gate к restart-required. См. [evidence](archive/analyzer-shadow-copy-improvements/v2/u-arb-04-load-boundary-evidence.md), [решение](archive/analyzer-shadow-copy-improvements/v2/u-arb-04-decision.md) и [спецификацию реализации](archive/analyzer-shadow-copy-improvements/v2/u-arb-04-atomic-load-prepare.md).

**Поведение:** abort хоста mid-load → **Workspace Load Cancelled (client abort)** (поднять MCP timeout; это не ошибка MSBuild). После успешного load **сохранённые** `.cs` вотчатся и подмешиваются в поиск символов (несохранённый буфер игнорируется). Смена `.csproj`/`.sln`/`Directory.Build.props` сбрасывает кэш следующего `load_workspace`. Предупреждения restore (`NU1701` TFM-compat, audit, prune) и design-time MSBuild (deprecation `IncludeOpenAPIAnalyzers`/ASPDEPR007, mismatch архитектуры, analyzer без metadata) не валят load даже в обёртке `Msbuild failed when processing the file`; настоящие ошибки MSBuild/SDK (`error NU|MSB|NETSDK`) — валят. Пустой `TargetFramework` (`ResolvePackageAssets`) — отдельный fail: повторить с IDE-конфигом или это Bazel-generated sln, который MSBuildWorkspace не открывает. Нет target `Compile` (outer CrossTargeting) — отдельный fail: повторить с `targetFramework` из отчёта / `Directory.Build.props`; `dotnet build` при этом может быть зелёным. Падение VS 2026 / MSBuild 18 BuildHost (`XMakeElements`) — отдельный fail, **не** `MCP_MSBUILD_SDK_MISMATCH`; нужен MCP 1.0.35+ или один SDK-style `.csproj`.
</details>

<details>
<summary><code>reset_workspace</code> — Сбрасывает in-memory MSBuildWorkspace и кэш решения. После сборки (generated в <code>obj</code>, refs) вызови снова <code>load_workspace</code>. Сохранённые <code>.cs</code> с v1.1.0 подхватываются без reset. CLR-сборки анализаторов <strong>не</strong> выгружает; после same-identity пересборки генератора нужен новый процесс MCP.</summary>

**Параметры:** *(нет)*
</details>

<details>
<summary><code>get_file_content</code> — Возвращает текст с диска в пределах 32 000 символов и 500 строк.</summary>

**Параметры:**
- `filePath: string`

Лимит символов считается в кодовых единицах UTF-16. Границы строк определяются по LF, CRLF и CR. Конечный перевод строки не добавляет отдельную строку. Если первым превышен лимит строк, превью содержит первые 100 строк. Усечённый ответ помечается отдельно. Для диапазона строк используйте `read_file_range`, для части длинной строки — локальный редактор.
</details>

<details>
<summary><code>get_class_skeleton</code> — Возвращает структуру C# файла без тел методов (индекс workspace после saved <code>.cs</code>).</summary>

**Параметры:**
- `filePath: string`
</details>

<details>
<summary><code>get_code_skeleton</code> — Парсит `.cs` с диска и возвращает полный синтаксис файла с вырезанными телами (сигнатуры + пустые блоки); опционально каталог (до 20 файлов, пропуск сегментов пути <code>bin</code>/<code>obj</code>/<code>Test</code>/<code>Tests</code>).</summary>

**Параметры:**
- `path: string` — абсолютный путь к одному файлу `.cs` или к папке для рекурсивного обхода.

**Заметка:** `load_workspace` не требуется. В `lite` включите группу `files` (или используйте `full`), чтобы увидеть этот tool. Для файла из уже загруженного solution по-прежнему уместен `get_class_skeleton`.
**Важно:** этот tool работает только с путём к файлу/папке (`path`). Не передавайте сюда `assemblyName` / `typeName`; для внешних/NuGet-сборок используйте `decompile_type` / `get_decompiled_class_skeleton`.
</details>

<details>
<summary><code>get_method_body</code> — Возвращает исходник первого совпавшего метода из <code>.cs</code> на диске. Выбор перегрузки не делается.</summary>

**Параметры:**
- `filePath: string`
- `className: string`
- `methodName: string`

**Разрешение пути:** `filePath` может быть абсолютным или относительным к workspace. После `load_workspace` путь вида `src/...` считается от каталога загруженного `.sln`/`.slnx`/`.csproj`.
</details>

<details>
<summary><code>get_diagnostics_for_file</code> — Возвращает предупреждения и ошибки компилятора Roslyn для одного файла C#. Сначала подхватывает сохранённую версию <code>.cs</code>.</summary>

**Параметры:**
- `filePath: string`

Для каждой диагностики компилятора выводятся уровень, **строка**, **колонка**, код (например `CS0246`) и сообщение. Передайте код и позицию в `get_code_fixes`.
</details>

<details>
<summary><code>get_code_fixes</code> — Список доступных Roslyn CodeAction для диагностики компилятора в указанной позиции.</summary>

**Параметры:**
- `filePath: string`
- `diagnosticId: string` — из `get_diagnostics_for_file` (например `CS0246`)
- `line: int` — номер строки (1-based), где начинается диагностика
- `column: int = 1` — колонка (1-based, из вывода diagnostics)

**Результат:** пронумерованные фиксы (`fixIndex`, с 0) с заголовками вроде «Add using …», «Implement interface» и т.д. Не более 20 фиксов за вызов.

**Workflow:** `get_diagnostics_for_file` → `get_code_fixes` → `apply_code_fix`. Не придумывайте правку вручную, если Roslyn уже предлагает fix.
</details>

<details>
<summary><code>apply_code_fix</code> — Применяет CodeAction, выбранный из <code>get_code_fixes</code>.</summary>

**Параметры:**
- `filePath: string`
- `diagnosticId: string`
- `fixIndex: int` — индекс из `get_code_fixes` (0-based)
- `line: int` — те же значения, что в `get_code_fixes`
- `column: int = 1`
- `previewOnly: bool = false` — при `true` только diff-превью без записи на диск

**Поведение:** пишет файлы через write boundary и обновляет in-memory workspace. Неподдержанный или stale analyzer-reference diff отклоняется до любой серверной записи. Частичное сохранение — не полный успех: в ответе Status, Reason и известные сохранённые пути. После применения вызовите `get_diagnostics_for_file`, чтобы проверить оставшиеся предупреждения и ошибки компилятора.

**Заметка:** `fixIndex` действителен только для той же пары `filePath` / `diagnosticId` / `line` / `column` в рамках сессии (файл не должен меняться между `get_code_fixes` и `apply_code_fix`).
</details>

<details>
<summary><code>explore_assembly</code> — Декомпилирует подключенную внешнюю сборку (NuGet/сторонний DLL) через ILSpy и возвращает структуру namespaces с видимыми top-level class/interface.</summary>

**Параметры:**
- `assemblyName: string?` — имя без `.dll` (через workspace после `load_workspace`).
- `assemblyPath: string?` — абсолютный путь к `.dll` (например кэш NuGet). Нужен **один** из параметров.
</details>

<details>
<summary><code>decompile_type</code> — Декомпилирует один конкретный тип из подключенной сборки и возвращает C# исходник (circuit breaker: максимум 500 строк).</summary>

**Параметры:**
- `assemblyName: string?` / `assemblyPath: string?` — как у `explore_assembly` (один обязателен).
- `fullTypeName: string` (например `Microsoft.AspNetCore.Mvc.ControllerBase`)

**Поведение:** если результат декомпиляции больше 500 строк, tool возвращает ошибку с рекомендацией использовать `get_decompiled_class_skeleton` и `get_decompiled_method_body`.
</details>

<details>
<summary><code>get_decompiled_class_skeleton</code> — Возвращает только сигнатуры (public/protected fields/properties/methods) для одного типа из подключенной сборки.</summary>

**Параметры:**
- `assemblyName: string?` / `assemblyPath: string?` — как у `explore_assembly` (один обязателен).
- `fullTypeName: string` (например `Microsoft.AspNetCore.Mvc.ControllerBase`)
</details>

<details>
<summary><code>get_decompiled_method_body</code> — Декомпилирует только нужный метод (все совпавшие перегрузки) из одного типа в подключенной сборке.</summary>

**Параметры:**
- `assemblyName: string?` / `assemblyPath: string?` — как у `explore_assembly` (один обязателен).
- `fullTypeName: string` (например `Microsoft.AspNetCore.Mvc.ControllerBase`)
- `methodName: string`
</details>

<details>
<summary><code>find_symbol_references</code> — Ищет использования типа/члена по solution.</summary>

**Параметры:**
- `symbolName: string?` — простое имя или точный FQN (`Namespace.Type` / `Namespace.Type.Member`; без `global::`, без `()`); можно опустить с точными `filePath`+`line`+`column`. Поиск по solution без учёта регистра; при `filePath` без `line` — порядковое сравнение с учётом регистра; для auto-column без `column` имя обязательно
- `filePath: string?` — опциональный `.cs`; без него — поиск по имени/FQN по solution; обязателен с `line`; без `line` — единственное совпавшее объявление в файле (несколько → ошибка)
- `line: int?` — опциональная 1-based строка; требует `filePath`; резолвит символ в этой позиции
- `column: int?` — опциональная 1-based колонка; без неё — единственный identifier token с именем `symbolName` на строке
- `maxResults: int?` — лимит списка (1–500). Пропуск → 50, иначе `ROSLYN_MCP_MAX_RESULTS`, иначе `max-results` из `RoslynMcp.jsonc`; явный аргумент важнее
- `preview: bool? = null` — true: добавить текст строки исходника (до 400 символов). Пропуск → `preview` из `RoslynMcp.jsonc`, иначе только `path:line:col`
- `overflowCursor: string?` — следующий chunk in-memory overflow (новый поиск не запускается)
- `directOnly: bool = false` — при true (нужен `filePath`) для virtual/abstract/override метода класса оставляет только прямые вызовы; по умолчанию false — те же точки и при необходимости одна строка про virtual dispatch. Межпроектные source/metadata типы сравниваются по полному имени.

**Поведение:** файл без строки покрывает class/struct/record/interface/enum/method/ctor/dtor/property/event/field (не operator/indexer/local function). Несколько совпадений → ошибка со списком FQN и `строка:колонка` идентификатора (без тихого первого). Локации сверх `maxResults` хранятся в процессе за cursor (не тихая обрезка; не `%Temp%` на хосте). Неизвестный/истёкший cursor — человекочитаемая ошибка. Фильтр (если включён) применяется после дедупа и до лимита списка / overflow store.
</details>

<details>
<summary><code>find_symbol_definition</code> — Семантический поиск: где объявлен тип или член (файл, строка, колонка, полное имя) в загруженном solution.</summary>

**Параметры:**
- `symbolName: string?` — имя класса, интерфейса, struct, enum или члена (например `IRunCommand`); можно опустить с точными `filePath`+`line`+`column`; в остальных режимах обязательно. Поиск по solution без учёта регистра; файл без строки — с учётом регистра.
- `filePath: string?` — опционально; без него — поиск по имени по solution; без `line` — единственное совпавшее объявление в файле; обязателен вместе с `line` для позиционного go-to-definition
- `line: int?` — опциональная 1-based строка в `filePath` (usage или объявление)
- `column: int?` — опциональная 1-based колонка; без неё — единственный совпадающий identifier token на строке
- `maxResults: int?` — лимит списка (1–500). Пропуск → 50, иначе `ROSLYN_MCP_MAX_RESULTS`, иначе `max-results` из `RoslynMcp.jsonc`; явный аргумент важнее
- `overflowCursor: string?` — следующий chunk in-memory overflow; новый поиск не запускается

**Поведение:** локации сверх `maxResults` хранятся в процессе за cursor (не тихая обрезка; не `%Temp%` на хосте). Неизвестный/истёкший cursor — человекочитаемая ошибка. Режим file+line использует ту же схему cursor.

**Для модели:** после `load_workspace` для «где **объявлен** X?» используй этот tool — не текстовый поиск и не выдуманный tool вроде `search`. Для произвольного текста по файлам — встроенный **`grep`** среды (IDE), не `bash`/PowerShell с grep. Так не лезем в `bin/`/`obj/` и опираемся на Roslyn. **Сохранённые** `.cs` (IDE/git) подмешиваются до поиска; несохранённый буфер игнорируется — `reset_workspace` для обычных save не нужен. Без `filePath`/`line` — поиск по имени по solution; `filePath` без `line` — одно объявление в файле (печатает все исходные места, включая части `partial`, каждое с колонкой и полным именем); несколько совпадений → ошибка с FQN и координатами идентификатора; с `filePath`+`line` — определение символа в этой позиции.

Опциональный `preview: bool?` добавляет строку исходника каждого определения (до 400 символов), включая продолжение за cursor. Явный аргумент важнее `preview` в `RoslynMcp.jsonc`; без настройки — false. С точными `filePath`+`line`+`column` можно опустить `symbolName`.
</details>

<details>
<summary><code>find_usages</code> — Ссылки по всему solution (<code>path:line:col</code>; опционально preview / overflow).</summary>

**Параметры:**
- `symbolName: string` — простое имя или точный FQN (те же правила, что у `find_symbol_references` без `filePath`).
- `maxResults: int?` — лимит списка (1–500). Пропуск → 50, иначе `ROSLYN_MCP_MAX_RESULTS`, иначе `max-results` из `RoslynMcp.jsonc`; явный аргумент важнее
- `preview: bool? = null` — true: добавить текст строки исходника (до 400 символов). Пропуск → `preview` из `RoslynMcp.jsonc`, иначе false
- `overflowCursor: string?` — следующий chunk in-memory overflow

**Поведение:** нужен `load_workspace`. Name-based alias `find_symbol_references` без `filePath`. Сначала saved `.cs` с диска. Все группы объявлений (без выбора primary). FQN-промах перечисляет кандидатов и не падает на простое имя. Перегрузки — одна FQN-группа. Избыток локаций — тот же overflow store.
</details>

<details>
<summary><code>find_implementations</code> — Классы, реализующие интерфейс, или наследники базового типа.</summary>

**Параметры:**
- `symbolName: string` — имя интерфейса или базового класса. Короткое имя возвращает секцию для каждой совпавшей базы. Для выбора одной базы скопируйте её полное имя из секции, например `Demo.IProvider<T>` или `global::IProvider`
- `transitive: bool = true` — при `true` включает косвенные реализации / наследников по иерархии
- `maxResults: int?` — лимит списка (1–500). Пропуск → 50, иначе `ROSLYN_MCP_MAX_RESULTS`, иначе `max-results` из `RoslynMcp.jsonc`; явный аргумент важнее
- `preview: bool? = null` — true: добавить текст строки исходника (до 400 символов). Пропуск → `preview` из `RoslynMcp.jsonc`, иначе только `path:line:col`
- `overflowCursor: string?` — следующий chunk in-memory overflow

**Поведение:** нужен `load_workspace`. Сначала подхватываются сохранённые `.cs`. Для **интерфейсов** вызывается Roslyn `FindImplementationsAsync`, для **классов и структур** — `FindDerivedClassesAsync`. Полное имя без параметров типа выбирает необобщённое объявление, если есть обе формы. Если найдено одно обобщённое объявление, его прежнее имя без параметров тоже работает. При промахе по полному имени инструмент перечисляет кандидатов. Пустые секции сохраняются. Каждая секция выводит свои типы с `path:line:col`, итог считает уникальные типы по всем секциям. Оставшиеся строки доступны через `overflowCursor`. Не используйте текстовый поиск или `find_usages` для «кто реализует X?» / «кто наследует Y?».

**Для модели:** после `load_workspace` — вместо grep или анализа usages, когда нужна OOP-иерархия.
</details>

<details>
<summary><code>get_call_graph</code> — Кто вызывает метод и что вызывает он (call graph).</summary>

**Параметры:** `filePath`, `className`, `methodName`, `maxNodes: int = 25`, `includeExternalCallees: bool = false`, `line: int?`, `column: int?` (строка и колонка только вместе)

Нужен `load_workspace`. Без позиции несколько одноимённых методов в классе — ошибка (FQN + line:column), не первый. С `line`+`column` — объявление или место вызова. Для расследования багов — вместо массовой загрузки тел через `get_method_body`.

</details>

### File Editing

<details>
<summary><code>update_file_content</code> — Полная перезапись файла. Автоматически создает отсутствующие каталоги. Новый <code>.cs</code> пишется на диск и помечает граф проекта как composition-stale; в семантический снимок он не попадает, пока не вызвать <code>load_workspace</code> (устаревший граф пропускает кэш загрузки) или <code>reset_workspace</code>, затем <code>load_workspace</code>.</summary>

**Параметры:**
- `filePath: string`
- `content: string`
</details>

<details>
<summary><code>add_using</code> — Добавить using через Roslyn AST.</summary>

**Параметры:**
- `filePath: string`
- `namespaceName: string` — например `System.Linq`

Требует `load_workspace`. Для импортов — вместо `apply_patch`.

</details>

<details>
<summary><code>add_method_to_class</code> — Вставить метод в класс через Roslyn AST.</summary>

**Параметры:**
- `filePath: string`
- `className: string` — top-level класс
- `methodSource: string` — объявление метода (модификаторы, сигнатура, тело)

Парсит C#, вставляет через DocumentEditor, форматирует. Для новых методов — вместо `apply_patch`.

</details>

<details>
<summary><code>update_method_body</code> — Заменить тело метода через Roslyn AST.</summary>

**Параметры:**
- `filePath: string` — абсолютный путь к `.cs`
- `className: string` — класс (top-level или вложенный)
- `methodName: string`
- `newBody: string` — только statements или блок `{ ... }`
- `parameterTypes: string[]?` — если массив передан, сигнатура должна совпадать, например `["string", "int"]`. Для метода без параметров передайте `[]`. Поле можно опустить, если имя метода однозначно.

**Поведение:** находит метод, парсит тело в `BlockSyntax`, заменяет block/expression body, проверяет syntax errors до записи, форматирует. Пара с `get_method_body` — вместо `apply_patch` для правок только тела.

</details>

<details>
<summary><code>apply_patch</code> — Инструмент точечного поиска-замены по фрагменту.</summary>

**Параметры:**
- `filePath: string`
- `oldString: string`
- `newString: string`
- `replaceAll: bool = false`

**Поведение:** `replaceAll` продолжает поиск после вставленного `newString` (замену повторно не сканирует). Безопасно, если `newString` содержит `oldString`. В лог пишутся start/match/write и время.
</details>

### Build / Test / CLI

<details>
<summary><code>estimate_affected_tests</code> — Приблизительная оценка затронутых тестов с момента загрузки solution.</summary>

Во время захвата baseline исходников инструмент сообщает, сколько документов ожидают захвата. Повторите вызов после завершения захвата. При `run: true` ответ о незавершённом захвате возвращается до запуска сборки и тестов. Документы с неизвестным baseline по-прежнему участвуют в консервативном выборе и учитываются отдельно в `Not covered`. Объём ответа ограничен 32 КиБ текста UTF-8.

Нужен `load_workspace`. Инструмент синхронизирует сохранённые правки на диске и через MCP и показывает изменения с момента загрузки. `depth` (без учёта регистра) выбирает накопительную ступень: `changed` (изменённые и новые тестовые классы), `direct` (также ссылки между типами), `di` (также обнаружение типов контейнером и фреймворком), `all` (также загрузка сборок по имени, по умолчанию). Для суженного проекта ответ показывает число классов на каждой из четырёх ступеней и фильтр VSTest выбранной ступени. `includeDetails: true` перечисляет классы по ступеням и изменившиеся типы из сборок. В ответ также входят ошибки в изменённых исходниках и ограничения `Not covered`. Ошибки компиляции уровня проекта, например конфликт точек входа, смотрите в выводе сборки.

Если класс или метод известен, используйте `run_specific_test`. Для сохранённой правки с широким или неочевидным охватом вызовите `estimate_affected_tests` с `run: true`, чтобы собрать и запустить выбранные тесты одним вызовом. Передайте `run: false`, чтобы посмотреть план или использовать свежую сборку. На свежих выходах запускайте возвращённые фильтры через `run_test_by_filter`, целые проекты через `run_dotnet_test` с `noBuild: true`. На двух правках полного Jellyfin автоматический режим сократил цикл на 13,85–18,10 с относительно `maxParallelProjects: 1` при одинаковых тестах и исходах. [Замер всего цикла](rapid-tia-2/mvp/evidence/parallel-run-2026-11-19.md).

Начальный выбор строится по сохранённым входам и зависимостям проектов. Для сужения используются отпечатки скомпилированных типов и графы зависимостей до и после правки. Baseline сохраняется при reload или reset того же solution. Другой solution или перезапуск сервера начинает новый baseline. Когда все ступени пусты, ответ различает неизменившийся код в сборках и изменившиеся типы, до которых не дошёл ни один тестовый класс.

Если исходный план выбрал целый проект, ответ сохраняет этот объём и называет причину, когда нет снимка до правки, сборку перезаписали до окончания её первого чтения, сборку нельзя прочитать, выход не новее последней правки или тестовая сборка устарела после изменения формы зависимости. Проект также остаётся целым при изменении не-C# входа или файла проекта, устаревшем графе проектов, разрыве наблюдения, неизвестном имени тестового класса для VSTest или превышении бюджета фильтра. В режиме только оценки при устаревших сборках инструмент предлагает собрать и повторить запрос.

Граф может пропустить типы, названные строкой или в конфигурации, и зависимости, которые проходят через другой процесс, запущенный тестом. Сужение рассматривает только тестовые проекты из исходного выбора на уровне проектов. Читаются сборки для целевых платформ загруженного workspace. Правка, которая влияет только на другую платформу, может выглядеть как неизменившийся код в сборках.

По умолчанию `run: false`. С `run: true` инструмент последовательно собирает выбранные проекты с конфигурацией, платформой и аргументами из `load_workspace`, сужает выбор по свежим сборкам, затем запускает VSTest без повторной сборки. `maxParallelProjects: 0` (по умолчанию) берёт половину логических процессоров с округлением вниз, минимум один и максимум по числу проектов с тестами к запуску. Значение больше одного задаёт предел. Если проекты делят порт, файл или базу, передайте `1` для чередования сборки, сужения и тестов по каждому проекту. У каждого тестового процесса свой временный каталог результатов, который удаляется после обработки. В ответе проекты идут в порядке пути, а строка режима показывает степень параллелизма тестов.

При пустом выборе тестовый процесс пропускается с исходом `not run`. Если сужение после успешной сборки упало, проект запускается целиком, а ответ называет ошибку. Правка только комментария проверяется лишь при `run: true`: оценка сохраняет сравнение синтаксиса, а запуск сравнивает скомпилированный код. Число изменившихся типов при запуске относится к проектам, собранным в этом запуске. `timeoutSeconds` (по умолчанию 300, 0 отключает) применяется отдельно к каждому процессу сборки или тестов. При ошибке или таймауте остальные проекты продолжают работу. Отмена MCP завершает активные деревья процессов, пропускает проекты в очереди и записывает исходы в лог.

**Выход сборки:** изменения определяются по написанным входам (исходники, файлы проектов, входы evaluation, данные и сборки, которые лежат в репозитории), а не по продуктам сборки. Пересборка проекта, в том числе внутри `run`, `run_*` или IDE, не расширяет следующую оценку, а откат правки может вернуть пустой план. Проект, который ссылается на выход другого проекта solution как на файл сборки (metadata reference), вне графа проектов, по-прежнему выбирается при изменении этого выхода; проект, который подключает его только как анализатор, — нет.

Фильтры проверяют вхождение имени: выбор `Check` может запустить и `CheckExtra`, и все строки параметризованного теста. Runtime-имена вложенных/унаследованных/generic тестов берутся из измеренных VSTest-форм; неизвестные формы и NUnit-фикстуры с constructor/source arguments дают проект целиком. MTP-проекты отдельно не распознаются и идут существующим VSTest-путём; отдельный MTP-план отложен. Reflection, соглашения, внешние данные/процессы, source generators и тесты, читающие исходники, могут остаться вне приближения; наблюдение входов и синтаксическое распознавание тестовых ролей имеют ограничения. Обязательные проверки проекта (например `SourceStructure`) запускаются отдельно. Полный suite и CI обязательны перед приёмкой или коммитом.
</details>

<details>
<summary><code>run_dotnet_build</code> — Запускает dotnet build и возвращает компактную сводку.</summary>

**Параметры:**
- `workspacePath: string` — только существующий **файл** `.csproj`, `.sln` или `.slnx` (не каталог).
- `configuration: string? = null` — опционально `-p:Configuration=` (например `Sit-Debug`, `Dit-Debug`). Если не задан — берётся с `load_workspace`.
- `noIncremental: bool = true` — `--no-incremental` на каждом build-шаге (по умолчанию). `false` только если явно принимаете up-to-date кэш MSBuild.
- `platform: string? = null` — опционально `-p:Platform=`. Если не задан — с `load_workspace`.
- `projectName: string? = null` — опционально. Если задан, `workspacePath` должен быть `.sln`/`.slnx`. Собирает этот проект через MSBuild-таргет виртуального пути в solution (`-t:"Folder\Project"`). Совпадение по display name, имени файла или виртуальному пути.
- `timeoutSeconds: int? = null` — по умолчанию 180 секунд на шаг и 300 секунд на всю операцию. Значение от 1 до 4 294 967 задаёт общий бюджет для шагов сборки и восстановления пакетов. `0` отключает таймаут инструмента. Отмена клиентом и клиентский таймаут `tools/call` продолжают действовать.
- `reportCursor: string? = null` — следующий фрагмент сохранённого вывода процесса. Новую сборку не запускает.
- `includeBuildWarnings: bool = false` — показывать предупреждения сборки; по умолчанию скрыты. Ошибки выводятся всегда.

**Поведение:** использует SDK, который `dotnet` выбирает для рабочего каталога с учётом `rollForward` из `global.json`. Унаследованные переопределения MSBuild сбрасываются. Сервер применяет путь SDK, когда его удалось подтвердить. Если фактический путь MSBuild расходится с разрешённым, возможны `MCP_MSBUILD_SDK_MISMATCH` и повторный restore через выбранный `MSBuild.dll`. Диагностика показывает запрошенный и выбранный SDK.

Цепочка шагов — минимальная сборка, restore через выбранный SDK, подробный restore или build при необходимости. Итоговый код выхода берётся с последнего шага сборки. В контексте указаны `Configuration`, `Platform`, `ProjectName`, `SolutionTarget`, `BuildArgs` и `NoIncremental`. Session `buildArgs` из `load_workspace` применяются на шагах сборки. Сервер запускает сборку на каждый запрос. Уведомления MCP о прогрессе показывают шаг, время и предыдущий код выхода. Таймаут клиента продолжает действовать.

**Вывод:** успешная сборка возвращает `Build succeeded` и предупреждения по запросу. `Execution context` появляется при ошибке, таймауте или неподтверждённом результате тестов. Через `reportCursor` доступен сохранённый вывод, включая скрытые предупреждения. Сервер дочитывает оба потока и хранит не более 240 000 символов на поток. При усечении сохраняются целые строки из начала и хвоста, а также отдельные диагностические строки. Слишком длинная строка может быть пропущена.

</details>

<details>
<summary><code>run_dotnet_test</code> — Запускает dotnet test с сокращенным выводом ошибок.</summary>

**Параметры:**
- `workspacePath: string`
- `timeoutSeconds: int = 300` — таймаут процесса; `0` отключает (не рекомендуется). Для долгих интеграционных поднимайте (900/1800).
- `noBuild: bool? = null` — без `binariesPath` пропуск значит false. С `binariesPath` `false` — это `always`, `true` — `never`; вместе с `buildPolicy` не передавать.
- `buildPolicy: string? = null` — `auto`, `always` или `never`. Нужен `binariesPath`. Если не переданы оба параметра, DLL-маршрут берёт `auto`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (например `Sit-Debug`). Если не задан — с `load_workspace`.
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — каталог bin с `{AssemblyName}.dll`. Нужен loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. При `noBuild=false` сначала `dotnet build <sln> -t`, затем `dotnet test` по DLL. При `noBuild=true` DLL уже должна лежать в этом каталоге.
- `includeFullOutput: bool = false` — полный Standard Output/Error упавшего теста до 100000 символов на поток. По умолчанию `false` — бюджет `maxOutputChars`.
- `maxOutputChars: int = 0` — бюджет StdOut на один failed test. `0` = 2500 (голова 1600 + хвост 700), или 100000 при `includeFullOutput`. Потолок 100000. StdErr масштабируется.
- `reportCursor: string? = null` — следующий фрагмент сохранённого вывода процесса. Новую сборку или тест не запускает.
- `includeBuildWarnings: bool = false` — показывать предупреждения сборки; по умолчанию скрыты. Ошибки выводятся всегда.

**Поведение:** при `noBuild=false` и без `binariesPath` сначала отдельный incremental `dotnet build` (те же `-c` / platform / session `buildArgs` с `load_workspace`), затем `dotnet test --no-build --no-restore` (парсер видит только тест). С `binariesPath` сборка идёт через solution `-t`, цель теста — DLL. Сводка из `Passed!`, `Test Run Successful` + `Total tests`/`Passed:` (Failed=0 если нет строки), или `.slnx` fail-only `Total tests` + `Failed:` (Passed = Total − Failed − Skipped), FQN-строки тестов (`[ms]` / `[1 s]` / `[1 m 28 s]`); `Error Message:` многострочный (head+tail); StdOut/StdErr отдельными блоками (по умолчанию 2500/1000, head+tail; `includeFullOutput` / `maxOutputChars` поднимают лимит); футер VSTest `Build FAILED` / `0 Error(s)` не считается ошибкой компиляции; дедуп NU audit. Без маркеров сводки при exit 0 → **partial** + 2KB лога (футер срезан). `timeoutSeconds` — общий бюджет на build+test. Рядом с DLL нужны `.runtimeconfig.json` / `.deps.json`. Пока шаг выполняется, идут MCP progress-heartbeat'ы (`dotnet build` / `dotnet test`, без stdout) — UX, не обход хост-таймаута `tools/call`.

**Вывод:** успешная сборка возвращает `Build succeeded` и предупреждения по запросу. `Execution context` появляется при ошибке, таймауте или неподтверждённом результате тестов. Через `reportCursor` доступен сохранённый вывод, включая скрытые предупреждения. Сервер дочитывает оба потока и хранит не более 240 000 символов на поток. При усечении сохраняются целые строки из начала и хвоста, а также отдельные диагностические строки. Слишком длинная строка может быть пропущена.

</details>

<details>
<summary><code>run_specific_test</code> — dotnet test с фильтром по классу и/или методу.</summary>

**Параметры:**
- `workspacePath: string`
- `className: string?` — например `UserServiceTests`
- `methodName: string?` — например `CreateUser_WhenValid_ReturnsOk`
- `timeoutSeconds: int = 300` — как у `run_dotnet_test`.
- `noBuild: bool? = null` — без `binariesPath` пропуск значит false. С `binariesPath` `false` — это `always`, `true` — `never`; вместе с `buildPolicy` не передавать.
- `buildPolicy: string? = null` — та же политика DLL-маршрута, что у `run_dotnet_test`. Нужен `binariesPath`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c` (как у `run_dotnet_test`).
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — как у `run_dotnet_test`: каталог bin с `{AssemblyName}.dll`; loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. `noBuild=false` собирает через solution `-t`.
- `includeFullOutput: bool = false` — как у `run_dotnet_test`.
- `maxOutputChars: int = 0` — как у `run_dotnet_test`.
- `reportCursor: string? = null` — следующий фрагмент сохранённого вывода процесса. Новую сборку или тест не запускает.
- `includeBuildWarnings: bool = false` — показывать предупреждения сборки; по умолчанию скрыты. Ошибки выводятся всегда.

Нужен хотя бы один из `className` / `methodName`. Tool строит VSTest-safe `--filter` (`FullyQualifiedName~…`, без `()` у метода, без лишней ведущей `.` на dotted FQN). После `load_workspace` Roslyn по возможности резолвит FQN типа/метода. Строка `Passed` только с именем метода или `Method(args)` — это успех, не **no matching tests**.

**Для модели:** TDD/фикс бага — этот tool, не полный suite. Сырой VSTest `--filter` (`TestCategory=Smoke`, `FullyQualifiedName~…`) — `run_test_by_filter`. Предпочитайте `className` + короткое `methodName`. После билда предпочитайте `noBuild=true`. При `noBuild=false` — тот же split build/test, что у `run_dotnet_test`. Проекты, которые собираются только внутри своего `.sln`, передавайте с `binariesPath` и `.csproj`.

**Вывод:** успешная сборка возвращает `Build succeeded` и предупреждения по запросу. `Execution context` появляется при ошибке, таймауте или неподтверждённом результате тестов. Через `reportCursor` доступен сохранённый вывод, включая скрытые предупреждения. Сервер дочитывает оба потока и хранит не более 240 000 символов на поток. При усечении сохраняются целые строки из начала и хвоста, а также отдельные диагностические строки. Слишком длинная строка может быть пропущена.

</details>

<details>
<summary><code>run_test_by_filter</code> — dotnet test с сырым VSTest <code>--filter</code>.</summary>

**Параметры:**
- `workspacePath: string` — `.csproj`, `.sln`, `.slnx` или каталог тестового проекта (как у `run_dotnet_test`).
- `filter: string` — передаётся в `--filter` (например `FullyQualifiedName~MyClass`, `FullyQualifiedName~CreateUser`, `TestCategory=Smoke`). Пустой — ошибка. Не ставьте `()` у метода.
- `timeoutSeconds: int = 300` — как у `run_dotnet_test`.
- `noBuild: bool? = null` — без `binariesPath` пропуск значит true (по умолчанию **true**, в отличие от остальных test tools). С `binariesPath` `false` — это `always`, `true` — `never`; вместе с `buildPolicy` не передавать.
- `buildPolicy: string? = null` — та же политика DLL-маршрута, что у `run_dotnet_test`. Нужен `binariesPath`.
- `noRestore: bool = false` — `--no-restore`.
- `configuration: string? = null` — опционально `dotnet test -c`. Если не задан — с `load_workspace`.
- `platform: string? = null` — опционально `-p:Platform=`.
- `binariesPath: string? = null` — каталог bin с test DLL. Нужен loaded `.sln`/`.slnx` и `workspacePath` на `.csproj`. Запускает `{AssemblyName}.dll` из этого каталога. При `noBuild=false` сначала solution `-t`; при `noBuild=true` DLL уже должна существовать.
- `includeFullOutput: bool = false` — как у `run_dotnet_test`.
- `maxOutputChars: int = 0` — как у `run_dotnet_test`.
- `reportCursor: string? = null` — следующий фрагмент сохранённого вывода процесса. Новую сборку или тест не запускает.
- `includeBuildWarnings: bool = false` — показывать предупреждения сборки; по умолчанию скрыты. Ошибки выводятся всегда.

**Поведение:** тот же парсер VSTest и split build/test, что у `run_dotnet_test`, если `binariesPath` не задан и `noBuild=false`. Не проверяет, что needle фильтра есть в FQN теста (для `TestCategory` это дало бы ложный no-match). Для одного класса/метода предпочитайте `run_specific_test`.

**Вывод:** успешная сборка возвращает `Build succeeded` и предупреждения по запросу. `Execution context` появляется при ошибке, таймауте или неподтверждённом результате тестов. Через `reportCursor` доступен сохранённый вывод, включая скрытые предупреждения. Сервер дочитывает оба потока и хранит не более 240 000 символов на поток. При усечении сохраняются целые строки из начала и хвоста, а также отдельные диагностические строки. Слишком длинная строка может быть пропущена.

</details>

<details>
<summary><code>get_test_list</code> — Список тестов в solution (JSON). Сначала saved <code>.cs</code> с диска.</summary>

**Параметры:**
- `maxResults: int = 200` (кламп 1–500)
- `projectName: string? = null` — опционально. Только этот проект в загруженном workspace (точное совпадение без учёта регистра: display name, имя файла без расширения, assembly). Нет совпадения или неоднозначность — список проектов, не пустой JSON.
- `nameContains: string? = null` — опционально. Подстрока без учёта регистра по VSTest FQN (`Namespace.Class.Method`), имени класса или метода.

**Поведение:** Fact/Theory/TestMethod/и т.д. Нужен `load_workspace`. Фильтры применяются **до** `maxResults`. В каждом элементе есть `projectName`. Без фильтров `count: 0` — подсказка, что загружен не тот `.csproj`; с фильтрами `count: 0` — нет совпадений (сначала ослабьте фильтр).

</details>

<details>
<summary><code>generate_test_method_stub</code> — Вставить заглушку тестового метода.</summary>

**Параметры:** `filePath`, `className`, `methodName`, `testFramework: string?` — `xunit` / `nunit` / `mstest`

</details>

<details>
<summary><code>list_nuget_packages</code> — Установленные NuGet-пакеты (JSON по проектам).</summary>

**Параметры:**
- `workspacePath: string` — `.sln`, `.slnx`, `.csproj` или каталог
- `includeTransitive: bool` — по умолчанию `true`
- `includeOutdated: bool` — по умолчанию `false` (`--outdated`)
- `includeVulnerable: bool` — по умолчанию `false` (`--vulnerable`)

**Для модели:** смотреть дерево зависимостей до правок `.csproj`; не `execute_dotnet_command` для списка пакетов.

</details>

<details>
<summary><code>list_outdated_packages</code> — Устаревшие NuGet-пакеты (JSON).</summary>

**Параметры:** `workspacePath` — shortcut для `list_nuget_packages` с `includeOutdated=true`.

</details>

<details>
<summary><code>run_nuget_audit</code> — Аудит уязвимостей NuGet (<code>dotnet list package --vulnerable --include-transitive</code>). Это не ошибки компиляции из <code>run_dotnet_build</code>.</summary>

**Параметры:**
- `workspacePath: string` — `.sln`, `.slnx`, `.csproj` или каталог
- `maxEntries: int = 40` — максимум строк уязвимостей в таблице

</details>

<details>
<summary><code>add_package_reference</code> — Добавить PackageReference в .csproj.</summary>

**Параметры:** `projectPath`, `packageId`, `version?`. Сначала `search_nuget_registry`. После — `load_workspace`.

</details>

<details>
<summary><code>remove_package_reference</code> — Удалить PackageReference из .csproj.</summary>

**Параметры:** `projectPath`, `packageId`

</details>

<details>
<summary><code>search_nuget_registry</code> — Поиск пакета и последней стабильной версии на NuGet.</summary>

**Параметры:**
- `query: string` — id или поисковый термин
- `exactMatch: bool` — по умолчанию `true`
- `maxResults: int` — при `exactMatch=false`, по умолчанию 10

**Для модели:** проверять id/версию перед `dotnet add package`; не выдумывать названия пакетов.

</details>

<details>
<summary><code>run_dotnet_run</code> — Собрать и запустить исполняемый проект.</summary>

**Параметры:** `workspacePath` (.csproj), `arguments`, `workingDirectory`, `timeoutSeconds=120`, `maxStdoutChars=8000`, `maxStderrChars=2000`, `reportCursor`, `includeBuildWarnings=false`.

**Поведение:** сначала сборка, затем `dotnet run --no-build --project` в пределах общего таймаута. Предупреждения сборки скрыты по умолчанию; при включении удаляются дубли и ограничивается объём. stdout/stderr приложения сохраняются с прежними лимитами фрагментов. `Execution context` выводится при ошибке или таймауте; успешный запуск обходится без шапки. Неуспешная сборка останавливает запуск приложения. `reportCursor` читает сохранённый вывод без повторного запуска процессов. Сервер дочитывает оба потока и хранит не более 240 000 символов на поток. При усечении сохраняются целые строки из начала и хвоста, а также отдельные диагностические строки. Слишком длинная строка может быть пропущена.

</details>

<details>
<summary><code>execute_dotnet_command</code> — Запускает dotnet {command} в указанной директории.</summary>

**Параметры:**
- `command: string`
- `workingDirectory: string?`
- `timeoutSeconds: int = 300` — таймаут процесса; `0` отключает
- `includeBuildWarnings: bool = false` — показывать предупреждения сборки; по умолчанию скрыты. Ошибки выводятся всегда.
- `reportCursor: string? = null` — следующий фрагмент сохранённого отчёта сборки. Команду заново не запускает.

**Вывод сборки:** для явной команды `build` действуют silent-режим и контекст запуска только при неуспехе. Остальные команды сохраняют сырой stdout/stderr.

</details>

<details>
<summary><code>get_changed_files</code> — Список изменённых и неотслеживаемых путей git. Diff не возвращает.</summary>

**Параметры:**
- `workspacePath: string? = null` — `.sln`, `.slnx`, `.csproj` или каталог репозитория. Без аргумента — загруженный workspace, затем каталог процесса.

`git` ограничен 180 с плюс до 3 с на остановку процесса. Тело diff — через `git diff` хоста.

</details>

<details>
<summary><code>run_format</code> — Запускает dotnet format (применение или verify-only). Отформатированные <code>.cs</code> подхватывает следующий поиск символов без reset.</summary>

**Параметры:**
- `workspacePath: string`
- `verifyOnly: bool = false`
</details>

### Filesystem Utility & Search

<details>
<summary><code>list_directory_tree</code> — Строит дерево файлов и директорий (исключая bin, obj, .git, .vs).</summary>

**Параметры:**
- `directoryPath: string`
- `maxDepth: int = 2`
</details>

<details>
<summary><code>search_code</code> — Текстовый поиск (по умолчанию управляемый обход; опционально ripgrep).</summary>

**Параметры:**
- `pattern: string`
- `directoryPath: string? = null`
- `includeExtensions: string? = ".cs"` — список через запятую/`;` (`.cs,.csproj,.json`) или `*` для всех файлов.
- `useRegex: bool = false` — .NET regex для управляемого обхода; синтаксис ripgrep (Rust) при `useRipgrep: true`.
- `caseSensitive: bool = false` — по умолчанию без учёта регистра; для leftover branding — `true`.
- `maxResults: int? = null` — пропуск → 50, иначе `ROSLYN_MCP_MAX_RESULTS`, иначе `max-results` из `RoslynMcp.jsonc`; явный аргумент важнее
- `maxScanSeconds: int = 20`
- `useRipgrep: bool = false` — при `true` только `rg` (без fallback на управляемый обход). Отсутствие `rg` — ошибка только с этим флагом.
- `ripgrepPath: string? = null` — полный путь к `rg`/`rg.exe`; учитывается только с `useRipgrep=true` (иначе игнорируется). Без аргумента — `ripgrep-path` из `RoslynMcp.jsonc` или PATH.

**Для агента:** если в клиенте есть встроенный **`grep`**, для обычного текстового поиска предпочитай его (не grep из терминала). Этот MCP-tool — когда нужен поиск из процесса Roslyn MCP. Не предполагай, что ripgrep установлен.
</details>

<details>
<summary><code>read_file_range</code> — Читает конкретный диапазон строк из файла с их исходными номерами.</summary>

**Параметры:**
- `filePath: string`
- `startLine: int` (1-based)
- `lineCount: int`
</details>

<details>
<summary><code>read_log_tail</code> — Читает конец лог-файла с опциональной фильтрацией по ключевому слову.</summary>

**Параметры:**
- `filePath: string? = null` — не указывайте, чтобы читать последний лог MCP (`logs/mcp-*.log`, как `tail_tool_log`).
- `lastNLines: int = 200`
- `filterKeyword: string? = null` — например `Failure`, `ERR`, `NU1903`.

**Совет:** для диагностики MCP удобнее `tail_tool_log` и `filterKeyword`.
</details>

<details>
<summary><code>tail_tool_log</code> — Читает последний лог сервера `logs/mcp-*.log`.</summary>

**Параметры:**
- `lastNLines: int = 200`
- `filterKeyword: string? = null`
</details>

<details>
<summary><code>manage_agent_scratchpad</code> — Управляет долговременной памятью агента между сессиями.</summary>

**Параметры:**
- `action: string` (`read` | `write` | `append` | `clear`)
- `content: string? = null`
</details>

### Roslyn Refactoring / Solution Insights

<details>
<summary><code>rename_symbol</code> — Семантический rename C# символа через Roslyn с предпросмотром.</summary>

**Параметры:**
- `filePath: string`
- `symbolName: string`
- `newName: string`
- `scope: string = "project"` — допустимо: `project` | `solution`
- `previewOnly: bool = true`
- `line: int?` — необязательная 1-based строка; только вместе с `column`
- `column: int?` — необязательная 1-based колонка; только вместе с `line`

**Область:** только C# символы. Для папки проекта / `.csproj` / графа solution — `rename_project`. Для README/rules/URL — host Grep/edit.

**Поведение:** без позиции несколько одноимённых объявлений в файле — ошибка (FQN + line:column), запись на диск не выполняется. С `line`+`column` — объявление или использование в этом файле. Сохранение идёт через тот же write boundary, что и `apply_code_fix`. Неподдержанный analyzer-reference diff отклоняется до записи. Частичное сохранение сообщает Status, Reason и известные пути — это не полный успех rename. Предпросмотр по-прежнему по умолчанию.
</details>

<details>
<summary><code>rename_project</code> — Переименование SDK-style проекта (папка + `.csproj`) и правка MSBuild-графа.</summary>

**Параметры:**
- `projectPath: string` — путь к `.csproj`
- `newProjectName: string` — один сегмент пути (например `DupFinder.Core`)
- `dryRun: bool = true`
- `searchRoot: string? = null` — корень поиска соседних проектов / `.sln` / `.slnx`

**Поведение:** переносит одноимённый каталог SDK-style проекта и переименовывает его `.csproj`. Предпросмотр показывает конечные пути правок `.csproj`, `.fsproj`, `.vbproj`, `.sln` и `.slnx`, включая файлы внутри переносимого каталога. `searchRoot` задаёт область поиска дополнительных файлов. Если каталог проекта является корнем Git checkout, целевой путь занят или в проверяемом проекте есть вычисляемый `ProjectReference`, планирование возвращает отказ. При записи сохраняется кодировка UTF-8 с BOM или без него, а также UTF-16 и UTF-32 с BOM. Кодировка в объявлении XML должна соответствовать кодировке файла. Для UTF-32 с порядком байтов big-endian в объявлении требуется `utf-32BE`. Обновляет `AssemblyName` и `RootNamespace`, только если они совпадают со старым именем проекта. Не трогает C# namespace/типы, Docker, launchSettings, CI, docs. После apply: `load_workspace` → `run_dotnet_build`.
</details>

<details>
<summary><code>list_projects</code> — Показывает проекты текущего workspace.</summary>

**Поведение:** Показывает целевой фреймворк и тип выходной сборки каждого загруженного экземпляра проекта с учётом импортов и условий. При явном `workspacePath` возвращает проекты из этой загрузки. Если оценённое значение недоступно, инструмент показывает `(unknown)`. Это относится и к типу выходной сборки при загрузке через старый BuildHost, который его не сообщает.

**Параметры:**
- `workspacePath: string? = null`
</details>

<details>
<summary><code>get_project_graph</code> — Строит граф зависимостей project-to-project.</summary>

**Параметры:**
- `workspacePath: string? = null`
</details>

### Жизненный цикл сервера

<details>
<summary><code>get_mcp_server_info</code> — Путь к exe, число tools, логи, состояние workspace.</summary>

**Параметры:** *(нет)*

После `dotnet publish` — проверка, что MCP подхватил новый бинарник. Ожидайте `Version` из `RoslynMcpServer.csproj`, **55** tools в `full` или **16** в `lite`.
В отчёте видны рабочий набор памяти процесса сервера и число успешных физических загрузок workspace с момента запуска. Счётчик сохраняется при попадании в кэш и после `reset_workspace`.

</details>

<details>
<summary><code>list_tool_groups</code> — Группы: назначение, active/inactive, состав.</summary>

**Параметры:** *(нет)*

Группы не включает. Для включения — `enable_tool_group` или рестарт с `ROSLYN_MCP_TOOL_GROUPS`. Markdown; `tools/list` остаётся JSON Schema.

</details>

<details>
<summary><code>get_tool_help</code> — Markdown-справка по одному тулу (kind, group, живые параметры).</summary>

**Параметры:**
- `toolName: string` — точное публичное имя (например `find_usages`). Неизвестные имена — близкие совпадения.

</details>

<details>
<summary><code>enable_tool_group</code> — Добавляет все недостающие тулы одной группы в эту сессию.</summary>

**Параметры:**
- `group: string` — точное имя группы, без учёта регистра (`core`, `files`, `navigation`, `editing`, `decompile`, `nuget`, `project`, `runtime`, `operations`).

Идемпотентно. Профиль `full` — no-op. `tools/list_changed` только при реальном добавлении. Если клиент не обновляет список: рестарт с `ROSLYN_MCP_TOOL_GROUPS=<group>`. Disable в этом релизе нет.

</details>

<details>
<summary><code>stop_mcp_server</code> — Завершает процесс MCP после ответа (чтобы пересобрать бинарник сервера; затем перезапуск MCP в IDE). Не для refresh сохранённых исходников — они синхронизируются сами.</summary>

**Параметры:** *(нет)*

</details>

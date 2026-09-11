# Эпоха 3 — Контракт загрузчика и зависимости

Статус: **принято** (U-ARB-02: restart-required; U-ARB-03: main-only + явный отказ).
Зависимости: эпохи 1–2.
Приёмка: [epoch-3-acceptance.md](epoch-3-acceptance.md).
Прогон: [epoch-3-results.md](epoch-3-results.md).

## E3-S1. Проверка выполнимости и выбор режима

Проверенное content-addressed поколение устанавливает идентичность подготовленных
байтов; один новый shadow path не доказывает ни их проверку, ни выбранную CLR assembly.
Текущий минимальный loader использует `Assembly.LoadFrom` и живёт дольше workspace.

Production oracle эпохи 1 на .NET 10 / Roslyn 5.9.0 (и повтор в этой эпохе):

| Операция | Подготовка / generation | Loaded path | Exact marker |
| --- | --- | --- | --- |
| V1→V2 cached load | новый hash, overlay не активируется как V2 | старое поколение остаётся в CLR | V2 **не** исполняется |
| V1→V2 reset+load | новый session mapping не даёт новую CLR identity | loader процесса сохраняется | V2 **не** исполняется |
| V1→V2 process restart | новое поколение в новом процессе | новый shadow | точный **V2** |
| A→B same identity | mapping A не переносится | identity уже в процессе | маркер A не выдаётся за B |

Выбранный контракт U-ARB-02: **restart-required**.

Поддержанные операции: первый opt-in load main-only генератора в процессе,
где эта assembly identity ещё не загружена; process restart + load для новой
версии с той же identity. Неподдержанный in-process refresh (cached load и
reset+load при уже загруженной identity) отклоняется до исполнения с
диагностикой и действием «restart the MCP server process». После restart
проверяется exact V2.

Неудача эксперимента не выбирала restart автоматически: in-process update
не подтверждён, hot reload не требуется, restart-required — окончательный
режим этой реализации. Нельзя молча возвращать known-stale/wrong semantics
или исполнять сборку другого решения.

## E3-S2. Подготовка и discovery зависимостей

Fixture с явным main DLL + private helper DLL проверен. Разделены: отсутствие
подготовленного файла, binding/dependency refusal и execution result.

Production источник полного private runtime-набора не установлен. По U-ARB-03
поддержка ограничена **main-only**: AssemblyRef вне точного
`AnalyzerHostContractCatalog` — отказ. Копирование всех `*.dll` output с
blacklist не используется. Private DLL из реального build output не являются
fallback. Helper-only change не дополняет main-only поколение и не считается
успешной генерацией.

Конфликтующие версии одноимённой helper отклоняются тем же main-only
отказом (`DependencyUnsupported`), до любой версии: requester-scoped
resolution не реализован и отдельный reason не используется.

## E3-S3. Binding и время жизни

`AddDependencyLocation` записывает path/directory/время. Resolve handler
записывает requesting assembly, requested name и исход; **не** берёт первый
`simpleName.dll` из неупорядоченного process-global набора. Конфликтующие
helpers не поддерживаются.

ALC не выбран. Shared host contracts перечислены точно в
`AnalyzerHostContractCatalog` (не маска `System.*`). Generation-private копии
этих контрактов не резолвятся из каталога поколения. Negative test:
положенный рядом `Microsoft.CodeAnalysis.dll` не загружается loader'ом.
Positive: main-only генератор исполняется с host type identity
(`IIncrementalGenerator`).

Владелец loader/resolver — `SolutionManager` (время жизни процесса).
`ClearWorkspaceAsync` не снимает handler и не выгружает CLR assemblies.
Немедленная выгрузка не обещается.

## E3-S4. Состояния и ошибки

Различаются `prepared`, `reference rewritten`, `load failed`,
`execution observed`, плюс `restart-required` / `dependency-unsupported` /
`identity-collision`. Rewrite count в ответе `load_workspace` — только ссылки.
Публичная сводка краткая; внутренний `AnalyzerExecutionObservation` хранит
стадии, project/generator/generation и missing/conflicting dependency.

Loading остаётся lazy. First-use (`ObserveAnalyzerExecution` / compilation)
сопоставляет отказ с project, генератором и причиной. Канал: metadata inspect
до load, исключение `GetAnalyzers`, identity collision в loader; пустые
diagnostics по-прежнему не oracle версии.

## E3-S5. Приёмка и ресурсы

Исследование завершено: evidence эпохи 1 записано, режим выбран явно.
Реализация принимается отдельным прогоном E3-S5.

Нативные зависимости и отдельный процесс исполнения генераторов остаются вне
эпохи.

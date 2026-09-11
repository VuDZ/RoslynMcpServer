# Ответ на review спецификаций analyzer shadow copy

Статус: **оценка критики; изменения спецификаций не внесены**. Дата: 2026-09-11.
Основание кода: commit `3dcd63f7f814e6ac366c409693b34a8492130307`, версия проекта 1.3.5,
.NET 10, Roslyn 5.9.0. Спецификации и review на момент анализа — untracked files.

Это соседний с `review/` каталог. Имена файлов зеркалируют review, исходные ID
сохранены. Каждый verdict оценивает замечание целиком, а не автоматически
принимает его Suggested change. `PARTIALLY ACCEPT` означает, что ниже явно
разделены принятая проблема и непринятое объяснение/решение.

## Полнота и навигация

Прочитаны все четыре исторических документа, README и шесть новых эпох,
все семь файлов review. Всего **42 finding**. Ответы:

- R-01–R-04: этот файл.
- [E1-01–E1-10](epoch-1-lifecycle-verification.md).
- [E2-01–E2-07](epoch-2-immutable-shadow-copies.md).
- [E3-01–E3-06](epoch-3-loader-contract-and-dependencies.md).
- [E4-01–E4-06](epoch-4-workspace-write-boundary.md).
- [E5-01–E5-05](epoch-5-reference-provenance.md).
- [E6-01–E6-04](epoch-6-contract-and-documentation.md).
- [Сводка решений, изменений и арбитража](SUMMARY.md).

Исходный review: [README](../review/README.md). Исходное предложение:
[README](../README.md). В этом проходе нет реализации, новых baseline-результатов
или пересмотра окончательной архитектуры. Выводы по коду отделены от гипотез
о фактической загрузке DLL. Никакие эксперименты с генераторами не запускались.

## Авторитет требований и источники

В исходных требованиях нет формальных requirement IDs или отдельного ADR для
новой серии. Следующие обозначения — **локальные ссылки этого ответа**, а не
вновь введённые требования:

- **U** — исходная беседа: оценить документацию без правок; предложить улучшения;
  оформить эти предложения как спецификации/эпохи. Предложения включали
  эксперимент перед большой переработкой loader и возможность документировать
  ограничения. Пользователь не устанавливал обязательную поддержку hot reload,
  ALC, MVCC, shared cache или автоматической очистки.
- **H1** — [историческая эпоха 1](../../analyzer-shadow-copy/epoch-1-diagnosis-and-mvp.md),
  «Baseline / problem», «Decisions»: неправильный analyzer path и lock корректного
  output — две исходные задачи; disposable external repro, diagnostic-only flag.
- **H2** — [историческая эпоха 2](../../analyzer-shadow-copy/epoch-2-first-fix-attempt-and-disk-corruption.md),
  «The defect», «Compatibility / migration impact»: нельзя сохранять shadow
  references в `.csproj`; cleanup исторической порчи не автоматизирован.
- **H3** — [историческая эпоха 3](../../analyzer-shadow-copy/epoch-3-inmemory-overlay-fix.md),
  «Decisions» 4–7, «Scope and non-goals», «Verification»: overlay-aware reads,
  обратное преобразование перед apply, отсутствие нового public SG search.
- **A** — [ARCHITECTURE](../../ARCHITECTURE.md), «Runtime composition», «Workspace lifecycle»:
  singleton `SolutionManager`, RAM load cache, сохранённые тексты, watcher,
  opt-in, temp growth. Ошибочная фраза recompute-on-read не признаётся фактом.
- **C** — [SolutionManager.cs](../../../Services/SolutionManager.cs):
  `GetCurrentSolution` (257), `ShadowCopyInSolutionAnalyzerReferencesAsync` (282),
  `ApplyShadowCopyOverlayIfEnabled` (325), revert guard (349),
  `ApplySolutionChangesToDiskAsync` (466), `ClearWorkspaceAsync` (578),
  `LoadCoreAsync` (646), `FlushDirtyDocumentsUnderLockAsync` (756).
- **F** — [AnalyzerReferenceShadowCopier.cs](../../../Services/AnalyzerReferenceShadowCopier.cs):
  `GetDefaultShadowRootDirectory`, matcher в `ShadowCopyInSolutionAnalyzerReferences`,
  `CopyToShadowDirectory`, `RewriteResult`.
- **L** — [InProcessAnalyzerAssemblyLoader.cs](../../../Services/InProcessAnalyzerAssemblyLoader.cs):
  `LoadFromPath`, `LoadCore`, `AddDependencyLocation`, глобальный resolver.
- **W** — [WorkspaceTools.cs](../../../Tools/WorkspaceTools.cs), `LoadWorkspace`:
  `LoadAsync` и включение overlay — отдельные вызовы, `false` не выключает
  ранее включённый overlay на RAM hit.
- **T** — [SolutionManagerAnalyzerOverlayTests.cs](../../../RoslynMcpServer.Tests/SolutionManagerAnalyzerOverlayTests.cs):
  только `AdhocWorkspace` и reflection guard; комментарий фиксирует отсутствие
  MSBuild bootstrap в этом наборе. [csproj](../../../RoslynMcpServer.csproj)
  уже предоставляет `InternalsVisibleTo` тестовому проекту.

Другие планы, включая [workspace-load-cache v2](../../workspace-load-cache_v2/README.md),
имеют статус proposed и не являются действующим ADR, разрешающим изменить lifecycle
этой серии. Действующих `AGENTS.md` в workspace/проверенных родительских каталогах
не найдено; `AGENTS.md.sample` не приравнивается к ним.

Проверены первичные .NET-источники: один ALC имеет одну сборку на simple name,
общие контрактные типы требуют общей Assembly instance; ALC не является security
sandbox. Это ограничения механизма, а не эксперимент над данным сервером.
[Microsoft: AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext).
`Assembly.LoadFrom` использует Default ALC; точный результат конкретной смены
версии нужно измерить на .NET 10, а не переносить описание .NET Framework Fusion.
[Microsoft: managed loading](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/loading-managed).

## R-01 — PARTIALLY ACCEPT

**Основание.** README предложения явно помечен «спецификации; реализация не начата»,
а спорная фраза стоит в «Цель». Целевой инвариант не становится обещанием shipped
поведения из-за красного baseline. Но слово «гарантировать» без области действия
обещает больше, чем H1/H3 проверили, и U этого не требовал.

**Изменение.** В README «Цель», «Общие инварианты» разделить измеряемую цель,
целевые критерии и текущие гарантии; указать матрицу и эпоху ввода каждого
свойства. Не удалять неизменяемость из целевых критериев до эпохи 2.

**Последствия.** Меняются release gates и формулировки поддержки; API, persistence
и миграции не меняются. Красный baseline допустим как результат исследования,
но не как подтверждение готовности исправления.

## R-02 — PARTIALLY ACCEPT

**Основание.** В C чтение опубликованного immutable snapshot и синхронизация
с диском — разные свойства. Отсутствие lock у getter само по себе не доказывает
частично переписанный `Solution`. Кроме того, пример с rename неточен:
[RenameSymbol](../../../Tools/UtilityTools.cs) вызывает `FindDocumentAsync` (957),
который делает flush, до получения semantic model. Затем rename читает solution
повторно (991): это уже другой потенциальный race, см. N-02 в сводке.

**Изменение.** README invariant 2 и эпоха 6 «Контракт снимков»: отдельно описать
целостность снимка, flush уже доставленных событий и согласованность одной
операции. Проинвентаризировать реальные semantic entry points. Не обещать
видимость ещё не доставленного watcher event даже после flush.

**Последствия.** Тесты freshness отдельно от overlay; возможные исправления
читателей требуют отдельной оценки. Нет основания просто исключить rename
или все getter callers из целевой гарантии навсегда.

## R-03 — ACCEPT

**Основание.** Точная обратная замена требует отображения original↔shadow,
которого C/F сейчас не возвращают как устойчивый контракт. U требует конкретных
этапов, а не двух последовательных переделок guard. Mapping можно разработать
до файловой реализации, но нельзя объявлять весь этап 4 независимым от него.

**Изменение.** README «Эпохи и порядок», эпоха 2 «Требования» и эпоха 4
«Требования»: этап 4 зависит от утверждённого контракта mapping эпохи 2.
Централизация call sites может готовиться раньше. Эпоха 3 сохраняет собственный
binding gate; новый content path не закрывает её.

**Последствия.** Внутренний DTO mapping и порядок работ; новые MCP параметры,
дисковая сериализация Roslyn snapshots и MVCC не требуются.

## R-04 — REJECT

**Основание.** Замечание не обнаруживает заявленного смешения ответственности:
эпоха 2 «Проблема» явно описывает перезапись файла, эпоха 3 «Проблема» прямо
говорит, что новый путь не доказывает новое исполнение. Список открытых вопросов
в README перечисляет несколько вопросов через запятую, а не утверждает,
что один чинит другой. U требует проверять loader прежде, чем усложнять его;
разделение уже соответствует этому ограничению.

**Изменение.** Обязательного изменения по R-04 нет. Неопределённость cache-hit
исправляется по E1-02, а не принимается как доказательство слияния эпох 2 и 3.

**Последствия.** Две независимые проверки — файловая целостность и исполняемая
версия — сохраняются. Закрывать loader по факту нового каталога недопустимо,
но это уже запрещено исходной спецификацией.

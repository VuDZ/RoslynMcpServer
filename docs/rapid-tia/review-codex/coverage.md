# Coverage

## Reviewed target

- Полностью прочитан docs/rapid-tia/README.md, 384 строки.
- Дата: 2026-10-30. Раунд: 1.
- Проверенная версия: HEAD c6f7f851f20bd62b8773cbcdd409e278c95395a1, исходный рабочий diff пуст.
- SHA-256 README до учётных правок: 18A6001E3E0A96CD2E8D05C0BF6AF91EC38E4F0B9D4C6B294739F46E2AFFE107.
- Рецензент: Codex; точный runtime model ID средой не предоставлен. Другие рецензенты не запускались.
- Scope: review программы и её соответствия доступным production seams, без реализации, защиты и арбитража.

## Context inspected

- AGENTS.md; docs/README.md, включая правила статусов и статистики ревью.
- docs/ARCHITECTURE.md: публикация workspace, disk sync и ограничения membership/TFM.
- docs/user-data-directory/README.md целиком: расположение tia и зависимость эпохи 4.
- docs/workspace-load-performance/README.md: актуальный статус, границы и контекст стоимости первого поиска; чужие review-каталоги и материалы арбитража не читались.
- Services/Workspace/SolutionManager.cs: чтение опубликованного состояния, очистка, reload/cache hit, публикация, запуск/отмена search prewarm.
- Services/Navigation/SearchIndexPrewarm.cs целиком; Services/Navigation/SymbolDeclarationResolver.cs — нормализация идентичностей.
- Services/Testing/TestAttributeMatcher.cs, TestFilterHelper.cs, TestDiscoveryHelper.cs.
- Tools/TestTools.cs: внешний контракт RunTestByFilter и передача фильтра существующему runner.
- Services/Workspace/WorkspaceDocumentDiskSync.cs; WorkspaceDiskEventClassifier.cs и WorkspaceInputMapBuilder.cs — соответствующие участки классификации событий, memberships и контекста проектов.
- Services/Models/LoadedProjectInstance.cs, InputOccurrence.cs, InputCategoryKind.cs, WorkspaceInputMap.cs.
- Services/Workspace/MsBuildWorkspaceProperties.cs; Services/Build/Models/BuildContext.cs.
- Services/Workspace/MissingSourceFiles.cs и MissingSourceFileTextLoader.cs — материализация текстов и file loaders.
- Services/Workspace/StateCache/Models/SnapshotProjectKey.cs, IsolatedWorkspaceHydrator.cs и SnapshotOptionFacts.cs — существующие границы project/document identity и восстановленных текстов.
- RoslynMcpServer.Tests/SourceStructure/ReachabilityAnalysis.cs — контракт обхода, лимиты и представление символов; production-перенос не выполнялся.
- RoslynMcpServer.Tests/Workspace/WorkspaceDocumentDiskSyncTests.cs и WorkspaceSharedSourceSyncTests.cs — новые/удалённые файлы и linked source с разными parse options.
- RoslynMcpServer.Tests/Navigation/SearchIndexPrewarmTests.cs — управляемая остановка фона и cancel/reload.
- RoslynMcpServer.Tests/TestRunner/TestFilterHelperTests.cs — существующие гарантии contains/escaping.
- RoslynMcpServer.csproj — используемый Roslyn 5.9.0; docs/code-style.md прочитан полностью, C# не менялся.

## Source/code verification

- Разобраны ветви обычной публикации/очистки/reload; сохранение TIA-baseline в production ещё не реализовано и не объявляется проверенным поведением.
- Сопоставлены синтаксический pruning, новые test roots, binding changes и запрос baseline-битсетов.
- Проверено разделение физических путей и project/document occurrences в уже существующей input map.
- Проверено фактическое использование FullyQualifiedName~ в TestFilterHelper.
- Для stale-cache ответа построен контрпример с изменённым до load телом фабрики и edit свежего документа после load; это логический walkthrough предложенного алгоритма, не runtime-тест TIA.
- По первичным внешним источникам сверены [FileTextLoader](https://source.dot.net/Microsoft.CodeAnalysis.Workspaces/Workspace/Solution/FileTextLoader.cs.html), [TextDocumentState](https://source.dot.net/Microsoft.CodeAnalysis.Workspaces/Workspace/Solution/TextDocumentState.cs.html), [MSBuildWorkspace](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs) и [формат documentation IDs](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments#d42-id-string-format). Внешние исходники не выдаются за точную ревизию пакета; две узкие предпосылки дополнительно проверены локально на 5.9.0.

### Выполненные API-пробы

Из PowerShell 7.6.5 загружены установленные пакеты Microsoft.CodeAnalysis.Common, CSharp и Workspaces.Common 5.9.0, lib/net10.0, в отдельный collectible AssemblyLoadContext.

1. Во временный файл записано before; создан FileTextLoader; файл заменён на after; впервые вызван LoadTextAndVersionAsync с SHA-256. Получено:

    Captured FileTextLoader first read: after

   Проверка ожидаемого after прошла. Временный файл удалён.

2. Созданы независимые CSharpCompilation First и Second с одинаковым объявлением Shared.Helper.Run. Через ISymbol.GetDocumentationCommentId получено:

    First: M:Shared.Helper.Run
    Second: M:Shared.Helper.Run

   Ссылки на metadata для этой проверки идентичности объявлений не требуются; это не проверка компилируемости/исполнения пользовательского проекта.

Первоначальная попытка загрузить Roslyn непосредственно в контекст PowerShell встретила assembly-version conflict; последующие пробы изолированы в AssemblyLoadContext. Промежуточные ошибки reflection-вызовов исправлены до получения указанных результатов. Это ограничения исследовательской обвязки, а не failures тестов репозитория.

### Проверка каталога результатов

- Проверены все 7 finding IDs, обязательные секции и соответствие индексу result.md.
- Сводка совпадает с файлами: 6 major, 1 minor.
- Проверены 8 относительных ссылок внутри review-каталога.
- Построчно сопоставлен нормативный текст README с HEAD: изменений нет; изменены только статус в шапке и поля учёта ревью.
- git diff --check прошёл. Проверка репозиторных suite не выполнялась и не заявляется.

## Context not inspected

- Чужие review-*, defence/response, arbitration и post-version decision ledgers не читались и не использовались.
- Не проводился аудит всего кода репозитория, upstream fork и всех тестовых адаптеров.
- Исторические коммиты не прогонялись; документы с чужими результатами completeness-стендов не использовались.
- Рабочая нагрузка большого пользовательского solution и закрытое офисное окружение недоступны.

## Coverage limitations

- Реализации Rapid TIA пока нет. Приведённые failure scenarios проверяют specification, а не утверждают наличие уже shipped-багов.
- Нет интеграционной пробы «загрузка через SolutionManager → удержанный фон → внешняя запись»: F-001 подтверждён на уровне loader API и исследованного порядка публикации. Такая проба нужна при подготовке эпохи 1.
- Не измерялись recall, размер/время чтения кеша, RTA/CHA CPU и память, влияние на первый поиск. Числа программы остаются гипотезами для эпохи 2.
- Не проверялись runtime-форматы nested/generic FQN всех xUnit/NUnit/MSTest адаптеров. F-007 доказан уже текущим contains-контрактом на обычных именах методов.
- Release build, SourceStructure, main CI и AnalyzerLifecycle не запускались: production, tests, project/build settings не менялись. Выполненные API-пробы не считаются заменой этим suite и не означают приёмку реализации.
- Итоговый diff ограничен каталогом этого review и учётными полями README темы/индекса; нормативный текст proposal не переписан.

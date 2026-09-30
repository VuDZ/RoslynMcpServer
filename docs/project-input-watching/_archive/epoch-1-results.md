# Эпоха 1. Результаты: карта входов

Статус: **принята**. Версия сборки не менялась (`1.5.1`).

## Модель

Карта живёт в уже существующей `WorkspaceInputSession`. `Generation` — прежний token группы watchers. Повторная публикация карты увеличивает `MembershipRevision` и не заводит второй счётчик сессии. Чужой `Generation` в сессию не принимается.

Обратный индекс: канонический путь (`Path.GetFullPath`, без разрешения symlink и других алиасов) → все вхождения. Сравнение путей совпадает с ОС. У вхождения есть project instance (`ProjectId`, не имя и не путь `.csproj`), `DocumentId` если это документ, роль, производитель и признак present/missing. Производители (`generated`, output) и потребители (user input, additional file, analyzer config, metadata reference) хранятся раздельно.

Области потенциального membership — каталоги загруженных проектов, рекурсивно. Отсутствие пути в индексе известных документов не доказывает исключение: новый `.cs` под каталогом SDK-проекта остаётся потенциальным glob-item. `Compile Remove` в этом источнике не виден как доказанное исключение.

`search_code` не менялся. Корни поиска на карте — прежний `ComputeWatchRoots`: каталог загруженного workspace и каталоги проектов, вложенные дубликаты убираются. Linked-файлы и ancestor props в поиск не входят.

Описатели watcher — отдельный список. Они не запускают `FileSystemWatcher` и не подменяют текущие корни дискового watcher.

## Источник MSBuild evidence

Публичные данные загруженного `Solution` после оценки `MSBuildWorkspace`:

- документы, additional documents, analyzer config documents;
- project references между загруженными instances;
- metadata references (`PortableExecutableReference`);
- `OutputFilePath` и `OutputRefFilePath`;
- флаг `DocumentInfo.IsGenerated`. На живом документе он лежит во внутреннем state; это тот же флаг, который MSBuildWorkspace выставляет из оценённого item («side effect of the build»). Если член прочитать нельзя, роль unknown, а не догадка.

`FileGlobs`, список `Import`, входы restore и входы custom task в этом API нет. Своего интерпретатора item group нет.

`TargetFramework` / `TargetFrameworks` читаются из текста `.csproj` и предковых `Directory.Build.props` только как сигнал полноты, тем же разбором элемента, что `DirectoryBuildPropsReader`. Условия и items не оцениваются.

## Профиль: что индексируется и что unknown

Весь целевой граф этой эпохи не объявляется complete. `PublishedAsComplete` остаётся false, пока есть неизвестные категории. Урезанный индекс документов за полную карту не выдаётся. Неуспешный load добавляет причину `load-incomplete` и тоже не публикует индекс как complete.

Индексируется по загруженному графу, без заявления о полноте категории:

| Категория | Источник | Событие | Предел |
|---|---|---|---|
| C# | `Project.Documents` | изменение известного документа; появление файла в каталоге проекта | список документов не является правилом включения |
| Additional files | `AdditionalDocuments` | изменение оценённых файлов | не попавшие в граф items unknown; текстовая синхронизация не делается |
| Analyzer configs | `AnalyzerConfigDocuments` | изменение оценённых файлов | новый `.editorconfig` вне списка unknown |
| Project references | `ProjectReferences` загруженных instances | изменение файла загруженного проекта | ссылка, оставшаяся только metadata, не является instance |
| Outputs | `OutputFilePath`, `OutputRefFilePath` | изменение output | это не user input производящего проекта |
| Walk-up кандидаты | имена файлов вверх от каталога проекта | создание, изменение, удаление, в том числе пока файла нет | факт импорта не доказан |

Unknown для всего графа, даже если документы проиндексированы:

- imports (кроме непроверенных walk-up кандидатов);
- restore inputs (`project.assets.json`, lock, `nuget.config`);
- custom tasks;
- полнота внешнего glob;
- XAML и resources, которых нет среди документов графа; увиденный `.xaml` / `.resx` имеет роль additional file, не «роль расширения», и полнота категории всё равно unknown;
- условные входы другого TFM, не подтверждённые этим графом;
- metadata-only зависимость, которой нет среди загруженных проектов (сборка shared framework или пакет NuGet записывается, но не считается отсутствующим проектом).

Роли:

- `generated` только при `IsGenerated = true` (производитель — этот project instance);
- обычный compile item с флагом false — user input, в том числе явный файл в `obj`, если имя не похоже на артефакт SDK;
- `*.g.cs`, `*AssemblyInfo.cs`, `*AssemblyAttributes.cs` с флагом false — unknown, не user и не generated. Design-time загрузка SDK кладёт `App.AssemblyInfo.cs` и `*.AssemblyAttributes.cs` в `obj` именно с флагом false;
- путь в `obj` / `bin` / `artifacts`, которого нет в индексе, — unknown, не blanket ignore и не доказанное generated.

Один путь с разными ролями сохраняет все вхождения: generated-производитель в одном проекте и user-потребитель в другом.

## Группировка watchers

Рекурсивный описатель разрешён только на каталог загруженного проекта. Точечные фильтры в него не сливаются.

Нерекурсивные подписки группируются по каталогу файла: несколько путей в одном каталоге делят один описатель, `IncludeSubdirectories = false`. Объединение не поднимает такую подписку до рекурсии и не захватывает предка.

Для внешних путей фикстуры:

- linked-файл (`linked/Shared.cs`) — нерекурсивная подписка на каталог `linked`, точечный фильтр этого файла;
- общий файл с двумя ролями (`roles/Mixed.cs`) — так же, каталог `roles`;
- результат внешнего glob (`external-glob/Matched.cs`) — нерекурсивная подписка на каталог файла; сосед `NotInGraph.cs` не член рекурсивной области, появление unknown; рекурсивного watcher на `external-glob` нет;
- ancestor `Directory.Build.props` на три уровня выше проекта — нерекурсивная подписка на каталог самого props, не на дерево предка;
- отсутствующий `Directory.Build.targets` в каталоге проекта — отдельная нерекурсивная точечная подписка на этот путь (файл absent). Рекурсивная область каталога проекта при этом остаётся и не превращается в watcher предка;
- `global.json` и `Directory.Packages.props` по той же walk-up цепочке — точечно и нерекурсивно, в том числе пока файлов нет;
- metadata DLL условного проекта вне графа — нерекурсивная подписка на каталог DLL; сам `.csproj` в граф не входит; покрытие не complete;
- файл solution — нерекурсивная точка на каталог `.sln`, не рекурсивное дерево solution.

Каталоги проектов `app` и `external` — единственные рекурсивные области.

## Что проверено про TFM

Обычная загрузка внутреннего TFM не создаёт второй instance. Реальный `LoadAsync` SDK-проекта с `TargetFrameworks` `net10.0;net8.0` и аргументом `targetFramework=net10.0` вернул один project instance. Условный `OnlyNet8.cs`, лежащий в каталоге проекта, всё равно вошёл в этот instance через SDK glob; неизвестность другого TFM следует из декларации двух TFM при одном instance, а не из выкидывания этого файла.

Второй instance одного `.csproj` искусственной моделью не подменялся и этой загрузкой не наблюдался. Не проверялся отдельный прогон без `targetFramework` (внешний cross-targeting build). Покрытие multi-TFM при двух объявленных TFM и одном instance — unknown на весь граф.

Соответствие symlink другому пути не объединялось: каноникализация не резолвит ссылки. Отдельный symlink-прогон не делался.

# Открытые вопросы наблюдения за входами

Дата: 2026-10-01. W-02 закрыт эпохой 1. Карта входов публикуется в той же session generation при загрузке. W-03 по-прежнему не выбирает политику восстановления семантики.

## W-01. Выбор карты и граница синхронизации — закрыт

Решение серии закреплено после критики: обнаруживать C# и не-C# изменения
и уведомлять по ролям. Текстовую синхронизацию `AdditionalDocument` и
`AnalyzerConfigDocument` эта серия не делает. Повторный выбор между
обнаружением и такой синхронизацией не требуется.

Одна карта ролей включает `.cs` и обнаруженные не-C# inputs, в том числе
XAML/resources, AdditionalFiles, analyzer configs и evaluation/restore inputs.
Автоматический text sync только для поддержанного пользовательского `.cs`;
остальные роли дают уведомление и unknown, пока нет достаточного evidence.
Source of evidence и полнота профиля остаются W-02, а не повторным выбором W-01.

## W-02. Membership и данные оценки MSBuild — закрыт

Источник membership — загруженный граф Roslyn после оценки `MSBuildWorkspace`,
а не отдельный интерпретатор MSBuild. Индексируются фактически присутствующие
instances: `Project.Documents`, `AdditionalDocuments`, `AnalyzerConfigDocuments`,
`ProjectReferences`, `MetadataReferences`, `OutputFilePath` / `OutputRefFilePath`.
Флаг `DocumentInfo.IsGenerated` («side effect of the build») читается с живого
документа. `FileGlobs`, граф `Import`, входы restore и custom tasks на этой
публичной поверхности отсутствуют.

`TargetFramework` / `TargetFrameworks` читаются только как сигнал полноты
(тот же разбор элемента, что у `DirectoryBuildPropsReader`), без оценки условий
и item group. Ровно один объявленный TFM и один загруженный instance этого
`.csproj` не создают второй instance. Больше одного объявленного TFM при меньшем
числе instances, либо отсутствие декларации, переводят покрытие всего графа
в unknown.

Поддерживаемый профиль — не полное покрытие целевого графа:

- Индексируются известные C# документы, additional files, analyzer configs,
  рёбра project reference между загруженными instances, пути output и
  кандидаты walk-up (`Directory.Build.props` / `.targets`, `Directory.Packages.props`,
  `global.json`), включая отсутствующий файл.
- XAML и resources учитываются только если они уже документы графа; расширение
  не является ролью. Полнота этих категорий unknown.
- Imports, restore inputs и custom tasks unknown.
- Внешний glob: известный файл вне каталога проекта получает точечную
  нерекурсивную подписку; появление новых соседей unknown. Рекурсивный watcher
  на чужое дерево не ставится.
- Metadata reference, которая не является output загруженного проекта и не
  является сборкой shared framework / NuGet package, не считается загруженным
  проектом и не даёт complete.
- `.g.cs`, `*AssemblyInfo.cs` и `*AssemblyAttributes.cs` в Compile не становятся
  пользовательским входом. Роль `generated` — только при установленном флаге
  side effect. Если флаг сброшен, роль unknown: design-time оценка SDK оставляет
  `AssemblyInfo` / `AssemblyAttributes` в `obj` с флагом false.
- Явный пользовательский `.cs` в `obj` с сброшенным флагом и без такого имени
  остаётся user input.

Индекс документов не публикуется как полная карта. Неуспешная загрузка тоже
не публикует индекс как complete. Реальный `load` проекта с
`TargetFrameworks` `net10.0;net8.0` и `targetFramework=net10.0` дал один
instance, не два.

## W-03. Свежесть семантики вне известных C# текстов

Граница серий закреплена: здесь обнаружение и уведомление по ролям;
текстовая синхронизация дополнительных/config документов остаётся в
[non-csharp-input-tracking](../non-csharp-input-tracking/README.md).
Наблюдение не выбирает общий reload/build/restore policy для semantic reads.
Открытая политика восстановления согласуется с этой темой и
[workspace-load-cache Epoch 3](../../backlog/workspace-load-cache/epoch-3-live-consistency/spec.md).
Локальное исправление внешних путей и linked memberships не зависит от disk cache.

## W-04. Версия и WPF-сценарий коллег — закрыт

[WPF-серия](../wpf-temporary-project-watching/README.md) закрыта на локальном fixture (SDK 10.0.300). Версия сервера коллег и их Solution не получены и в ту приёмку не входили. Для карты входов это не блокер.

## W-05. Consumer API и идентичность сессии — закрыт

Выбран [общий pull-контракт](input-state-contract.md): generation, revisions,
owners/roles, membership, coverage и причины unknown. Состояние сохраняется
независимо от consumer. Одна session generation используется WPF, watcher
и build freshness; будущий workspace cache переиспользует эту модель.
Реализация provider относится к эпохе 3; второй registry/FSW feed не допускается.

# Открытые вопросы наблюдения за входами

Дата: 2026-09-30. Эти вопросы описывают будущую реализацию; runtime не меняется.

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

## W-02. Membership и данные оценки MSBuild

Roslyn `Project.Documents` даёт известные документы, но не полный набор
imports, новых glob-items и custom task inputs. Нужен доказанный источник
данных и правила для условных/внешних зависимостей и metadata-only references.

Если профиль не покрывает эти случаи, использовать conservative invalidation
и unknown. Не реализовывать произвольный MSBuild evaluator в рамках watcher.
Выбор backend и поддерживаемого профиля — результат эпохи 1.

Проверить полный набор required categories целевого профиля: XAML/resources,
AdditionalFiles, analyzer configs, imports/restore inputs и custom tasks.
Урезанный документный индекс и отсутствие metadata-only reference в Roslyn
не разрешают complete. Неподтверждённые регионы остаются conservative stale/unknown.

Рекурсивное наблюдение каталогов предков и чужих внешних glob-деревьев не
является способом закрыть этот вопрос. Внешние paths наблюдаются точечно/
нерекурсивно; непокрытое появление новых inputs оставляет coverage unknown.
Для входов другого TFM, не подтверждённых фактически загруженным графом,
unknown распространяется на весь граф. Два instances одного `.csproj`
проверяются только при наличии обоих в реальном загруженном графе.

## W-03. Свежесть семантики вне известных C# текстов

Граница серий закреплена: здесь обнаружение и уведомление по ролям;
текстовая синхронизация дополнительных/config документов остаётся в
[non-csharp-input-tracking](../non-csharp-input-tracking/README.md).
Наблюдение не выбирает общий reload/build/restore policy для semantic reads.
Открытая политика восстановления согласуется с этой темой и
[workspace-load-cache Epoch 3](../workspace-load-cache/epoch-3-live-consistency.md).
Локальное исправление внешних путей и linked memberships не зависит от disk cache.

## W-04. Версия и WPF-сценарий коллег — закрыт

[WPF-серия](../archive/wpf-temporary-project-watching/README.md) закрыта на локальном fixture (SDK 10.0.300). Версия сервера коллег и их Solution не получены и в ту приёмку не входили. Для карты входов это не блокер.

## W-05. Consumer API и идентичность сессии — закрыт

Выбран [общий pull-контракт](input-state-contract.md): generation, revisions,
owners/roles, membership, coverage и причины unknown. Состояние сохраняется
независимо от consumer. Одна session generation используется WPF, watcher
и build freshness; будущий workspace cache переиспользует эту модель.
Реализация provider относится к эпохе 3; второй registry/FSW feed не допускается.

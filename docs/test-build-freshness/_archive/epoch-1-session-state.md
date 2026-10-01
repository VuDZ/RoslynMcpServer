# Эпоха 1. Состояние сборок в текущей сессии

Статус: **выполнена** (2026-10-01). Сложность: высокая из-за проверки полноты входов.

## Цель и работа

- Ввести независимый session build-state service: ключ build context,
  подтверждённые revisions/fingerprints, output identity и причина dirty/unknown.
  Независимы build proofs, а не источник входов: membership, owners и revisions
  читать из [общего pull-снимка](../../archive/project-input-watching/input-state-contract.md).
  Не создавать собственные FSW subscription/feed, input index или session token.
  Ничего не сохранять между процессами.
- Загрузка/reset/new session generation общего владельца создаёт unknown
  для всех проектов.
  Same-session cached load сохраняет состояние; изменение фактического build
  context выбирает другой ключ, даже если RAM Solution переиспользуется.
  Изменение membership revision также обесценивает неподтверждённый scope;
  отдельный счётчик session generation внутри build-state не вводить.
- На входной edit загрязнять владельцев и обратные транзитивные зависимости.
  Строить dependency closure по подтверждённому build graph; при неполноте
  не использовать только Roslyn reference set как полный.
- Build start фиксирует generation/context и состояние входов. Build success
  подтверждает лишь тот scope, для которого есть evidence; changed-during-build,
  failure/cancel и поздний результат старой сессии не очищают dirty.
- Pull не потребляет события. Фиксировать generation, membership revision,
  revisions входов подтверждаемого scope, owners/roles, pending state, coverage
  и unknown reasons до/после build и перед skip. Snapshot revision задаёт
  согласованность чтения; изменение только generated outputs или доказанно
  unrelated проекта не приравнивается к input edit выбранного build scope.
- Non-C# changes, случившиеся до создания build-state, уже находятся в общем
  session snapshot. Bootstrap не обнуляет их, semantic flush не очищает.
  Pending event, неполный provider, неизвестные membership rules и metadata-only
  dependencies запрещают `current` для reuse. Build success не восстанавливает
  coverage: при unknown сохраняется always-build fallback.
- Определить проверку перед reuse: чтение содержимого/валидирование полного
  supported input set, membership и outputs либо conservative always-build.
  [F-03](../UNRESOLVED.md) закрыть до включения skip; silent missing event должен
  обнаруживаться проверкой или приводить к отказу от оптимизации.
- Обычный `run_dotnet_build` может подтверждать состояние через тот же механизм,
  только если известны target, context, input evidence и реальный результат.
  Внешний build и `execute_dotnet_command` не получают доверие по одному имени команды.
- Уточнить публичный автоматический режим по [F-01](../UNRESOLVED.md) и evidence
  полноты входов по F-02. W-01 уже закрыт как выбор одной карты C#/non-C# ролей;
  новый этап исследования этого выбора не требуется. Модель проектировать
  независимо от выбранного имени параметра.

## Проверки и результат

Проверить A→D и B→D: правка D загрязняет A/B/D; сборка A не подтверждает B.
Общий `.cs` в A/B загрязняет обоих; build одного не очищает другого. Разные
Configuration/Platform/TFM/buildArgs изолированы. Отсутствующий output, смена
session generation/membership revision, failed build и event после build start
сохраняют dirty/unknown.

Проверить XAML/import edit до появления consumer, затем semantic flush и
bootstrap: сохранённый revision/unknown не исчезает. После fallback build
неполное coverage не становится complete/current для skip. Новый `.cs` вне
известного индекса, metadata-only dependency и недоступные membership rules
не дают ложного `current`. WPF token, input provider generation и build proof
совпадают; второй consumer читает те же revisions без потребления первым.

Результат эпохи: модель состояний и проверок с supported scope, интеграция
с общим pull provider и regression проверки переходов. Без полного evidence разрешён
always-build fallback; это не считается реализованным пропуском сборки.

# Открытые вопросы проверки сборки перед DLL-тестами

Дата: 2026-09-30. Согласованное начальное dirty-состояние и session-only
подтверждения закреплены в [README](README.md); повторного согласования не требуют.

## F-01. Автоматический режим и существующий noBuild

Нужно автоматическое ensure-current перед DLL-тестами. Сегодня `noBuild=true`
явно запрещает предварительный build; при этом default `run_test_by_filter`
равен true. Отсутствующий bool-аргумент и явно переданный true сейчас внутри
метода неразличимы. Изменение default само по себе не решает контракт.

Рекомендуемое направление для обсуждения: явная политика `auto` / `always` /
`never`, где автоматический DLL-маршрут использует `auto`, а принудительный
запрет на build при dirty/unknown возвращает отказ, а не запускает старую DLL.
Имя/тип параметра, миграция `noBuild` и поведение при двух одновременно
переданных параметрах пока не выбраны. Это предложение, не согласованный API.

До публичного включения закрепить одинаковую семантику трёх адаптеров и
обновить descriptions. Внутренний `--no-build` тестового процесса остаётся:
он не является запретом отдельной предварительной сборки через Solution.

## F-02. Evidence полноты значимых входов

В watcher-серии принято обнаружение не-C# изменений и уведомление по ролям;
[W-01](../archive/project-input-watching/UNRESOLVED.md) закрыт как выбор общей карты ролей
и границы text sync. Открыт W-02: источник membership и evidence полного профиля.
Для WPF одних `.cs` недостаточно.
Учитывать подтверждённые XAML/resources, AdditionalFiles, configs/imports/restore
inputs и зависимости; arbitrary custom tasks не получают гарантию без полного
evidence. Generated intermediates/outputs производящего build не увеличивают
его input revisions; отдельные подтверждённые роли пути сохраняются.
Неподдержанный профиль всегда строится через Solution и не получает оптимизацию skip.

Нет в индексе документов не означает «не input». Новые glob-items/imports,
неизвестные membership regions и metadata-only references сохраняют
conservative stale/unknown. Complete не выдаётся по урезанному профилю.

## F-03. Доказательство неизменности между запусками

Отсутствие FSW-события не доказывает неизменность. Нужно выбрать подтверждённую
проверку входов, состава и outputs перед reuse; metadata-only shortcuts,
content fingerprints и их бюджеты должны быть обоснованы сценариями.
Для unknown действует already-agreed build fallback. Выбор live validation
не означает реализацию долговременного кеша и не меняет глобальный semantic
read contract из workspace-load-cache.

## F-04. Что действительно подтверждает solution target

Нужны source of evidence для build scope, effective context и результатов
зависимостей, включая config mappings, disabled project-reference builds,
metadata-only зависимости и custom outputs. До подтверждения нельзя очищать
весь dependency closure по exit code 0. Эпоха 1 фиксирует conservative policy;
эпоха 3 проверяет её на реальной сборке.

## F-05. Граница гонок и retry

Нужны ограниченная retry policy, обработка общих outputs и внешнего build.
Инвариант уже задан: обнаруженное изменение не очищается старым build и
не разрешает запуск неподтверждённой DLL. Атомарная сборка из immutable
snapshot вне scope; точный предел проверяемой стабильности описать в отчёте.

## F-06. Consumer API и lifecycle — закрыт

Выбран [общий pull-снимок](../archive/project-input-watching/input-state-contract.md),
а не subscriber на watcher-потоке. Generation, revisions, owners/roles,
membership, coverage, pending state и unknown reasons читаются без потребления.
События C#/non-C# сохраняются в сессии до появления consumer и не очищаются
semantic sync. Session token общий с WPF/watchers; отдельного generation/
input index в build-state нет. Реализация provider остаётся в watcher Epoch 3;
при её отсутствии действует build fallback, а не второй временный feed.

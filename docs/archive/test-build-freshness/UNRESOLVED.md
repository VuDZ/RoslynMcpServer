# Открытые вопросы проверки сборки перед DLL-тестами

Дата: 2026-10-01. F-01…F-05 закрыты принятыми решениями. Публичный параметр,
пропуск сборки и цикл повтора этими решениями не включаются.
Согласованное начальное dirty-состояние и session-only
подтверждения закреплены в [README](README.md); повторного согласования не требуют.

## F-01. Автоматический режим и существующий noBuild — закрыт

Публичный параметр появится позже, не в модели состояния. `buildPolicy` =
`auto` | `always` | `never`, по умолчанию `auto` на DLL-маршруте
(`binariesPath` задан). `noBuild` становится `bool?`, чтобы пропуск
аргумента был виден. На маршруте без DLL отсутствие параметра сохраняет
сегодняшний default (`false` у `run_dotnet_test` и `run_specific_test`,
`true` у `run_test_by_filter`). На DLL-маршруте: оба параметра переданы —
ошибка; только `noBuild=false` → `always`; только `noBuild=true` →
`never`; ни один не передан → `auto`. `never` запускает тесты только
когда состояние `current` и выходы совпадают; иначе отказ, тесты не
стартуют. Внутренний `dotnet test` на DLL-маршруте всегда получает
`--no-build`. `buildPolicy` без `binariesPath` — ошибка. Это реализует
следующая эпоха. Модель состояния от имени параметра не зависит.

## F-02. Evidence полноты значимых входов — закрыт

Граница evidence — закрытый профиль W-02 в
[UNRESOLVED наблюдения за входами](../project-input-watching/UNRESOLVED.md).
Пропуск сборки потребовал бы pull coverage `complete` для dependency scope
этого build context. Любая неизвестность, пересекающая scope (imports,
restore inputs, custom tasks, соседи external-glob, metadata reference,
которая не является выходом загруженного проекта и не является сборкой
shared framework / NuGet, недосчитанный TFM, XAML/resources, которые ещё
не документы графа), оставляет always-build. Успешная сборка не превращает
unknown coverage в complete. Отсутствие пути в индексе документов не
доказывает, что путь не вход.

## F-03. Доказательство неизменности между запусками — закрыт

Отдельного хранилища хешей входов нет. Курсор подтверждения хранит
generation, membership revision, input revisions путей, coverage, pending
и output identity, которую передал вызывающий. Сравнение этого курсора
с хешами содержимого входов откладывается, пока общий снимок не несёт
эти хеши. До тех пор reuse/skip запрещён. Политика хеша semantic read
не выбирается (workspace-load-cache U-ARB-02 остаётся открытым).

## F-04. Что действительно подтверждает solution target — закрыт

Exit code 0 подтверждает только проект, который был целью вызова, и только
этот context (Configuration, Platform, TFM, buildArgs, выходной путь), и
только когда coverage этого scope был complete на старте и на финише,
input revisions не изменились и output identity снята. Project references,
соседи с общим файлом и остальная Solution не становятся current.
`BuildProjectReferences` из exit code не читается. Если выход зависимости —
вход, чья свежесть неизвестна, цель остаётся unknown.

## F-05. Граница гонок и retry — закрыт

Здесь только решение; цикл повтора не реализуется (его место — эпоха гонок).
Один повторный build,
если revisions или output изменились между успехом и стартом тестов;
второй сбой — отказ; оба делят один `timeoutSeconds`. Операции MCP с одним
и тем же ключом context сериализуются на этом ключе. Семафор workspace не
удерживается на время CLI-процесса. Внешний build и `execute_dotnet_command`
не являются подтверждениями. Два context с одним выходным путём остаются
unknown, пока путь не уникален.

## F-06. Consumer API и lifecycle — закрыт

Выбран [общий pull-снимок](../project-input-watching/input-state-contract.md),
а не subscriber на watcher-потоке. Generation, revisions, owners/roles,
membership, coverage, pending state и unknown reasons читаются без потребления.
События C#/non-C# сохраняются в сессии до появления consumer и не очищаются
semantic sync. Session token общий с WPF/watchers; отдельного generation/
input index в build-state нет. Реализация provider остаётся в watcher Epoch 3;
при её отсутствии действует build fallback, а не второй временный feed.

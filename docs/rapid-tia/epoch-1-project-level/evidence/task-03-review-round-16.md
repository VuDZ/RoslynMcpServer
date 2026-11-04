# task-03 — независимое ревью, раунд 16

Дата2026-11-04. Рецензент Codex, новая независимая сессия, runtime model ID недоступен. База1b617c4, четыре C# файла после исключения pure metadata/output lifetime seeds.

Вердикт **MUST-FIX**, R14-001 остаётся открытым.

## Транзитивная граница lifetime guards

WorkspaceDiskWatcherStarter.SubscriptionsToStart считает excluded metadata/output root уже защищённой границей для eligible descendant. Например, Shared содержит только output entry, Shared/Sub — Known linked C# документ. Shared seed исключается, а цепочка Sub останавливается на Shared. При переносе всего Shared ни один watcher не обязан сообщить о движении своего root; документ может остаться вне журнала при опущенном gap.

Требуется продолжить lifetime chain через excluded primary parent либо защитить его как ancestor eligible C# root. При этом существующий content watcher parent можно использовать для direct-child событий, без его дублирования. Нужна регрессия смешанной вложенной раскладки; независимые SDK metadata/output leaf roots остаются исключёнными.

Это один из аспектов R14-001, не новый unique finding ID. Остальной diff, минимальный pure-role scope и RED-хронология приняты. Рецензент не запускал runtime и не менял файлы.

## Validation

[До раунда17](task-03-validation-before-round-17.md): Windows main1759/1759, Linux1750/1751. Единственный Linux failure — Timestamp_guard_works_without_any_watcher_event; его причина не установлена, полного зелёного Linux acceptance нет. Отдельный повтор1/1 прошёл, но не заменяет обязательные main suites.

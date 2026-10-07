# E1 — Первый disk cache MVP

Статус: **draft / ready-for-review, 2026-10-07**. Реализация не начата.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат

Первый production MVP: ordinary load сохраняет snapshot и manifest; новый PID
проверяет их, восстанавливает поддержанный request и подключает существующие
watcher’ы. Неизменный реальный request даёт disk hit. Любое обнаруженное
значимое изменение пока даёт whole-request ordinary load с причиной.

## Стартовая проверка

Применяется ограниченный manifest из [README серии](../README.md#индекс-входов).
Проверяются и bytes известных inputs, и появление/удаление возможных inputs в
заданных каталогах. Imported paths извлекаются из ordinary load evidence;
стандартные absent walk-up кандидаты записываются явно. Это практический
MVP-профиль с указанными границами, а не доказанная полнота произвольного MSBuild.
Неподдержанный custom input, неразрешённый import или неясный project instance
даёт fallback. Нужен хотя бы один положительный реальный request;
support detector, отказывающий всем, не считается MVP.

SDK/MSBuild/Roslyn/schema identity проверяется отдельно от файлов проекта.
Изменение версии/resolution fingerprint даёт miss; обход всей установки SDK
не входит в обязательный путь. Validation profile возвращается как `mvp-fast`:
для больших бинарей применяется явно эвристическая проверка.

## Интеграция

Выделить минимальный host ownership seam из MSBuildWorkspace-specific операций.
Load/hydrate, prepare, admission и публикация остаются под одним manager acquisition.
Failed/cancelled candidate освобождается без публикации частичного состояния;
активная same-key сессия сохраняется до успешной замены. Старый другой key
не выдаётся как результат нового request. Watcher session/input map остаются
одними и принадлежат новой manager generation.

Watcher запускается до финального подтверждения candidate inputs; повторный
probe и revision comparison закрывают интервал между первым scan и запуском
watcher. События до publication остаются pending. Неудачный watch или событие
graph/membership в этом интервале отменяет disk candidate и ведёт к fallback.
После publication действует текущая live policy, без нового общего freshness layer.

Analyzer/generator preparation получает новую session и реальные bindings.
Старый provenance snapshot не ребиндится простой заменой ID. При отсутствии
допустимого fresh admission весь request идёт на ordinary load, без удаления
references или обхода Banned/Unavailable. Ограниченный generator request
проверяется положительным либо честным fallback контролем; full portable
closure research не становится обязательной задачей этой эпохи.

## Store и поведение

User-private cache directory, один законченный versioned envelope на request key,
checksum/bounds и запись через unique temporary file + atomic replace.
Читатель получает старый или новый целый envelope; concurrency не повреждает их.
Отдельный многопоколенный GC, leases для большого store и repair протокол не нужны.
Capture снимается со стабильной base после prepare; повторный probe отвергает
сдвиг inputs за время capture. Cache I/O failure не превращает успешный ordinary
load в ошибку. Session IDs, ProjectIds, shadow paths и operation contexts не сохраняются.

Предлагаемые параметры `useDiskCache=false` и `forceReload=false` описаны в
[README](../README.md#поведение-первого-mvp). Это proposed API до ревью.
Запись обычных existing `.cs` проходит текущий preflight/exact inverse/persistence;
unsupported add/remove/rename или `.csproj` edits отвергаются до записи на
hydrated host, с указанием выполнить обычную загрузку.

## Приёмка

Новый PID/unchanged hit, изменённый source или graph → miss, corruption/version
mismatch, capture/store failure, cancellation, watcher startup race, shared file,
safe `.cs` write и штатные analyzer lifecycle regressions. Проверки используют
production entry, а не только isolated host. Для сравнения ordinary и hydrate
нужны project/document inventories и заранее выбранные semantic assertions.
Измеряются unchanged hit, forced load и miss overhead; time-to-first-useful
должен улучшаться на поддержанном реальном контроле. Отрицательный результат
фиксируется как неприёмка performance с причиной, без изменения benchmark после замера.

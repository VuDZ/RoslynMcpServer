# task-03 — независимое ревью, раунд 8

- Дата: 2026-11-03.
- Рецензент: Codex, новая независимая сессия; точный runtime model ID недоступен.
- Версия: окончательный CODE FROZEN diff против `31cc8d8` после R7-001; восемь изменённых C# файлов, cached Integration возвращён к базе.
- Scope: R5-001…R5-004, R7-001, связанные callers, поздние фикстуры, фазы evidence и точный inventory.
- Вердикт: **ACCEPT**. Новых must-fix нет.
- Рецензент не запускал runtime и не менял файлов. Итоговая приёмка зависит от обязательных проверок координатора, включая три полных Linux main suite.

## Подтверждённые исправления

- **R5-001:** ожидание уникальной пары путей именно текущего переименования и EventRevision записи; порядок pre-read → directory rename → Semantic_sync закреплён, проверки текста сохранены. Прочие WaitForDirtySourceAsync callers проверены.
- **R5-002:** общий fixture задаёт исходникам A/B и linked Shared контролируемые прошлые mtime. Timestamp/event guards не ослаблены, отдельная cached-only коррекция удалена. Evidence различает доказанный обратный ход UTC в cached-test и неустановленную точную ветку старого startup main failure; контролируемый future-mtime RED не выдаётся за естественное воспроизведение.
- **R5-003:** строгое directory evidence (существующий каталог либо strict mapped descendant), ignored rename paths не входят в baseline journal. Legacy refresh/classification сохранены, unignored extensionless FILE остаётся обычной записью без gap. Настоящий rename_project поднимает gap.
- **R5-004:** разрыв до первой публикации сохраняет захват; journaled paths Unknown, остальные могут стать Known. После публикации Pending по-прежнему замораживается.
- **R7-001:** before/after-start hook snapshots доказывают отсутствие публикации до завершения старта watcher; afterLoad подтверждает опубликованный baseline. Сравнение двух UTC readings удалено.

Точный SourceStructure inventory16 сохраняет все identities/kinds/paths, новых
ограничений нет. `BaselineSessionIntegrationTests.cs` соответствует базе `31cc8d8`.

## Учёт

Task-03: **8 раундов, 8 исправленных уникальных замечаний** — R2-001, R2-002,
R3-001, R5-001, R5-002, R5-003, R5-004, R7-001. Дополнительные попытки исправления
R5-001/R5-002 не увеличивают счётчик. Codex — независимые сессии раундов1,3,4,6,7,8;
Claude Opus5.5 — раунды2,5. Эта приёмка не объявляет завершение эпохи или реализацию
task-04 и публичного инструмента.

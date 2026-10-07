# Epoch 4 — Прикладной индекс символов (опционально)

Status: **planned**. Низкий приоритет. Не начинать, пока Epoch 3 не shipped и
не ясно, что узкое место — `find_symbol_*` после load, а не сам DTB.

## Mission

Ускорить **поиск по имени** после того, как граф уже в памяти (или гидратирован
из Epoch 3), своим SQLite/файлом имён типов и членов, ключ = hash файла из
индекса Epoch 3. Это не замена evaluation snapshot и не ускорение
`OpenSolutionAsync`.

## Prerequisites

- [Epoch 3](epoch-3-evaluation-snapshot.md) shipped: есть content hash файлов.
- Не копировать внутренние Roslyn `SymbolTreeInfo` / IDE SQLite.

## Decisions

- Индекс строить только по **исходным** документам workspace (не `node_modules`,
  не generated `obj/`, пока Roslyn `SymbolFinder` generated всё равно не ищет —
  см. analyzer-shadow-copy / roslyn#63375).
- Инвалидация: hash файла из Epoch 3 изменился → пересканировать этот файл;
  Merkle hit → записи файла можно не перестраивать.
- Не использовать как единственный путь `find_symbol_definition`: при miss
  индекса — обычный `SymbolFinder`. Индекс — ускоритель, не источник истины.
- Формат свой, versioned, тот же каталог кеша или соседний файл. Не коммитить.

## Scope

- Только если замер после Epoch 3 показывает, что load уже быстрый, а
  solution-wide `FindDeclarations` на 100+ проектах всё ещё тяжёлый.
- Тесты: изменение метода → старое имя не находится по индексу после reindex.

## Non-goals

- Повтор Epoch 3.
- Совместимость с VS `.vs`.
- Полноценный IDE Find All References кэш.

## Risks

- Двойная правда (индекс vs Roslyn) → агент находит мёртвое имя. Mitigation:
  индекс только hint, verify через workspace.
- Размер SQLite на монорепе: писать только конус загруженного sln.

## Verification

- Сравнение времени `find_symbol_definition` по частому имени до/после на
  большом sln.
- Смена файла → индекс обновляется с хешем Epoch 3.

## Exit / handoff

- Можно **не делать** эту эпоху, если Epoch 3 закрыл боль пользователя.
- Если shipped: minor или patch по факту поверхности tools; ARCHITECTURE —
  отдельный абзац «optional symbol index», не смешивать с evaluation cache.

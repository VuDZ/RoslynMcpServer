# D/E0/task-08 — Source-only поиск: эквивалентность и офисный результат

- Дата фиксации: 2026-10-22.
- Статус: **accepted in the requested scope, 2026-10-22**.
- Принятый scope: переход на FindSourceDeclarationsAsync, проверки эквивалентности
  и сравнение офисных прогонов 1.5.8/1.5.9 для MainViewModel.
- Исполнитель: Codex; независимое ревью не проводилось.
- Зависимости: [task-02](task-02-design-time-build-summary-med.md) — проектные тайминги.
- Validation и результаты: [отчёт 1.5.9](evidence/task-06-provenance-forward-compatible-replay.md).
- Продолжение: [task-09](task-09-first-search-io-and-private-project-profile-hi.md).

## Что сделано

Общий name/FQN resolver вызывает FindSourceDeclarationsAsync вместо
FindDeclarationsAsync. Сохранены ignoreCase, SymbolFilter, нормализация FQN,
исключение metadata-only результатов, deduplication и порядок обхода/ответов.
Версия сервера — 1.5.9; проектные тайминги появились в 1.5.8.

Проверены 13 сценариев эквивалентности старого и нового API на отдельных свежих
workspace: регистр, FQN, перегрузки, вложенные типы, properties, project references,
linked sources, metadata-only, отсутствие результата и выполненный source generator.
Это сравнение прежнего поведения, а не расширение поддержки generated declarations.

## Полученные результаты

Офисные пакеты 2026-10-22 21:46:17 и 22:15:22, та же машина и MainViewModel,
три measured fresh-process запуска каждой версии, warmup исключён:

- Первый поиск: медиана **36,203 → 34,175 с**, **−5,60%**.
- Load: **48,642 → 49,079 с**, **+0,90%**.
- Медиана load + find внутри прогона: **84,976 → 83,161 с**, **−2,14%**.
- Все четыре ответа поиска, включая warmup, побайтно совпали: четыре объявления,
  одинаковые FQN, locations и порядок. SHA-256:
  `9b4668444194460742ebfab640348ffbc92106b7658dfe77e72fc16df742f837`.
- Промежуточных кандидатов 10 → 4.
- private_project: медиана проектного вызова **8,84 → 12,88 с**;
  private_project_4: **5,23 → 1,04 с**. Эти строки включают ленивую работу
  с зависимостями; их изменение не доказывает регрессию собственных файлов private_project.

Связанная [task-06](task-06-provenance-forward-compatible-replay-med.md)
исправила replay: оба офисных binlog прочитаны, второй — format 27 /
49 project starts; capture Complete, 285 confirmed bindings.

## Проверки и границы

Release solution build, main CI **1426 passed / 0 failed / 0 skipped**,
SourceStructure **123**, AnalyzerLifecycle **13/0/0**, PowerShell self-check и
опубликованный сервер со скриптом на Smoke fixture — PASS; receipts в отчёте.
Новый предкоммитный checkpoint фиксируется [отдельно](evidence/commit-2026-10-22.md).

Результат принят для реализованной замены API и измеренного запроса.
Статистическая универсальность выигрыша, остальные офисные запросы,
завершение E0/E1 и production активация параллельной загрузки не заявляются.

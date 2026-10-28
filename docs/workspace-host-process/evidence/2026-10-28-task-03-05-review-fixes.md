# Исправления по ревью task-03 и task-05, 2026-10-28

- Статус: **выполнено, 2026-10-28**, только документация.
- Основание: [ревью task-03 и task-05](../review-task-03-05-2026-10-28/result.md),
  F-01–F-09; согласие владельца применить исправления.
- База: `65bbc94`, сервер 1.5.18; production-код и код стендов не менялись.
- Рецензент и исполнитель исправлений: `claude-opus-5-5` (Claude Code), одна сессия.

## Изменения

- **Task-05, F-01–F-04.** [Результат](../epoch-0-design-and-spike/evidence/task-05-applicability.md):
  строки reset помечены как замер без GC, добавлены проверка ревью с aggressive GC
  и поправка оценки idle unload; раздел «Рекомендация по моделям после ревью»;
  два новых unknown; ссылки на локальный профиль Codex заменены текстом;
  `Program.cs:16` → `:15`; пояснение к наблюдению ~18 МБ.
- **Task-03, F-05–F-09.** [Результат](../epoch-0-design-and-spike/evidence/task-03-pipe-relay-spike.md):
  сравнение proxy с пустым сервером и замечание о режиме GC в модели ролей;
  уточнение Linux-проверки доступа; установленная часть причины C03-01 и смена
  получателя на task-04; unknowns `TMPDIR`/длина пути и peer UID; список значений
  стенда, не переносимых в production.
- Входы: [task-04](../epoch-0-design-and-spike/task-04-proxy-routing-and-recovery-spike-hi.md)
  получил входы F-02 и F-06–F-09; [task-01](../epoch-0-design-and-spike/task-01-architecture-decision-hi.md),
  п. 1 — вопрос владельцу о требовании чистого proxy; «Главное» task-00, п. 1 — поправка.
- [README темы](../README.md): цель Reload для Codex, вопрос о чистом proxy, U-C-06.
- Учёт: шапки task-03 и task-05, статистика карты E0, «Учёт» README темы,
  строка `docs/README.md`.

Выбор моделей, Depends on, статусы задач и разрешения на реализацию не менялись.
Решения по требованию чистого proxy и по цели Reload для Codex остаются за владельцем.

## Validation

- Проверка ревью с GC — [данные и метод](../review-task-03-05-2026-10-28/coverage.md).
- Markdown: 551 локальная ссылка и anchor во всех документах темы, `docs/README.md`
  и README стендов — битых 0; 19 ссылок вида `file:line` проверены как существующие файлы.
- `git diff --check` — PASS.
- Build и тесты не запускались: изменены только Markdown-файлы, данные ревью в JSON
  и `.status`.

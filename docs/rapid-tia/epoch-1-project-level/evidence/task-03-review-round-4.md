# task-03 — независимое ревью, раунд 4

- Дата: 2026-11-03.
- Рецензент: Codex, новая независимая сессия; точный runtime model ID недоступен.
- Версия: окончательный diff против `89c1bd1` после CODE RE-FROZEN, включая изоляцию тестов.
- Scope: task-03 и закрытие R2-001, R2-002, R3-001; связанные callers, тесты, фазы evidence и точный структурный inventory.
- Вердикт: **ACCEPT**. Новых must-fix нет.
- Рецензент самостоятельно не запускал runtime проверки и не менял файлов. Финальная validation принадлежит координатору.

## Закрытые замечания

- **R2-001:** синхронный сигнал потери наблюдения выполняется внутри session
  admission/closure, включая прямой вызов `rename_project`. Known сохраняется,
  Pending становится Unknown, предпубликационный флаг переживает инициализацию.
  Новый baseline не наследует разрыв другой solution, закрытая сессия его не меняет.
- **R2-002:** обычная публикация и её timestamp следуют после запуска watcher;
  промежуточные admitted writes сохраняются в provisional journal.
- **R3-001:** matching `[Collection("BaselineObservationGap")]` и публичная
  `CollectionDefinition` с `DisableParallelization=true` изолируют глобальную
  инъекцию сбоя от параллельных загрузок других коллекций.

RED/guard-фазы описаны честно: held startup boundary детерминированно доказывает
старый дефект порядка; event guard не выдаётся за независимое доказательство этой
гонки. Поздняя изоляция тестов обозначена отдельно, искусственный RED не заявлен.

Точный inventory16 сохраняет прежние 13 и три новых предела:
BeforeDiskWatcherStartForTests, AfterDiskWatcherStartForTests и onObservationGap,
все TargetOutsideTheScope, с проверкой индивидуальных source paths.

## Учёт

Для task-03 завершены **4 раунда, 3 исправленных замечания**: R2-001, R2-002, R3-001.
Раунд 1 — Codex ACCEPT; раунд 2 — Claude Opus 5.5 MUST-FIX; раунд 3 — Codex MUST-FIX;
раунд 4 — новая сессия Codex ACCEPT. Эта приёмка не объявляет завершение эпохи,
реализацию task-04 или публичную активацию инструмента.

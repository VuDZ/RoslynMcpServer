# Эпоха 3. Гонки, ошибки и интеграционная приёмка

Статус: **не начата**. Требует [эпохи 2](_archive/epoch-2-solution-build-before-tests.md) и общего pull provider watcher-серии.

## Цель и работа

- Проверить input edit до build, во время build и между build success и
  стартом tests. При обнаруженной гонке не запускать неподтверждённую DLL;
  повторная сборка ограничена retry policy и общим timeout.
- Сериализовать конфликтующие build/test операции с общими outputs либо
  явно отказать. Внутренний gate не объявлять блокировкой внешнего build.
  Не удерживать workspace semaphore на всём CLI-процессе; короткая проверка
  generation и commit state не должна создавать recursive acquire/deadlock.
- Старый build после reset/reload не подтверждает новую сессию. При
  неизвестном эффекте незавершённого процесса affected outputs остаются unknown.
- Проверить overflow/startup failure/coverage unknown, own MCP write и
  semantic flush между edit и tests. Изменение не теряется из build-state.
- Проверить общий generation token WPF/input provider/build proof: старый
  callback/result после reload не влияет на новую сессию. Нет второго session
  counter или прямого consumer callback на watcher-потоке.
- Проверить BuildProjectReferences=false, неполный graph и solution mapping:
  success верхнего target не очищает все reachable проекты без evidence.
- Build outputs и WPF-временные проекты не вызывают бесконечный dirty loop.
  Явные significant generated inputs не исключаются без классификации роли.

## Интеграционная матрица

1. WPF-Solution, внешний проект и XAML change: solution build → DLL tests.
2. Linked-файл вне watcher project roots и shared-файл в A/B: изменения
   доставлены; тесты A и B требуют своих актуальных outputs.
3. Transitive dependency edit, unrelated project edit и новая glob membership:
   наблюдаемое решение о build соответствует scope и completeness evidence.
4. Restart/reset/new configuration/TFM/args: старое подтверждение не используется.
5. Failed/cancelled/timed-out build, исчезнувший/заменённый output: tests
   не стартуют со старой или неподтверждённой сборкой.
6. Silent missed event при проверяемом профиле обнаруживается; unsupported
   input/custom task profile остаётся на always-build fallback.
7. Параллельные tests/build/reset и изменение во время подготовки фильтра:
   нет deadlock, старой публикации и ложного `current`.
8. XAML/AdditionalFile/import edit до появления consumer, semantic flush,
   первый pull и два consumer: revisions/roles/unknown сохранены, чтение одного
   не потребляет состояние другого. При pending событии skip не разрешён.
9. Generated-output-only событие не повышает input revisions производящего build;
   новая glob membership/metadata-only dependency с неизвестным evidence
   сохраняет conservative stale/unknown. Успешный build его сам не снимает.

## Приёмка и завершение

Отчёт содержит SDK/runtime, real fixture, команды, причины build/reuse,
input scope, выполненные fault/race проверки и известные ограничения.
Измерить стоимость unchanged validation и первый/повторный DLL-запуск;
численный performance budget заранее не выдумывать.

Фактические descriptions/help/README/ARCHITECTURE и version bump обновлять
при выпуске поведения. Persistent cache остаётся отдельной задачей.
Планы эпох не помечать выполненными только по зелёным юнит-тестам модели.

# BuildHost/task-02 — Направленный эксперимент оптимизации

- Дата: 2026-10-22.
- Статус: **planned; кандидат ещё не выбран, реализация не начата**.
- Сложность: hi; риск — ускорить загрузку ценой неверного workspace.
- Depends on: [task-01](task-01-buildhost-graph-and-timings-hi.md).
- Evidence/validation: пока отсутствуют.

## Выбор кандидата

По профилю task-01 до кода зафиксировать один hotspot, механизм, ожидаемое
изменение и критерий go/no-go. Возможные кандидаты перечислены в [README](README.md):
reuse измеренных query/evaluation results, пакетный RPC, параллельная обработка
в общей сессии, сокращение доказанно лишних targets/tasks.
Реализовать один кандидат, измерить его отдельно; следующий — отдельным шагом
после результата. Не накапливать несколько непроверенных оптимизаций.

## Ограничения корректности

- Ключ reuse учитывает effective properties, SDK/toolset, TFM и состояние входов.
  Одинаковый path не доказывает одинаковую задачу.
- Параллельная работа не изменяет общий mutable Project между TFM; защищает
  shared obj/output paths, сохраняет каждый solution entry и правила native entries.
- Не отбрасывать generated sources, compiler args, references/aliases, diagnostics,
  analyzer evidence, cancellation и failure propagation ради измеренного выигрыша.
- Prewarm и долгоживущий процесс считать отдельными режимами; их цену не скрывать
  до таймера. Построение графа, запуск хостов, extraction и merge входят в wall.
- Обычный fallback после потраченного времени fork не превращает failed attempt
  в correctness PASS или ускорение.

## Эксперимент и решение

Сравнить исходный package, baseline fork без оптимизации и fork с кандидатом:
те же публичные fixtures (PowerToys и полный Roslyn), параметры и запросы.
Warmup + минимум три измеренных fresh-process запуска на вариант; порядок
чередовать. Отдельно first query и load + first useful query, CPU, peak RAM,
GC при доступности, errors/timeouts и child cleanup. Офисный прогон — следующий
внешний контроль, не заменяет публичные fixtures.

До реализации назвать численный порог полезного выигрыша и допустимой регрессии
контрольных решений/RAM; пороги не подгонять после результата. Сравнивать одинаковые
составы workspace и полный elapsed, а не сумму inclusive targets.

Итог: go/no-go/inconclusive с объяснением и следующим узким шагом.
Production включение не входит в эту задачу: даже успешный spike требует отдельного
решения об интеграции и совместимости. Отчёт: evidence/task-02-optimization-spike.md.

Перед code/tests commit — проверки task-00 и AGENTS.md. В отчёте явно сохранить
failures/skips/unavailable environments, после выполнения обновить статус здесь
и в README. Отрицательный результат не удалять и не переписывать.

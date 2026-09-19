# Unresolved arbitration issues

Статус: **открытые продуктовые вопросы, не blockers следующей ревизии
Stage 0/2**. Этот документ сохраняет решения за пределами арбитража без
выбора варианта. Не превращать их молча в требования Stage 2.

Источник: [_archive/arbitration/unresolved.md](_archive/arbitration/unresolved.md).

## U-ARB-01 — Pin .NET SDK репозитория

Finding: E0-01 — ACCEPT WITH MODIFICATION.

Точное pin-значение SDK для всего репозитория (`global.json`) и политика
обновления patch-версии не заданы исходными требованиями. Stage 0 требует
явный `10.0.x` в CI; более строгая воспроизводимость — отдельное решение.

## U-ARB-02 — Переносимый или durable `symbolId`

Findings: E2-01, E2-04 — ACCEPT WITH MODIFICATION.

Переносимые между процессами/загрузками ID или сохранение ID после
изменения документа объявления исходные требования не задают. Текущая
норма fail-closed. Если это станет продуктовым требованием, понадобится
отдельный механизм tracking и проверка его точности — не условие Stage 2.

## U-ARB-03 — Миграция legacy filePath-tools

Finding: E2-03 — ACCEPT WITH MODIFICATION.

Stage 2 обеспечивает точность новых ID/location-режимов и безопасную
запись в общий путь. Изменение старого first-hit поведения всех
существующих filePath-tools потребует отдельного совместимого решения.

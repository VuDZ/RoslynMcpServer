# Epoch N — Handoff

> Шаблон. Копировать в `epoch-N-handoff.md` после работы. Не отмечать proposed
> эпоху как shipped по факту наличия этого файла.

## Решение

- Outcome: go / revise / stop.
- Статус результата: experiment / implemented / shipped.
- Commit/версия реализации и дата:
- Следующая разрешённая эпоха:

## Реализованное поведение

Описать фактический путь load/hydrate/fallback, defaults и effective metadata.
Для эксперимента явно написать, что production runtime не изменён.

## Границы поддержки

- Поддержанные SDK/targets, язык и features:
- Как определяется eligible/unsupported:
- Источники DTB provenance и используемые публичные API:
- Учтённые positive/negative dependencies:
- Неподдержанные случаи и поведение fallback:

## Контракты и отклонения

- Schema/producer version, ключ и compatibility fingerprint:
- Каталог и retention:
- Политика strict/non-strict и модель конкурентных изменений:
- Lifecycle/overlay/write-path изменения:
- Отклонения от плана, основания и последствия:

## Проверки

| Test ID / команда | Результат | Артефакт или причина пропуска |
|---|---|---|
| Заполнить | passed / failed / not run / unsupported | Заполнить |

Не смешивать unsupported fixture и успешно пройденную equivalence-проверку.
Приложить воспроизводимые команды и местоположение отчётов.

## Производительность

- Решение, оборудование, версии и параметры:
- Число запусков, raw results:
- Median/p95 load и first semantic, DTB count, peak memory, bytes hashed:
- Заранее установленный budget и результат проверки:
- Неразрешённые ограничения замеров:

## Переход дальше

Оставшиеся blockers, конкретные решения следующей эпохи, документация продукта
и versioning, которые были реально обновлены. Не включать будущие обещания в
описание текущего runtime.

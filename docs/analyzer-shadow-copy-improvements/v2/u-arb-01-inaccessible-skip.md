# U-ARB-01 — inaccessible original: выбран skip

Дата: 2026-09-12. Версия продукта: **v1.3.21**.
Статус: **выбрано — skip.** Решение владельца, не новый ресёч.
Missing/foreign по U-ARB-01 по-прежнему: capture + confirmed-only
([UNRESOLVED-v2.md](UNRESOLVED-v2.md)). Эта запись закрывает только ветку
**inaccessible original path**.

## Что это

Provenance item известен (или нет), но **прочитать original
`AnalyzerReference.FullPath` нельзя** (`UnauthorizedAccessException` /
`IOException` на probe). Это не missing file и не failed/corrupt capture
(S4). Capture читает design-time metadata, не байты DLL.

## Выбранное действие: skip

- Не rewrite: не подставлять resolved output подтверждённого проекта.
- Не fail-closed на весь snapshot: inaccessible original не делает
  confirmed overlay candidate.
- Исходная ссылка остаётся как есть. `reasonCode=access_failure`,
  `originalPathState=AccessFailure`.
- Load summary явно пишет, что путь **skipped (U-ARB-01 skip; not missing;
  not rewritten)**. Если confirmed project известен, он назван и помечен
  как не использованный для rewrite.
- Не путать с inaccessible **source output** (нельзя скопировать выбранный
  DLL): там по-прежнему prepare/publication failure confirmed overlay.

## Почему не rewrite и не fail-closed

Rewrite подменил бы файл, на который указал MSBuild, другим output того же
проекта. Это может быть правильно в поле, но это уже другая идентичность
байтов. Fail-closed резал бы весь semantic snapshot из‑за одного
залоченного analyzer — слишком жёстко относительно missing/foreign skip.

Skip честен: мы знаем, что путь недоступен, и не притворяемся, что
генерация восстановлена.

## Альтернативы (не внедрены)

| Вариант | Следствие |
| --- | --- |
| **skip** (текущий) | Original сохранён; overlay нет; диагностика `access_failure` |
| **rewrite** | Shadow из confirmed project output, даже если item-path недоступен |
| **fail-closed** | Unavailable/Banned всего snapshot, как S4 |

Alt-2/Alt-3 из U-ARB-01 (unique-name / exact-path only) **сюда не относятся**:
это запас, если бы capture провалился. Capture принят.

## Смена решения

Политика **может быть изменена** после наблюдений на реальных репозиториях
(ACL на `artifacts`, locked generator output, корпоративные фильтры).
Смена — отдельное решение владельца с записью здесь **до** смены matcher
и тестов. Не менять skip «по пути», не приравнивать inaccessible к missing
и не включать rewrite молча.

Точки: [epoch-5-reference-provenance.md](epoch-5-reference-provenance.md),
[ARCHITECTURE.md](../../ARCHITECTURE.md), README `load_workspace`.

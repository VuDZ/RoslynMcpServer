# Independent Review

Дата: **2026-10-07**. Рецензент: Codex, один независимый агент; субагенты не запускались. `review-astra` — заданное имя каталога, не подтверждение точного model ID. Точный model ID недоступен в контексте сессии.

Scope: общий пакет workspace-state-cache, E0–E3 specs/карты и 33 task contracts. Review завершён; это не acceptance реализации или разрешение activation. Evidence: [coverage.md](coverage.md), [validation.md](validation.md), [снимок проверенных файлов](target-snapshot.json).

## Verdict

Постепенный маршрут E0→E1→E2 с optional E3 выглядит обоснованным. Перед принятием соответствующих production contracts требуется уточнить generation binding исходного capture, environment policy и предпосылки metadata refresh. Выявлены три существенных замечания; фундаментальная невозможность всего подхода не установлена.

## Summary

Обнаруженные риски относятся к условиям разрешения cache reuse. Повторная проверка текущих файлов не доказывает их соответствие ранее evaluated graph; новый PID не гарантирует прежнее evaluation environment; неизменные reference paths не доказывают неизменность MSBuild resolution после замены DLL.

У пакета уже есть полезные ограничения: явный binary blind spot, whole-request fallback, запрет переносить session/provenance как готовое admission, отдельная functional/performance приёмка и no-go для selective spike. Эти ограничения не выданы за defects.

## Findings

| ID | Severity | Category | Title |
|----|----------|----------|-------|
| [F-001](findings/F-001.md) | major | correctness | Post-load probe не связывает manifest с поколением evaluated graph |
| [F-002](findings/F-002.md) | major | requirements | Не определена валидация evaluation environment между PID |
| [F-003](findings/F-003.md) | major | correctness | Неизменность metadata reference inventory нельзя вывести из неизменности путей |

0 blocker, 3 major, 0 minor, 0 suggestion. F-001/F-002 относятся к E1 и последующим эпохам; F-003 — к расширению E2. Общие ссылки на задачи не создают дополнительные findings. Исправления не выполнялись.

## Highest risks

- Долгоживущий ложный disk hit: envelope объединяет старый graph и hashes новых graph inputs.
- Отличие cached parse/options от ordinary load при другом окружении нового процесса.
- Отличие metadata symbols/diagnostics от ordinary resolution при замене DLL.

## Questions / uncertainties

- Чем подтверждается принадлежность captured options/memberships тому же input generation, что manifest?
- Какова environment policy и как current effective values проверяются до reuse?
- Какие metadata resolution cases действительно допускает E2 support profile?
- Выбранные real positive control, representability options и fresh analyzer admission ещё требуют предусмотренных самим пакетом implementation evidence; review не объявляет их доказанными.

## Suggested spikes

Для F-001 нужен детерминированный negative race control вокруг consumption props и capture. Для F-002 достаточно isolated restart control со сменой одного evaluation property из environment. Для F-003 узкий design-time resolution spike нужен только при желании поддержать exact-version binary mutations без fallback. Отдельная большая closure-программа не предлагается.

## Overall assessment

Обоснованы staged delivery, explicit unsupported outcomes, единый manager ownership и использование текущих write/admission механизмов. Нужны точечные изменения перечисленных contracts и проверяемые границы профиля. Выбор environment policy остаётся за автором/владельцем; review не выполняет defense или arbitration. E0 isolated round-trip можно оценивать отдельно от production готовности E1. Требования E3 не объявлены prerequisite для уже принятого E1/E2 scope.

Исходные requirements и их execution statuses не менялись: в этой сессии пользователь ограничил результаты собственным review directory. Этот каталог фиксирует завершение только независимого review; обновление общей статистики пакета остаётся координатору последующей стадии.

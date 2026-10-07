# E1/task-05 — Preparation новой hydrated сессии

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-00](task-00-input-manifest-hi.md), [E1/task-04](task-04-manager-host-ownership-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Подключить восстановленную base к текущим analyzer/generator preparation и admission с новой session.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Только adapter к existing Services/Analyzers и manager prepare seam; полный SDK/analyzer dynamic closure audit не входит; gate semantics не ослабляются.

## Конкретный результат

- Scoped fresh binding adapter либо явный support/fallback classifier
- evidence/task-05-preparation.md с ordinary/hydrated controls.

## Проверки

- DTO не становится Complete provenance простой сменой ID
- Banned/Unavailable и restart-required сохраняются
- поддержанный generator работает с актуальными bindings или request целиком fallback
- references не удаляются ради hit
- sticky overlay matrix проходит
- требуется жизнеспособный real positive E1 control.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-003, P-009**; [change ledger](../change-ledger.md).

Указать mode support matrix. Без доказанного fresh binding overlay-on уходит на whole-request ordinary load до disk publication; это достаточный E1 outcome. Не вводить permanent запрет будущего adapter/обязательный overlay hit. Cache attempt не создаёт artificial Unavailable для пригодного ordinary; реальные bans/restart-required остаются. New host сохраняет single apply site.

## Решение владельца H-001

[H-001](../human-decisions.md): XML documentation provider/text metadata/DLL references
вне MVP equivalence; source comments загруженных проектов и остальные обязательные
metadata/options facts сохраняются. Это объявленное исключение, не silent loss.
Documentation-dependent consumers получают ordinary route либо явный отказ до execution/
side effects. Применимые controls/compare key отражают эту границу.

## Решение владельца H-002

[H-002](../human-decisions.md#h-002--ограниченный-strong-name-contract-mvp), 2026-10-07:
U-001 решён, выбран B. Поддерживаются только подтверждённые signing-independent
проекты/операции; signing-dependent и unknown cases используют ordinary load до
execution/side effects (при невозможности перехода — явный отказ). Emit/signing из
hydrated compilation вне первоначального scope. Semantic/write correctness и
remaining mandatory state сохраняются; signing flags/csproj/refs не меняются ради pass.
Проверки допуска учитывают effective options, attributes и project bindings;
один symbol query/SignAssembly=false не доказывает независимость. S-001 проверяет
real positive и excluded/unknown controls; runtime policy ещё не реализована.
Execution/review statistics остаются незаполненными.
Применение решения: [H-002 report](../h-002-report.md).

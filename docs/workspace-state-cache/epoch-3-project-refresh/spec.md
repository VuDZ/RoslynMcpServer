# E3 — Выборочное обновление проектов

Статус: **spec-v2 / revised-for-review, 2026-10-07**. Реализация не начата.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат и первый шаг

Начать с узкого исполняемого spike: открыть изменённый проект штатным MSBuild
loader и встроить результат в hydrated graph без потери references, options,
memberships, analyzer bindings и session ownership. Предыдущая эпоха остаётся
полезной, даже если spike даст no-go.

Task-00 выдаёт `supported` для конкретного среза либо документированный `deferred`
с причиной. При no-go следующие selective implementation tasks отмечаются
deferred; существующий whole-request fallback сохраняется. Нельзя объявить
выборочную перезагрузку реализованной по одному DTO merge happy path.

## Dirty и зависимые проекты

Изменённый общий import принадлежит всем его потребителям. Linked input может
принадлежать нескольким проектам. Изменение public shape проекта инвалидирует
semantic results его consumers по reverse project-reference graph; это не
означает безусловный DTB всех consumers. Повторная evaluation нужна, если их
собственные evaluated inputs/edges/options зависят от изменения.
Loaded inner-TFM identities и directed references должны ребиндиться однозначно.

Membership add/delete/rename делает project dirty и решается новой MSBuild
evaluation проекта. Самодельный интерпретатор cached globs не вводится.
Изменённая solution topology, неоднозначный TFM, unresolved external glob,
неподдержанные target effects или неполное merge evidence ведут к ordinary
whole-request load. Новые ignored-tree files не теряются, когда у них explicit role.

## Publication и failure

Selective candidate строится вне published state, затем manager под своим lock
проверяет inputs/revisions, новые analyzer/session bindings и публикует целый
graph. Отмена или ошибка сохраняет пригодную прежнюю same-key сессию либо
завершает fallback; частичный merge не публикуется. Retained projects не получают
foreign provenance/operation contexts. Existing write boundary остаётся обязательной.

## Приёмка

Минимум: два связанных проекта; edit csproj/props одного; add/delete/rename `.cs`;
shared input; transitively consumed symbol; не затронутый проект; changed-during-
refresh; cancellation и failure. Результат сравнивается со fresh ordinary load.
Open/DTB counters относятся к реально загруженным проектам, а не к флагу ответа.
Бенчмарк показывает стоимость selective refresh относительно whole-request load,
включая validation и первый полезный запрос. При отсутствии выигрыша scope
остаётся deferred/experimental; E1/E2 не теряют свою принятую функциональность.

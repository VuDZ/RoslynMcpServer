# E2 — Обновление существующих входов

Статус: **draft / ready-for-review, 2026-10-07**. Реализация не начата.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат

Стартовый diff выделяет изменения содержимого существующих входов отдельно
от изменения состава/настроек проекта. Неизменные graph inputs позволяют
восстановить base и применить новые тексты без ordinary MSBuild open.
Для custom targets/conditions, зависящих от source bytes, это правило
не распространяется автоматически: неподдержанный случай ведёт к fallback.

## Правила dirty

- Existing `.cs` content: один read physical path, обновить все memberships,
  перечитать encoding; semantic consumers получают новый snapshot.
- Existing AdditionalFiles/analyzer-config text: обновить текущими механизмами,
  если путь не имеет также evaluation role; генераторы повторно используют
  актуальные inputs по текущему execution gate. Новые/удалённые configs дают fallback.
- Изменённый metadata binary: создать свежий reference с текущими bytes и
  сохранёнными reference properties. Путь/набор references изменился — graph dirty.
- Analyzer/generator binary: reprepare по текущим lifecycle rules либо ordinary
  fallback/restart-required; выборочный binary hash не разрешает stale execution.
- Graph inputs, membership, missing files, неизвестная роль и unreadable inputs:
  whole-request ordinary load с причиной. Dirty проекты вычисляются по всем
  владельцам path/region, даже когда обновление выполняется общим fallback.

Не вводить второй live watcher/index. Применяется стартовый diff manifest и
existing reconcile/publication; неизвестность самого cache-профиля не превращает
старые standing coverage gaps в новый запрет всех live reads.

## Capture после refresh

Восстановленный candidate проверяется перед publication и может заменить disk
envelope в рамках успешного load. Частая запись на каждый watcher event и
background checkpoint scheduler в scope не входят. Неуспех записи нового
envelope оставляет успешный load и прежний законченный envelope на диске.

## Приёмка

Source edit offline → disk graph reuse без Open*/DTB; shared source получает
одинаковый text во всех memberships. Additional/config и metadata edits имеют
положительные role-specific проверки. Create/delete/rename source, новый
Directory.Build.props, restore/import change и ambiguous role дают fallback.
Mutation сравнивается со свежей ordinary загрузкой по независимым semantic assertions.
Бенчмарки single/batch content edit → restart и build → restart различают
фактический hit/miss и реальные изменившиеся inputs.

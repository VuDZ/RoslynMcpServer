# Epoch 0 — Feasibility, profile и baseline

Статус: **proposed experiment; production hit запрещён**.

## Задача

До production реализации доказать выполнимость host, persistence, dependency
admission, semantic equivalence и измерений. Fixture может разрешить продолжить
experiment, но не public activation.

## Обязательная работа

1. Зафиксировать commit `9867318ddb5294ce144bf024b9a61a1a2e3814c3`,
   source 1.3.21, отдельно running MCP, Roslyn/MSBuild/SDK, hardware и параметры.
2. Первым gate выбрать production hydrate host, который восстанавливает DTO без
   скрытого DTB. `OpenSolutionAsync`/`OpenProjectAsync` с последующей подменой
   не считается skip-DTB.
3. Построить operation/capability matrix: read, text edit,
   add/remove/rename document, project mutation, disk reconciliation; указать
   host capability, writer, preflight, result/failure/partial и reload.
4. Не принимать молча read-only downgrade, custom project writer или
   reload-on-write. Если current public scope/A-WRITE не сохраняется,
   production feasibility закрыта отрицательно до отдельного решения U-ARB-05.
5. Для semantic DTO и dependency evidence назвать отдельные public/supported
   источники. Проверить positive, known-absent, wildcard regions и target inputs.
   Binlog/evaluation не объявлять полными без evidence.
6. Versioned detector до hit различает supported, unsupported и unknown.
   Unknown отвергает весь request. Если закрытость profile не доказана,
   действует U-ARB-06 и production hit запрещён.
7. Сравнить candidate production host с независимым fresh MSBuild oracle.
   Для supported generator проверить полный generated document set, texts,
   marker/constant и diagnostics; unavailable output = failed/not-run.
8. Metadata fast-open исследовать как отдельный O7 comparator с собственным
   outcome и измерениями. Его результат не является cache go и не меняет
   full-source profile.
9. Назвать target solution и обязательные SDK/TFM/instances/generators/imports.
   Если target multi-target, exact inner mapping становится gate. До решения
   численного workload/budget действует U-ARB-03.
10. Измерить end-to-end: probes, capture, optional prepare, compilation и первый
    полезный semantic call. Порог 70% из прежнего proposal — только предложение,
    не нормативный budget.

Trace: R-01, R-02, R-03, R-06, E0-01, E0-02, E0-03, E0-04, E0-05,
V-03, V-04.

## Приёмка

- Host и capability/write contract выбраны либо production route остановлен.
- Есть две таблицы: hydration data и admission evidence.
- Для каждого profile отдельно записаны:
  `positive equivalence passed`, `negative admission passed`, `not run`.
- Unknown condition/custom target и absent path becoming present отвергаются
  до hydrate/publication; unsupported не засчитывается как equivalence.
- Fresh MSBuild oracle сравнивает graph, options, memberships, references,
  diagnostics, navigation, generated output, encoding и persistence.
- Functional fixture без target workload разрешает только `experiment allowed`.
- Workload и budget утверждены до результатов для `public activation allowed`.

## Handoff

Использовать общий [handoff template](handoff-template.md). Отдельно указать
verdict для experiment, implementation, public activation, next epoch и series
completion. Epoch 1 нельзя открывать как production route без host/persistence
решения; metadata comparator имеет отдельный verdict.

Trace: H-01 — ACCEPT WITH MODIFICATION.

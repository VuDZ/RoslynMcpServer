# Independent Review — workspace state cache (review-ds)

## Verdict

Пакет проработан заметно глубже обычного плана: границы MVP честно названы, эвристики
(бинарная политика `mvp-fast`, неполнота произвольного MSBuild-графа, отсутствие GC store)
объявлены явно, деградация к ordinary load описана как штатный путь, а не как исключение.
Существенных противоречий с действующими production-инвариантами не найдено.

Тем не менее proposal не готов к реализации без правок: не определён контракт попадания
в disk cache (F-001), не заявлено и не измерено ограничение overlay-режима (F-002),
обязательный «реальный положительный» результат может оказаться недостижимым без
дополнительного gate (F-003), а вердикт и coverage ordinary загрузки не переносятся в
envelope (F-004). Четыре minor-finding касаются owner-ства флага включения, судьбы
E0-артефактов, структурного write-boundary контракта и role evidence входов.

Blocker-находок нет: каждое из major-замечаний закрывается локальной правкой spec/tasks,
без изменения выбранного направления (disk snapshot + index inputs + fallback).

## Summary

- Проверялись 4 spec, 4 execution README, 33 task-файла, общие README/execution/benchmark/
  preparation-report, а также production seams, на которые они ссылаются, и отчёт
  прежнего hydrate-эксперимента как источник технических ограничений.
- Выбранный маршрут (E0 round-trip → E1 disk MVP → E2 content refresh → E3 selective
  reopen) согласован: зависимости ацикличны, каждая эпоха имеет собственный результат и
  benchmark, E3 отделён от готовности E1/E2, production write/analyzer/publication
  инварианты не ослабляются.
- Главные риски лежат не в «архитектуре кэша», а в стыках: ключ запроса, analyzer
  provenance, вердикт/покрытие загрузки и роль входов — то есть там, где поведение hit
  должно быть неотличимо от ordinary загрузки.
- Бинарная эвристика `mvp-fast` с документированным blind spot и отсутствие GC store —
  принятые ограничения MVP, отдельных findings по ним нет (см. «Highest risks»).

## Findings

| ID | Severity | Category | Title |
|----|----------|----------|-------|
| F-001 | major | correctness | Ключ disk cache не определяет сравнение omitted и effective load properties в новом PID |
| F-002 | major | integration | Overlay mode не может получить fresh provenance на hydrated host: disk hit недостижим и не заявлен как ограничение |
| F-003 | major | requirements | Обязательный real positive capture может быть недостижим: нет gate и contingency |
| F-004 | major | correctness | Envelope не переносит verdict/completeness ordinary загрузки, а hydrated host выводит их из отсутствующих MSBuild diagnostics |
| F-005 | minor | scope | Config-driven lazy load не может включить disk cache: путь и владелец флага не определены |
| F-006 | minor | maintainability | E0 валидирует DTO/codec в Tests assembly, а E1 поставляет другую реализацию |
| F-007 | minor | testability | Host seam не учитывает единственную разрешённую точку Workspace.TryApplyChanges |
| F-008 | minor | correctness | Role evidence документов (IsGenerated) не переносится из DTO в hydrated input map |

## Highest risks

1. **Hit, не эквивалентный ordinary загрузке** (F-001, F-004, F-008). Все три проблемы
   проявляются одинаково: `load_workspace` сообщает успех и `baseGraphSource=disk`, а
   наблюдаемое состояние (эффективные свойства, вердикт загрузки, coverage, роли входов)
   отличается от того, что дала бы обычная загрузка. Диагностировать это постфактум
   дорого, потому что кэш не оставляет следа о том, чем hit отличался.
2. **Overlay mode и disk cache несовместимы по построению** (F-002). Режим
   `shadowCopyInSolutionAnalyzers` — документированный и session-sticky; для него hit
   невозможен, пока fresh admission получают только через MSBuild open. Без явного
   ограничения это выглядит как «кэш сломан» и портит доверие к метрикам E1.
3. **Эпоха может остановиться после реализации** (F-003). Обязательный реальный
   положительный результат зависит от public API representability, по которой уже есть
   документированные отказы. Проверка этого факта стоит один ordinary capture, но
   запланирована как часть обычной работы задачи, а не как gate.
4. **Непринятые ограничения примут за дефекты** (сопутствующий риск): `mvp-fast` blind
   spot, отсутствие eviction/GC, отсутствие multi-TFM capture. В пакете они названы, но
   не сведены в один список пределов MVP рядом с API/ответом; при эксплуатации это
   превращается в поток «багов» на ожидаемое поведение.

## Questions / uncertainties

1. Что именно означает «фактически effective load properties» в ключе disk cache, если
   запрос опускает configuration/platform/TFM, и допустимо ли попадание в envelope,
   снятый с явным значением? (F-001)
2. Является ли disk hit в overlay mode обязательным сценарием MVP или документированным
   ограничением? Если обязательным — какой механизм даёт fresh admission без MSBuild
   open? (F-002)
3. Владелец решения: допустима ли потеря `DocumentationProvider`/`StrongNameProvider` при
   пересоздании reference с диска, и считается ли это representability-ограничением, а не
   silent default? (F-003)
4. Поддерживается ли config-driven lazy load (`RoslynMcp.jsonc: workspace-path`) в E1, или
   MVP ограничен явным `load_workspace`? (F-005)
5. Переносятся ли артефакты E0 в production или E1 реализует codec заново и повторяет его
   malformed/bounds suite? (F-006)
6. Строится ли input map hydrated host из DTO целиком или переиспользованием
   `WorkspaceInputMapBuilder` на восстановленном Solution? (F-008)
7. Какой реальный контроль выбирается в E0/task-00 и какие у него подпись сборки, XML-doc
   зависимости, in-solution analyzers и multi-TFM? (F-002, F-003)

## Suggested spikes

- **Capture-ability gate (обязателен до E0/task-02…06).** Снять ordinary capture одного
  реального контроля на текущем HEAD/Roslyn 5.9.0 и записать точный набор отказов:
  `unsupported-options` (StrongName), reference-level `DocumentationProvider`,
  same-path inner-TFM ambiguity. Результат — либо подтверждение поддержанного среза, либо
  возврат в spec. (F-003)
- **Проба «hit ≡ ordinary» на одном решении.** Сравнить для одного неизменного дерева:
  эффективные свойства, вердикт загрузки/диагностики, `loadGraphComplete`, coverage
  reasons, роли входов и input map — ordinary против disk hit. Это одна интеграционная
  проверка, закрывающая F-001, F-004 и F-008 одновременно.
- **Проба overlay + disk.** `useDiskCache=true` + `shadowCopyInSolutionAnalyzers=true` в
  новом PID: зафиксировать, что происходит (ordinary fallback, а не `Unavailable`), и
  решить судьбу сценария. (F-002)

## Overall assessment

Что выглядит обоснованным:

- маршрут и дисциплина эпох: раздельные plan/task/implementation-статусы, ацикличные
  зависимости, отказ от наследования v2 O1–O8/gates, явные fallback-и и запрет
  «положительного результата любой ценой»;
- инварианты: capture снимается после stable base, session/ProjectId/shadow paths не
  сериализуются, hydrated host проходит действующие admission/publication/write gates,
  unsupported write отвергается до disk I/O, cache failure не ломает ordinary load;
- benchmark protocol: compare key, новый PID на попытку, медиана только по successful
  attempts, miss overhead отдельно от hit, запрет менять корпус и сценарий задним числом;
- явно названные пределы: `mvp-fast` blind spot, неполнота произвольного MSBuild,
  отсутствие GC/leases, no-go E3 не отменяет E1/E2.

Что требует изменения:

- F-001 (ключ), F-004 (вердикт/coverage), F-008 (роли) — нормативный текст в spec E1 и
  проверки в задачах;
- F-002 (overlay) и F-005 (config-путь) — зафиксировать ограничение или расширить scope;
- F-006 (E0→E1 artifacts) — снять противоречие формулировок;
- F-007 (write boundary) — записать границу ответственности host seam.

Что требует проверки (не выводов на бумаге):

- достижимость реального положительного capture (F-003) — spike;
- недостижимость hit в overlay mode (F-002) — проба;
- эквивалентность hit и ordinary по наблюдаемому состоянию (F-001/F-004/F-008) — одна
  сравнительная проверка.

Что требует human decision:

- обязателен ли disk hit при активном overlay mode и какой ценой (F-002);
- допустимы ли документированные representability-потери при пересоздании metadata
  references (F-003);
- поддерживается ли config-driven lazy load в E1 (F-005).

Ревью проведено независимо: результаты других ревьюеров, defence и arbitration не
использовались. Спецификация, task-файлы, README эпох и счётчики статистики не изменялись —
правки остаются за координатором после recheck (`review/README.md`).

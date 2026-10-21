# Docs

Индекс `docs/`. Агентам: читать **канон** темы, не `_archive/` и не `archive/`, пока не нужен audit trail.

| Роль | Что это |
|---|---|
| **Reference** | Как система ведёт себя сейчас: [ARCHITECTURE.md](ARCHITECTURE.md), корневой README, tool `[Description]` |
| **Active** | План, который ещё может менять код |
| **Backlog** | Отложенная программа; исторические результаты сохранены, исполнение не продолжается автоматически |
| **Contract** | Спека shipped-фичи, пока факты не полностью влиты в ARCHITECTURE |
| **Record** | Закрытые эпохи и process (review / ответы / арбитраж). Не норма |

## Active

| Тема | Канон | Статус |
|---|---|---|
| Явный раннер тестов | [test-runner-selection/](test-runner-selection/README.md) | эпоха 1, код не начат; `testRunner=auto\|vstest\|mtp`, молчание = `auto` |
| Скорость обычной загрузки workspace | [workspace-load-performance/](workspace-load-performance/README.md) | 2026-10-09: создана по H-003 (вариант D); E0 — замер стадий, 12 задач (task-05/task-05.1 — разбивка по профилю стоимости, **no-go** 2026-10-10: профиль получен, лучший прогноз N=4 — 1,79× при пороге 2×; общее ядро (22 % стоимости) грузится в каждой группе, холодный BuildHost ≈ 6 с ([отчёт и ревью](workspace-load-performance/epoch-0-stage-measurement/evidence/task-05.1-profile-retry.md)); вывод ревью «ядро публичным API не разделить» поправлен 2026-10-10 — task-07 implemented, 2026-10-10: шаг 0 и 40 прогонов, **no-go** — XML N=4 1,84×, но OutputFilePath/diagnostics меняются, snapshot повторяет ROA=false зависимости; независимое ревью round 2 ACCEPT, code/tests acceptance pending main CI 1362 passed / 1 прежний SDK-specific failed ([результат](workspace-load-performance/epoch-0-stage-measurement/evidence/task-07-core-first-parallel-spike.md), [validation](workspace-load-performance/epoch-0-stage-measurement/evidence/task-07-core-first-parallel-spike/validation.md)); task-07.1 implemented, 2026-10-10 — шаг 0 и 52 matrix attempts, **no-go**: эквивалентность восстановлена, PowerToys N=4 depth2 25,228 с против 41,825 с (1,658×), Files cluster N=2/4 невыполнимы при ε=0,15; независимое ревью round 2 ACCEPT для standalone реализации/evidence, code/tests acceptance pending main CI 1362 passed / 1 прежний SDK-specific failed / 0 skipped, commit не создан ([результат](workspace-load-performance/epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition.md), [validation](workspace-load-performance/epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition/validation.md)); [task-07.2](workspace-load-performance/epoch-0-stage-measurement/task-07.2-refusal-fast-planner-roslyn-hi.md) implemented, 2026-10-10 — 36 matrix runs +6 дополнительных load processes, **no-go**: PowerToys cluster N4 23,306 с / 56,11% baseline, pre-load отказы ≤105%, Roslyn plan-violation; универсальный детерминизм deadline-limited N8 не достигнут. Независимое ревью round 2 ACCEPT для standalone реализации/evidence; code/tests acceptance pending main CI 1362 passed / 1 прежний SDK-specific failed / 0 skipped, commit не создан, production/E1 не активированы ([результат](workspace-load-performance/epoch-0-stage-measurement/evidence/task-07.2-refusal-fast-planner-roslyn.md), [validation](workspace-load-performance/epoch-0-stage-measurement/evidence/task-07.2-refusal-fast-planner-roslyn/validation.md)); [task-07.3](workspace-load-performance/epoch-0-stage-measurement/task-07.3-evaluated-load-graph-hi.md) planned, 2026-10-21 — сначала достоверный load graph и Roslyn N4 correctness, затем wall с ценой графа; компактное evidence сохраняется в Git, тяжёлые raw artifacts — отдельно; предкоммитный checkpoint 2026-10-21: main CI 1388 passed / 0 failed / 0 skipped ([validation](workspace-load-performance/epoch-0-stage-measurement/evidence/commit-2026-10-21.md)); task-06 — production bugfix provenance capture на binlog новее reader, planned); 2026-10-10: task-04 (spike параллельной загрузки) implemented, **no-go** — лучший N ≤ 4 даёт 1,51× при пороге 2×, round-robin N=4/8 теряет generated documents из-за гонки на общих obj, binlog +0,2 % ([отчёт](workspace-load-performance/epoch-0-stage-measurement/evidence/task-04-parallel-loader-spike.md)); формальная приёмка code/tests pending единого main CI (SDK-specific тест незакоммиченной работы workspace-load-cache); task-00 in progress: публичные прокси сняты (PowerToys 42 с, Files 11 с, open 94–98 %, однопоточный BuildHost — [результаты](workspace-load-performance/epoch-0-stage-measurement/evidence/task-00-current-version.md)), офисный замер по [протоколу](workspace-load-performance/epoch-0-stage-measurement/evidence/task-00-protocol.md) ожидается |
| Workspace state cache MVP | [workspace-state-cache/](workspace-state-cache/README.md) | 2026-10-09: post-arbitration revised-for-review; четыре эпохи, 35 задач; H-001 XML exclusion и H-002 strong-name B приняты, U-001 resolved; E0/task-00 accepted; E0/task-01 accepted для ordinary baseline runner контроля RoslynMcpPlugin, round 3; E0/task-08 accepted для isolated supported base-query slice, round 2; E0/task-02 accepted для schema v1 и живого isolated ordinary consumer, round 1; E0/task-03 accepted для ordinary capture выбранного контроля, round 1; E0/task-04 accepted для bounded schema-v1 codec, round 3; E0/task-05 accepted для isolated hydrate, round 2; E0/task-06 accepted, round 1, принятый scope — real new-PID roundtrip и scoped linked .cs edit ([validation](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-06-roundtrip.md), [review](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-06-review-round-1.md)); task-07 accepted, round 2, 2026-10-09, benchmark isolated restore записан, независимое ревью task-07 round 2, 2026-10-09 — ACCEPT, T07-R1-001/002/003 закрыты; принят bounded SamplePlugin benchmark scope ([review/validation](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2.md)) ([benchmark](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md)); приёмка эпохи не объявлена; S-002 not-run; [task-01 review round 3](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-01-review-round-3.md), [task-08 review round 2](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-08-review-round-2.md), [task-02 review](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-02-review-round-1.md), [task-02 validation](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-02-validation.md), [task-03 review](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-03-review-round-1.md), [task-03 validation](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-03-validation.md), [capability и validation](workspace-state-cache/epoch-0-snapshot-roundtrip/evidence/task-08-capability.md), [проверка revision](workspace-state-cache/preparation-report.md), [применение H-002](workspace-state-cache/h-002-report.md) |
| MCP tool surface evolution | [mcp-tool-surface-evolution/](mcp-tool-surface-evolution/README.md) | post-arbitration spec v2; реализация не начата |
| GitHub binary releases | [github-releases/](github-releases/README.md) | O-01 закрыт; CI + release workflow есть; smoke дистрибутива и O-02/O-03 открыты |
| Поддержка linux-x64 | [linux-x64-support/](linux-x64-support/README.md) | 2026-10-21: по решению P-03; task-01 и task-02 accepted ([CI](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39672573581)); task-03 in progress — релизная матрица ещё без выпуска |
| Улучшение тестов | [test-improvements/](test-improvements/README.md) | эпохи 1 и 2 выполнены, планы и отчёты в [архиве](test-improvements/_archive/README.md); далее эпоха 3 |
| Архитектурное ревью 2026-10 | [architecture-review/](architecture-review/README.md) | 2026-10-21: 17 находок (P1 — 4, P2 — 5, P3 — 8), все open; 4 вопроса владельцу, P-03 решён 2026-10-21 |

## Contract

Нет. Факты shipped-фич — в [ARCHITECTURE.md](ARCHITECTURE.md) и корневом README.
Руководство по плагинам — [samples/RoslynMcpPlugin/README.md](../samples/RoslynMcpPlugin/README.md).

## Backlog

- [Workspace load cache v2](backlog/workspace-load-cache/README.md) — **deferred,
  2026-10-07**: большая программа исследований отложена в пользу постепенного
  MVP состояния workspace. Прежние документы и evidence сохранены;
  [проверка переноса](backlog/workspace-load-cache/relocation-report.md).
- [Индекс backlog](backlog/README.md).

## Record

Не тащить в контекст по умолчанию.

| Где | Что лежит |
|---|---|
| [build-output/](build-output/README.md) | 2026-10-05: принято для 1.5.5 — silent-вывод сборки и контекст при неуспехе; Release, основной CI 1264/1264 — [валидация](build-output/validation.md) |
| [archive/](archive/README.md) | Закрытые серии без операторской ценности |
| `*/_archive/` | Review, ответы, арбитраж, superseded drafts той же темы |

## Правила

- После арбитража норма — корень темы (или `v2/`/`v3/`, если это текущий контракт). Process сразу в `_archive/`.
- После ship факты копируются в ARCHITECTURE / product README. Design-пакет помечается shipped и не правится «под факт».
- Accepted/rejected review не удалять. Протухший how-to/ложный reference — удалять или tombstone, не плодить второй канон.
- Старт работ по спеке: если рядом с каноном лежат `review/` / `review-response/` / `arbitration/` или pre-arb копии — предложить перенос в `_archive/`.
- Все эпохи приняты и UNRESOLVED/U-ARB закрыты: напомнить убрать тему в `docs/archive/`, если она уже не контракт. Правило для агента: `.cursor/rules/roslyn-mcp-docs-lifecycle.mdc`.

## Учёт задач, ревью и статистики

Согласовано 2026-10-07. Эти правила применяются при создании новых планов и
обновлении учёта выполнения. Они не меняют требования, зависимости, проверки
или разрешения на реализацию и выпуск существующих спецификаций.

### Имена и шапки задач

Новые задачи называются `task-NN-description-low.md`, `-med.md`, `-hi.md` или
`-xhi.md`. Суффикс обозначает класс сложности задачи, а не конкретную модель
или значение reasoning. Существующие исторические имена сохраняются.
Численная оценка сложности 1–5 остаётся отдельным ориентиром для человека.

В шапке сохраняются рекомендуемые исполнители, reasoning, сложность, риск с
причиной, необходимые способности, статус и зависимости. Рекомендуемых моделей
может быть несколько: из доступных GPT, Grok 4.7, GLM 5.3 и DeepSeek 4.1 Flash
выбираются подходящие для конкретной задачи; перечислять все модели не требуется.
При подготовке следующих планов список доступных моделей актуализируется.
Reasoning указывается только для моделей, поддерживающих такую настройку.
Рекомендации не выдаются за результаты фактического выполнения.

```markdown
- Рекомендуемые модели: …
- Рекомендуемый reasoning: …
- Примерная сложность: …/5.
- Риск ошибки: … — причина.
- Необходимые способности: …
- Статус: planned.
- Depends on: …
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.
```

Фактические исполнители и рецензенты записываются точными именами моделей,
например `gpt-6.1-sol`, отдельно от рекомендаций. При смене исполнителя в поле
перечисляются все участвовавшие модели, а в отчёте — кто реализовал результат
и кто внёс последующие исправления. Сессии автора и рецензента различаются:
независимое ревью может проводить та же модель в другой сессии. Если идентификаторы
сессий доступны, они сохраняются в отчёте; неизвестные не выдумываются.

Обычно `low`/`med` проверяет один рецензент, `hi` — один или несколько по риску,
для `xhi` рекомендуется два независимых рецензента. Это рекомендации; состав
фактического ревью всегда фиксируется, а обязательная приёмка определяется
спецификацией темы.

### Ревью эпох

Первоначальное ревью плана эпохи является этапом подготовки документации;
отдельные задачи на каждого рецензента заводить не требуется. Обычно участвуют
одна или две модели. В README эпохи ведутся два отдельных блока:
«Ревью плана эпохи» и «Приёмка реализации эпохи».
Принятый план не означает реализованную или принятую эпоху.

Каждый блок содержит следующий учёт; до начала соответствующего этапа поля
остаются незаполненными:

```markdown
- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено.
- Отчёты: —.
```

Допустимые итоги ревью: «не проверено», «в процессе», «требуются исправления»,
«принято», «отклонено». Для приёмки реализации дополнительно фиксируются дата,
принятый scope, результаты обязательной validation и оставшиеся ограничения.
Сводное ревью эпохи не подменяет ревью задач и финальную проверку кода.

### Как считать и писать отчёты

- **Раунд ревью** — проверка одной зафиксированной версии плана или результата.
  Проверки двух моделей одной версии относятся к одному раунду; повторная
  проверка следующей версии после правок — к следующему. В отчёте указываются
  номер раунда, версия (commit либо описание состояния diff), дата и рецензенты.
- **Количество исправлений после ревью** — число отдельных замечаний, исправление
  которых подтверждено рецензентом. Это не число коммитов, изменённых строк или
  попыток исправления. Открытые, отклонённые и отложенные замечания в счётчик
  исправлений не входят; их исход и причина сохраняются в отчёте.
- Каждое замечание получает стабильный ID в пределах проверяемого scope.
  Отчёт содержит замечание, его источник (модель и раунд), ответ/исправление и
  результат перепроверки. Повторное предъявление или переоткрытие того же
  замечания не создаёт дополнительное исправление в счётчике.
- Совпадающие замечания разных рецензентов сохраняют исходные ссылки и модели,
  но учитываются как одно исправление после явного объединения под общим ID.
  Замечания по одному файлу с разными причинами автоматически не объединяются.
- До ревью или при неизвестной истории ставится `—`, а не `0`. После завершённого
  ревью без исправленных замечаний допустим `0`; он сам по себе не означает
  принятия результата. Исторические числа восстанавливаются только по evidence;
  приблизительные оценки не записываются как точная статистика.

### Сводка и сохранение истории

README эпохи содержит статистику ревью её плана, задач и приёмки реализации
раздельно. Корневой README темы суммирует те же три группы по эпохам, со ссылками
на исходные отчёты. Ревью плана всей серии, если оно проводилось отдельно,
учитывается отдельной строкой. Итоги задач не включаются повторно в показатели
приёмки эпохи. Учитываются уникальные замечания в пределах каждой группы;
сводка перечисляет охваченные задачи/эпохи и элементы с неизвестными данными.
При неполной истории показывается сумма известных значений с пометкой «частично»,
без подстановки нулей за пропуски.

После ревью и перепроверки координатор синхронизирует отчёт, шапку задачи,
README эпохи и сводку темы. Статус, дата, принятый scope и evidence обновляются
по [AGENTS.md](../AGENTS.md); статистика не заменяет обязательную validation.
При архивировании сохраняются отчёты, сведения об исполнителях/рецензентах,
счётчики и рабочие ссылки. Перенос в архив не обнуляет историю.

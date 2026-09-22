# Workspace load cache — proposal v2

Статус: **post-arbitration specification, proposed, не реализовано**.
Канон этой темы. Runtime не меняется. Исторический v1, доарбитражный draft,
review, ответы и арбитраж — в [_archive/](_archive/README.md).

## 1. Цели и границы

Серия сохраняет исходные цели:

- **O1** — ускорить первый полезный semantic-запрос в новом PID на большом
  C#-решении, включая reuse полностью неизменного дерева.
- **O2** — не использовать git/ast, VS cache, внутреннее Roslyn storage,
  сериализацию `Solution`, `Compilation`, `SyntaxTree`, source/generated texts.
- **O3** — проверять ограниченный конус решения; не сканировать монорепу
  целиком ради удобства и не терять значимые inputs ради бюджета.
- **O4** — после внешнего sync живая сессия на следующем semantic call видит
  подтверждённое состояние либо явно сообщает невозможность обновления.
- **O5** — основной результат поддерживает reuse после изменения source content
  и membership без обязательного полного DTB для доказанного профиля.
- **O6** — analyzer overlay остаётся in-memory; cache identity использует inner
  TFM, outer evaluation не гидратируется как обычный project instance.
- **O7** — metadata references и partial load остаются самостоятельными
  направлениями ускорения с собственными contracts и измерениями.
- **O8** — correctness-first: неизвестность означает отказ от hit; write или
  refactoring на stale/unknown base запрещены.

Epoch 2 является только **unchanged-restart checkpoint**. Он не закрывает O4/O5,
не завершает серию и сам по себе не разрешает public activation. Полный основной
маршрут обязан закрыть O4 и O5. Закрытая модель 4C допустима для O5 только при
доказанном subset и `unknown -> ordinary load`; общий интерпретатор MSBuild
по-прежнему запрещён.

Trace: O1–O8; R-05, R-06, E0-05, E2-04 — ACCEPT WITH MODIFICATION;
E4-01 — REJECT (closed-subset направление сохранено).

## 2. Текущее основание

Base truth закреплён на commit
`9867318ddb5294ce144bf024b9a61a1a2e3814c3`, source version **1.3.21**.
Версия фактически запущенного MCP записывается отдельно в handoff и не выводится
из source version.

Обязательные совместимые ограничения текущего lifecycle:

- **A-LOAD** — load/hydrate, prepare, admission gate и atomic manager
  publication выполняются под одним acquisition; internal under-lock методы
  повторно semaphore не захватывают; reader видит только published snapshot.
- **A-STICKY** — overlay opt-in session-sticky: false/omitted на same-key RAM
  session не отключают активный режим; reset, новый key и новый PID создают
  новую сессию; overlay автоматически не включается.
- **A-WRITE** — write preflight проверяет опубликованную base generation,
  exact inverse удаляет shadow refs, partial persistence сообщается; rollback
  нескольких файлов и MVCC не обещаются.
- **A-ADMISSION** — `Banned`/`Unavailable` и неполный provenance не обходятся
  raw publication.
- **A-PROVENANCE** — текущий analyzer evidence привязан к load session и
  `ProjectId`; перенос между PID не предполагается.
- **A-LOADER** — analyzer loader process-lifetime, main-only; same-identity
  update может требовать restart, reset не выгружает CLR.

Trace: R-03 — ACCEPT; E1-06 — ACCEPT; R-04, C-05, E1-04 —
ACCEPT WITH MODIFICATION.

## 3. Целевая нагрузка и профиль

До production go владелец фиксирует target solution, размер, SDK, обязательные
TFM/project-instance contexts, generators, imports/tasks, overlay/metadata modes
и смесь unchanged/edit/build/restart/live calls. Если нагрузка multi-target,
exact inner-instance mapping — gate. Ограниченный одно-TFM результат нельзя
обобщать на другие большие решения.

Численный budget, median/p95, miss overhead, hit-rate и resource limits
утверждаются до получения результатов. Пока это не сделано, действует
[U-ARB-03](UNRESOLVED-v2.md#u-arb-03--репрезентативная-нагрузка-и-численный-budget);
fixture разрешает experiment, но не public activation.
Снять текущие задержки на двух закреплённых корпусах:
[baseline benchmark](baseline-benchmark.md). Отчёт сам gate не закрывает.

Trace: R-06, E0-02, E0-04, V-03, V-04 — ACCEPT WITH MODIFICATION.

## 4. Этапы и обязательные результаты

| Эпоха | Обязательный результат | Не означает |
|---|---|---|
| [0 — feasibility](epoch-0-feasibility.md) | Выбранный production host, capability/write contract, доказанный admission profile, oracle и budget plan | Готовность production hit |
| [1 — lifecycle](epoch-1-workspace-lifecycle.md) | Общий production lifecycle обычной загрузки и hydrate, безопасные writes, memberships и transition table | Наличие disk generation |
| [2 — unchanged checkpoint](epoch-2-conservative-disk-cache.md) | Безопасный cross-process hit неизменного supported tree и корректный store | Закрытие O4/O5 или activation |
| [3 — live consistency](epoch-3-live-consistency.md) | Закрытие O4 после решения freshness gate; RAM/index/cache validity разделены | Обязательную запись generation после каждого edit |
| [4 — measured directions](epoch-4-measured-optimizations.md) | Независимые metadata/partial/validation/O5/index изменения только после измерений | Разрешение ослабить admission |

До начала production реализации должны быть закрыты применимые feasibility
blockers U-ARB-04, U-ARB-05 и U-ARB-06. U-ARB-01 и U-ARB-02 остаются явными
decision gates в Epoch 2/3.

Trace: R-01, R-02, R-04, R-05, E0-01, E2-01, E2-05, E3-02, E3-04 —
арбитражные решения и unresolved gates.

## 5. Полномочия решений

Каждый handoff выдаёт пять независимых verdict:

1. `experiment allowed`;
2. `implementation allowed`;
3. `public activation allowed`;
4. `next epoch allowed`;
5. `series complete`.

`go`, выключенный default, zero-DTB, зелёный store или fixture-only не заменяют
эти решения. `series complete=true` требует закрытия O1–O8 в согласованном
scope, включая O4/O5. Отклонение не отменяет MUST, O4/O5 или A-WRITE.

Trace: E0-04, E2-06, H-01 — ACCEPT WITH MODIFICATION; E2-02 — REJECT
(конъюнктивная приёмка сохранена).

## 6. Общие запреты

- Не читать и не писать `.vs`/`.dtbcache`.
- Не использовать внутренние Roslyn storage API и произвольную десериализацию.
- Не сериализовать source/generated text или shadow temp paths.
- Не использовать VCS для admission/validation.
- Не выполнять truncated scan как hit.
- Не считать checksum известных файлов доказательством отсутствия неизвестных.
- Не загружать analyzer DLL только по cache DTO.
- Не менять product README, AGENTS, ARCHITECTURE или версию до фактического
  выпуска поведения; при выпуске обновить их по правилам репозитория.

## 7. Комплект спецификации

- [Контракт](cache-contract.md)
- [Epoch 0](epoch-0-feasibility.md)
- [Epoch 1](epoch-1-workspace-lifecycle.md)
- [Epoch 2](epoch-2-conservative-disk-cache.md)
- [Epoch 3](epoch-3-live-consistency.md)
- [Epoch 4](epoch-4-measured-optimizations.md)
- [Verification](verification.md)
- [Handoff template](handoff-template.md)
- [Изменения v2](CHANGELOG-v2.md)
- [Traceability](TRACEABILITY-v2.md)
- [Unresolved](UNRESOLVED-v2.md)
- [Post-arbitration issues](POST-ARBITRATION-ISSUES.md)
- [Baseline benchmark](baseline-benchmark.md)

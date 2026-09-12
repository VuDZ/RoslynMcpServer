# Приёмка эпохи 6

Дата: 2026-09-12. Вердикт: **S1–S4 принимаются; аудит документации завершён.
Серия не завершена.**
Норматив: [epoch-6-contract-and-documentation.md](epoch-6-contract-and-documentation.md).
Прогон: [epoch-6-results.md](epoch-6-results.md) (сверка документов и кода;
runtime не запускался).
Shipped-поведение: **v1.3.8** (`f54aec15f48f942ef9ac077c752bf03ae018bf31`).
Сам аудит в этот commit не входит.

Первый проход принял только S1–S2 (A6-08 / A6-11 / A6-12 открыты). Этот
независимый повтор: дыры закрыты. A6-13 — неблокер.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| A6-01 | — | DOC-EARLY три targets | **закрыт** |
| A6-02 | — | LC-S1 ветки vs код | **закрыт** |
| A6-03 | — | LC-S2 G/S/M/A/L/D/R | **закрыт** |
| A6-04 | — | Semantic inventory | **закрыт** |
| A6-05 | — | Upgrade / reuse / restart | **закрыт** — LC-S3, README v1.3.8, AGENTS, ARCHITECTURE |
| A6-06 | — | CodeAction/rename + partial | **закрыт** — Reference, AGENTS, ARCHITECTURE Editing |
| A6-07 | — | Namespaces / cleanup | **закрыт** |
| A6-08 | — | Статусы серии | **закрыт** — «аудит завершён» ≠ «серия завершена» |
| A6-09 | — | Ссылки и имена параметров | **закрыт** — `shadowCopyInSolutionAnalyzers`; catalog 63 / 44165 |
| A6-10 | — | Исторический README → v2 | **закрыт** — указатель на v2 есть; см. A6-13 |
| A6-11 | Low | LC-S1 неровно аннотирован | **закрыт** — Under-lock / prepare failure / apply failure: v1.3.5 vs v1.3.6+/v1.3.8 |
| A6-12 | Medium | ARCHITECTURE Editing = raw `TryApplyChanges` | **закрыт** — persist через write boundary, не overlay |
| A6-13 | Low | Historical README: present-tense wipe | **открыт** — не блокер |

---

## Повтор после A6-08 / A6-11 / A6-12

- **A6-12:** `docs/ARCHITECTURE.md` § Editing: candidate через Roslyn APIs;
  persist — `ApplySolutionChangesToDiskAsync`; `TryApplyChanges` только внутри
  границы, на cleaned candidate, не на overlay.
- **A6-11:** LC-S1 Under-lock / prepare failure / apply failure аннотированы
  как text edit / FSW.
- **A6-08:** v2 README / spec / FOLLOWUPS F-08: аудит завершён; серия нет
  (эпоха 5 позднее принята в v1.3.12; U-ARB-05 позднее закрыт как
  session-sticky в v1.3.13; U-ARB-04/inaccessible остаются). v1
  `epoch-6-contract-and-documentation.md` всё ещё «S1/S2 приняты, не
  завершён» — указатель отстал, не ломает статус v2.

`f54aec15` — shipped write boundary, не commit аудита.

---

ID: A6-13
Severity: Low
Depends: A6-10

Target:
docs/analyzer-shadow-copy/README.md § Fixed architectural decisions (v1.3.5 history)

Claim:
Блок «Current shipped (v1.3.8)» верный (inverse + reject). Следующая секция
истории в present tense: «Any future code path … is defended by
`RevertAnalyzerReferenceOverlayForApply`». Метода нет с эпохи 4. «Rules»
ниже уже требуют write boundary. Путаница «история vs совет на будущее»,
не текущий ARCHITECTURE.

Suggested change:
Заменить на: v1.3.5 wipe-guard; v1.3.8 — exact inverse + reject. Не чинить
в этой приёмке.

---

## Что не чинить в этой эпохе

- U-ARB-01 matcher / эпоха 5
- U-ARB-04 / U-ARB-05
- A2-09, A4-09…A4-12, A6-13
- Runtime-прогон матрицы
- Version bump (документация без смены tool behavior)

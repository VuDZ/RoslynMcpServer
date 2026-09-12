# Трассировка v2

## Метод идентификации и источники

В v1 формальных requirement IDs нет. `V1-*` ниже — введённые **только для трассировки**
обозначения существующих разделов/пронумерованных инвариантов. Они не приписываются
исходному автору и не добавляют требований. Для ненумерованных требований идентификатор
ссылается на раздел целиком; конкретное изменение раскрыто строкой finding и CHANGELOG.
Имена `R-xx`/`Ex-xx` — исходные review finding IDs, а не requirement IDs.

Входы ограничены непосредственными файлами каталогов:
[v1](../README.md), [Grok review](../review/README.md),
[Astra responses](../review-response/README.md), [Astra summary](../review-response/SUMMARY.md),
[Sol arbitration](../arbitration/README.md). Вложенные каталоги входов не использовались.
Факты кода взяты из арбитража; нового code/runtime review не проводилось.

Нормативный приоритет: [finding-verdicts](../arbitration/finding-verdicts.md),
[normative-change-set](../arbitration/normative-change-set.md),
[cross-cutting-risks](../arbitration/cross-cutting-risks.md) и
[unresolved](../arbitration/unresolved.md). Рекомендации Grok/Astra вне принятых
границ не стали требованиями v2.

## Реестр ссылок на требования v1

| Requirement ID (ссылка v2 на v1) | Исходный файл и точный раздел |
| --- | --- |
| V1-R-GOAL | [README.md](../README.md) — Цель |
| V1-R-ORDER | [README.md](../README.md) — Эпохи и порядок |
| V1-R-FACT | [README.md](../README.md) — Исходные факты и открытые вопросы |
| V1-R-I1 | [README.md](../README.md) — Общие инварианты, пункт 1 |
| V1-R-I2 | [README.md](../README.md) — Общие инварианты, пункт 2 |
| V1-R-I3 | [README.md](../README.md) — Общие инварианты, пункт 3 |
| V1-R-I4 | [README.md](../README.md) — Общие инварианты, пункт 4 |
| V1-R-I5 | [README.md](../README.md) — Общие инварианты, пункт 5 |
| V1-R-I6 | [README.md](../README.md) — Общие инварианты, пункт 6 |
| V1-R-I7 | [README.md](../README.md) — Общие инварианты, пункт 7 |
| V1-R-RESULT | [README.md](../README.md) — Правила фиксации результатов |
| V1-E1-PUR | [epoch-1-lifecycle-verification.md](../epoch-1-lifecycle-verification.md) — Проблема и цель |
| V1-E1-SC | [epoch-1-lifecycle-verification.md](../epoch-1-lifecycle-verification.md) — Сценарий |
| V1-E1-ADD | [epoch-1-lifecycle-verification.md](../epoch-1-lifecycle-verification.md) — Дополнительные случаи |
| V1-E1-REP | [epoch-1-lifecycle-verification.md](../epoch-1-lifecycle-verification.md) — Воспроизводимость |
| V1-E1-AC | [epoch-1-lifecycle-verification.md](../epoch-1-lifecycle-verification.md) — Критерии завершения |
| V1-E2-PROB | [epoch-2-immutable-shadow-copies.md](../epoch-2-immutable-shadow-copies.md) — Проблема |
| V1-E2-REQ | [epoch-2-immutable-shadow-copies.md](../epoch-2-immutable-shadow-copies.md) — Требования |
| V1-E2-ALG | [epoch-2-immutable-shadow-copies.md](../epoch-2-immutable-shadow-copies.md) — Предлагаемый алгоритм |
| V1-E2-ERR | [epoch-2-immutable-shadow-copies.md](../epoch-2-immutable-shadow-copies.md) — Ошибки и границы |
| V1-E2-AC | [epoch-2-immutable-shadow-copies.md](../epoch-2-immutable-shadow-copies.md) — Критерии приёмки |
| V1-E3-PROB | [epoch-3-loader-contract-and-dependencies.md](../epoch-3-loader-contract-and-dependencies.md) — Проблема |
| V1-E3-CON | [epoch-3-loader-contract-and-dependencies.md](../epoch-3-loader-contract-and-dependencies.md) — Требуемый контракт |
| V1-E3-DEC | [epoch-3-loader-contract-and-dependencies.md](../epoch-3-loader-contract-and-dependencies.md) — Решение по результатам |
| V1-E3-DEP | [epoch-3-loader-contract-and-dependencies.md](../epoch-3-loader-contract-and-dependencies.md) — Подготовка зависимостей |
| V1-E3-RES | [epoch-3-loader-contract-and-dependencies.md](../epoch-3-loader-contract-and-dependencies.md) — Ресурсы и приёмка |
| V1-E4-PROB | [epoch-4-workspace-write-boundary.md](../epoch-4-workspace-write-boundary.md) — Проблема |
| V1-E4-REQ | [epoch-4-workspace-write-boundary.md](../epoch-4-workspace-write-boundary.md) — Требования |
| V1-E4-PUB | [epoch-4-workspace-write-boundary.md](../epoch-4-workspace-write-boundary.md) — Обновление overlay |
| V1-E4-AC | [epoch-4-workspace-write-boundary.md](../epoch-4-workspace-write-boundary.md) — Проверки и приёмка |
| V1-E5-PROB | [epoch-5-reference-provenance.md](../epoch-5-reference-provenance.md) — Проблема |
| V1-E5-REQ | [epoch-5-reference-provenance.md](../epoch-5-reference-provenance.md) — Решения и требования |
| V1-E5-DIAG | [epoch-5-reference-provenance.md](../epoch-5-reference-provenance.md) — Диагностика |
| V1-E5-AC | [epoch-5-reference-provenance.md](../epoch-5-reference-provenance.md) — Приёмка |
| V1-E6-GOAL | [epoch-6-contract-and-documentation.md](../epoch-6-contract-and-documentation.md) — Цель |
| V1-E6-SNAP | [epoch-6-contract-and-documentation.md](../epoch-6-contract-and-documentation.md) — Контракт снимков |
| V1-E6-DOC | [epoch-6-contract-and-documentation.md](../epoch-6-contract-and-documentation.md) — Изменения документации |
| V1-E6-AC | [epoch-6-contract-and-documentation.md](../epoch-6-contract-and-documentation.md) — Проверка и завершение серии |

## Requirement → раздел v1 → finding → арбитраж → раздел v2

Каждая строка соответствует одному из 42 findings. Verdict приведён буквально;
принятые границы решения изложены в результирующих разделах и CHANGELOG.

| Requirement ID | Раздел спецификации v1 | Review finding | Решение арбитража | Результирующий раздел v2 |
| --- | --- | --- | --- | --- |
| V1-R-GOAL; V1-R-I4 | [Цель](../README.md); [Общие инварианты, пункт 4](../README.md) | [R-01](../review/README.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (R-01) | README: текущее поведение, целевые инварианты, эпохи |
| V1-R-I2 | [Общие инварианты, пункт 2](../README.md) | [R-02](../review/README.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (R-02) | README: инвариант 2; [E1-S4](epoch-1-lifecycle-verification.md); [E4-S1/S2](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md); [E6-S2](epoch-6-contract-and-documentation.md) |
| V1-R-ORDER; V1-E4-REQ | [Эпохи и порядок](../README.md); [Требования](../epoch-4-workspace-write-boundary.md) | [R-03](../review/README.md) | [ACCEPT](../arbitration/finding-verdicts.md) (R-03) | README: эпохи; [E2-S1](epoch-2-immutable-shadow-copies.md); [E4-S1](epoch-4-workspace-write-boundary.md) |
| V1-R-FACT | [Исходные факты и открытые вопросы](../README.md) | [R-04](../review/README.md) | [REJECT](../arbitration/finding-verdicts.md) (R-04) | README: факты; [E2-S2](epoch-2-immutable-shadow-copies.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md) |
| V1-E1-SC; V1-E1-REP | [Сценарий](../epoch-1-lifecycle-verification.md); [Воспроизводимость](../epoch-1-lifecycle-verification.md) | [E1-01](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-01) | [E1-S1/S5](epoch-1-lifecycle-verification.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md) |
| V1-E1-SC | [Сценарий](../epoch-1-lifecycle-verification.md) | [E1-02](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-02) | README: термины; [E1-S2](epoch-1-lifecycle-verification.md); [E2-S1](epoch-2-immutable-shadow-copies.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md); [LC-S1–S3](LIFECYCLE-v2.md); [E6-S2/S3](epoch-6-contract-and-documentation.md) |
| V1-E1-SC | [Сценарий](../epoch-1-lifecycle-verification.md) | [E1-03](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-03) | [E1-S2/S4](epoch-1-lifecycle-verification.md); [E3-S1/S5](epoch-3-loader-contract-and-dependencies.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| V1-E1-SC; V1-R-I6 | [Сценарий](../epoch-1-lifecycle-verification.md); [Общие инварианты, пункт 6](../README.md) | [E1-04](../review/epoch-1-lifecycle-verification.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E1-04) | README: v1.3.5; [E1-S2](epoch-1-lifecycle-verification.md); [E2-S1](epoch-2-immutable-shadow-copies.md); [LC-S1–S3](LIFECYCLE-v2.md); [U-ARB-05](UNRESOLVED-v2.md) |
| V1-E1-SC | [Сценарий](../epoch-1-lifecycle-verification.md) | [E1-05](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-05) | [E1-S1](epoch-1-lifecycle-verification.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md); [E5-S4](epoch-5-reference-provenance.md) |
| V1-E1-SC; V1-E1-AC | [Сценарий](../epoch-1-lifecycle-verification.md); [Критерии завершения](../epoch-1-lifecycle-verification.md) | [E1-06](../review/epoch-1-lifecycle-verification.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E1-06) | [E1-S3](epoch-1-lifecycle-verification.md); [E4-S2/S4](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| V1-E1-SC | [Сценарий](../epoch-1-lifecycle-verification.md) | [E1-07](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-07) | [E1-S3](epoch-1-lifecycle-verification.md); [E3-S5](epoch-3-loader-contract-and-dependencies.md); [E4-S3/S4](epoch-4-workspace-write-boundary.md); [U-ARB-04 evidence](u-arb-04-load-boundary-evidence.md) |
| V1-E1-REP; V1-E1-SC | [Воспроизводимость](../epoch-1-lifecycle-verification.md); [Сценарий](../epoch-1-lifecycle-verification.md) | [E1-08](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-08) | [E1-S3](epoch-1-lifecycle-verification.md); README: инвариант 2; [LC-S1/S2](LIFECYCLE-v2.md) |
| V1-E1-SC; V1-E1-AC | [Сценарий](../epoch-1-lifecycle-verification.md); [Критерии завершения](../epoch-1-lifecycle-verification.md) | [E1-09](../review/epoch-1-lifecycle-verification.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E1-09) | [E1-S2/S5](epoch-1-lifecycle-verification.md); [E2-S1/S4/S5](epoch-2-immutable-shadow-copies.md); [E3-S4](epoch-3-loader-contract-and-dependencies.md); [E4-S2](epoch-4-workspace-write-boundary.md); [LC-S2](LIFECYCLE-v2.md) |
| V1-E1-ADD | [Дополнительные случаи](../epoch-1-lifecycle-verification.md) | [E1-10](../review/epoch-1-lifecycle-verification.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E1-10) | [E1-S2/S4](epoch-1-lifecycle-verification.md); [E2-S5](epoch-2-immutable-shadow-copies.md); [E3-S2](epoch-3-loader-contract-and-dependencies.md) |
| V1-E2-REQ | [Требования](../epoch-2-immutable-shadow-copies.md) | [E2-01](../review/epoch-2-immutable-shadow-copies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E2-01) | [E2-S2](epoch-2-immutable-shadow-copies.md); [E3-S2/S5](epoch-3-loader-contract-and-dependencies.md); [E6-S3](epoch-6-contract-and-documentation.md) |
| V1-E2-ERR; V1-E2-AC | [Ошибки и границы](../epoch-2-immutable-shadow-copies.md); [Критерии приёмки](../epoch-2-immutable-shadow-copies.md) | [E2-02](../review/epoch-2-immutable-shadow-copies.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E2-02) | [E2-S1/S2/S5](epoch-2-immutable-shadow-copies.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| V1-E2-ALG | [Предлагаемый алгоритм](../epoch-2-immutable-shadow-copies.md) | [E2-03](../review/epoch-2-immutable-shadow-copies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E2-03) | [E2-S3/S5](epoch-2-immutable-shadow-copies.md) |
| V1-E2-ERR; V1-E6-SNAP | [Ошибки и границы](../epoch-2-immutable-shadow-copies.md); [Контракт снимков](../epoch-6-contract-and-documentation.md) | [E2-04](../review/epoch-2-immutable-shadow-copies.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E2-04) | [E2-S1/S4/S5](epoch-2-immutable-shadow-copies.md); [E4-S2](epoch-4-workspace-write-boundary.md); [LC-S1–S3](LIFECYCLE-v2.md); [E6-S3](epoch-6-contract-and-documentation.md) |
| V1-E2-ALG; V1-E2-AC | [Предлагаемый алгоритм](../epoch-2-immutable-shadow-copies.md); [Критерии приёмки](../epoch-2-immutable-shadow-copies.md) | [E2-05](../review/epoch-2-immutable-shadow-copies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E2-05) | [E2-S3/S4/S5](epoch-2-immutable-shadow-copies.md); [E1-S4](epoch-1-lifecycle-verification.md) |
| V1-E2-ERR | [Ошибки и границы](../epoch-2-immutable-shadow-copies.md) | [E2-06](../review/epoch-2-immutable-shadow-copies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E2-06) | [E2-S4/S5](epoch-2-immutable-shadow-copies.md); [E3-S5](epoch-3-loader-contract-and-dependencies.md); [E6-S3](epoch-6-contract-and-documentation.md); [LC-S2](LIFECYCLE-v2.md) |
| V1-E2-REQ; V1-E2-AC | [Требования](../epoch-2-immutable-shadow-copies.md); [Критерии приёмки](../epoch-2-immutable-shadow-copies.md) | [E2-07](../review/epoch-2-immutable-shadow-copies.md) | [REJECT](../arbitration/finding-verdicts.md) (E2-07) | [E2-S2/S5](epoch-2-immutable-shadow-copies.md); [E3-S1](epoch-3-loader-contract-and-dependencies.md) |
| V1-E3-CON | [Требуемый контракт](../epoch-3-loader-contract-and-dependencies.md) | [E3-01](../review/epoch-3-loader-contract-and-dependencies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E3-01) | [E3-S1/S5](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [LC-S1–S3](LIFECYCLE-v2.md); [U-ARB-02](UNRESOLVED-v2.md) |
| V1-E3-DEC; V1-E3-DEP | [Решение по результатам](../epoch-3-loader-contract-and-dependencies.md); [Подготовка зависимостей](../epoch-3-loader-contract-and-dependencies.md) | [E3-02](../review/epoch-3-loader-contract-and-dependencies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E3-02) | [E3-S2](epoch-3-loader-contract-and-dependencies.md); [E2-S2](epoch-2-immutable-shadow-copies.md); [U-ARB-03](UNRESOLVED-v2.md) |
| V1-E3-DEC | [Решение по результатам](../epoch-3-loader-contract-and-dependencies.md) | [E3-03](../review/epoch-3-loader-contract-and-dependencies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E3-03) | [E3-S3/S5](epoch-3-loader-contract-and-dependencies.md); [U-ARB-03](UNRESOLVED-v2.md) |
| V1-E3-DEP; V1-E3-RES | [Подготовка зависимостей](../epoch-3-loader-contract-and-dependencies.md); [Ресурсы и приёмка](../epoch-3-loader-contract-and-dependencies.md) | [E3-04](../review/epoch-3-loader-contract-and-dependencies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E3-04) | [E3-S3/S5](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [LC-S2](LIFECYCLE-v2.md); [U-ARB-03](UNRESOLVED-v2.md) |
| V1-E3-DEC; V1-E3-RES | [Решение по результатам](../epoch-3-loader-contract-and-dependencies.md); [Ресурсы и приёмка](../epoch-3-loader-contract-and-dependencies.md) | [E3-05](../review/epoch-3-loader-contract-and-dependencies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E3-05) | [E3-S1/S5](epoch-3-loader-contract-and-dependencies.md); [E6-S4](epoch-6-contract-and-documentation.md); [U-ARB-02](UNRESOLVED-v2.md) |
| V1-E3-DEP; V1-R-I5 | [Подготовка зависимостей](../epoch-3-loader-contract-and-dependencies.md); [Общие инварианты, пункт 5](../README.md) | [E3-06](../review/epoch-3-loader-contract-and-dependencies.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E3-06) | README: состояние; [E3-S4/S5](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [LC-S2](LIFECYCLE-v2.md); [E5-S3](epoch-5-reference-provenance.md) |
| V1-E4-REQ | [Требования](../epoch-4-workspace-write-boundary.md) | [E4-01](../review/epoch-4-workspace-write-boundary.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E4-01) | [E4-S1](epoch-4-workspace-write-boundary.md); [E2-S1](epoch-2-immutable-shadow-copies.md); [E1-S4](epoch-1-lifecycle-verification.md); README: сквозные границы |
| V1-E4-REQ | [Требования](../epoch-4-workspace-write-boundary.md) | [E4-02](../review/epoch-4-workspace-write-boundary.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E4-02) | [E4-S1/S2/S4](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md); [E6-S3](epoch-6-contract-and-documentation.md) |
| V1-E4-REQ; V1-E4-PUB | [Требования](../epoch-4-workspace-write-boundary.md); [Обновление overlay](../epoch-4-workspace-write-boundary.md) | [E4-03](../review/epoch-4-workspace-write-boundary.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E4-03) | [E4-S3/S4](epoch-4-workspace-write-boundary.md); [E1-S3/S4](epoch-1-lifecycle-verification.md); [E3-S5](epoch-3-loader-contract-and-dependencies.md); [U-ARB-04 evidence](u-arb-04-load-boundary-evidence.md) |
| V1-E4-PUB | [Обновление overlay](../epoch-4-workspace-write-boundary.md) | [E4-04](../review/epoch-4-workspace-write-boundary.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E4-04) | [E4-S2/S4](epoch-4-workspace-write-boundary.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| V1-E4-REQ; V1-E4-AC | [Требования](../epoch-4-workspace-write-boundary.md); [Проверки и приёмка](../epoch-4-workspace-write-boundary.md) | [E4-05](../review/epoch-4-workspace-write-boundary.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E4-05) | [E4-S1/S4](epoch-4-workspace-write-boundary.md); [E2-S1](epoch-2-immutable-shadow-copies.md); README: эпохи |
| V1-E4-PUB; V1-E4-AC | [Обновление overlay](../epoch-4-workspace-write-boundary.md); [Проверки и приёмка](../epoch-4-workspace-write-boundary.md) | [E4-06](../review/epoch-4-workspace-write-boundary.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E4-06) | [E4-S2/S4](epoch-4-workspace-write-boundary.md); [E2-S1/S5](epoch-2-immutable-shadow-copies.md); [E1-S3](epoch-1-lifecycle-verification.md); [LC-S1/S2](LIFECYCLE-v2.md) |
| V1-E5-REQ | [Решения и требования](../epoch-5-reference-provenance.md) | [E5-01](../review/epoch-5-reference-provenance.md) | [UNRESOLVED](../arbitration/finding-verdicts.md) (E5-01) | [E5-S1/S2/S4](epoch-5-reference-provenance.md); [U-ARB-01](UNRESOLVED-v2.md) |
| V1-E5-REQ; V1-E5-AC | [Решения и требования](../epoch-5-reference-provenance.md); [Приёмка](../epoch-5-reference-provenance.md) | [E5-02](../review/epoch-5-reference-provenance.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E5-02) | [E5-S2](epoch-5-reference-provenance.md); [E1-S2](epoch-1-lifecycle-verification.md); [U-ARB-01](UNRESOLVED-v2.md) |
| V1-E5-REQ; V1-E5-AC | [Решения и требования](../epoch-5-reference-provenance.md); [Приёмка](../epoch-5-reference-provenance.md) | [E5-03](../review/epoch-5-reference-provenance.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E5-03) | [E5-S1/S2/S4](epoch-5-reference-provenance.md) |
| V1-E5-DIAG | [Диагностика](../epoch-5-reference-provenance.md) | [E5-04](../review/epoch-5-reference-provenance.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E5-04) | [E5-S3](epoch-5-reference-provenance.md); [E3-S4](epoch-3-loader-contract-and-dependencies.md); [E2-S4](epoch-2-immutable-shadow-copies.md); [U-ARB-01](UNRESOLVED-v2.md) |
| V1-E5-AC; V1-R-ORDER | [Приёмка](../epoch-5-reference-provenance.md); [Эпохи и порядок](../README.md) | [E5-05](../review/epoch-5-reference-provenance.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E5-05) | [E1-S2](epoch-1-lifecycle-verification.md); [E5-S1/S4](epoch-5-reference-provenance.md); README: эпохи |
| V1-E6-SNAP; V1-E6-DOC | [Контракт снимков](../epoch-6-contract-and-documentation.md); [Изменения документации](../epoch-6-contract-and-documentation.md) | [E6-01](../review/epoch-6-contract-and-documentation.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E6-01) | [LC-S1–S3](LIFECYCLE-v2.md); [E6-S3](epoch-6-contract-and-documentation.md); [E2-S1/S2](epoch-2-immutable-shadow-copies.md) |
| V1-E6-DOC; V1-R-RESULT | [Изменения документации](../epoch-6-contract-and-documentation.md); [Правила фиксации результатов](../README.md) | [E6-02](../review/epoch-6-contract-and-documentation.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E6-02) | [E6-S1](epoch-6-contract-and-documentation.md); README: текущее поведение и эпохи |
| V1-E6-SNAP; V1-E6-AC | [Контракт снимков](../epoch-6-contract-and-documentation.md); [Проверка и завершение серии](../epoch-6-contract-and-documentation.md) | [E6-03](../review/epoch-6-contract-and-documentation.md) | [ACCEPT](../arbitration/finding-verdicts.md) (E6-03) | [LC-S1/S2](LIFECYCLE-v2.md); [E6-S2](epoch-6-contract-and-documentation.md); [E1-S2/S4](epoch-1-lifecycle-verification.md); [U-ARB-05](UNRESOLVED-v2.md) |
| V1-E6-SNAP; V1-E6-AC | [Контракт снимков](../epoch-6-contract-and-documentation.md); [Проверка и завершение серии](../epoch-6-contract-and-documentation.md) | [E6-04](../review/epoch-6-contract-and-documentation.md) | [ACCEPT WITH MODIFICATION](../arbitration/finding-verdicts.md) (E6-04) | [E6-S2/S4](epoch-6-contract-and-documentation.md); [E2-S5](epoch-2-immutable-shadow-copies.md); [E4-S4](epoch-4-workspace-write-boundary.md); README: статус |

## Полнота переноса исходных разделов

Для каждого исходного раздела указан его полный преемник. Строка с «сохранено»
не вводит нового решения. Детальные verdicts всех связанных findings приведены выше.

| Requirement ID | Review findings | Арбитраж / сохранение | Результирующий раздел |
| --- | --- | --- | --- |
| V1-R-GOAL | R-01 | По индивидуальным verdicts выше | README: назначение и инварианты |
| V1-R-ORDER | R-03, E5-05 | По индивидуальным verdicts выше | README: эпохи и зависимости |
| V1-R-FACT | R-04 | По индивидуальным verdicts выше | README: текущее поведение |
| V1-R-I1 | — | Сохранено без отдельного finding | README: инвариант 1 |
| V1-R-I2 | R-02 | По индивидуальным verdicts выше | README: инвариант 2 |
| V1-R-I3 | — | Сохранено без отдельного finding | README: инвариант 3 |
| V1-R-I4 | R-01 | По индивидуальным verdicts выше | README: инвариант 4 |
| V1-R-I5 | E3-06 | По индивидуальным verdicts выше | README: инвариант 5 |
| V1-R-I6 | E1-04 | По индивидуальным verdicts выше | README: инвариант 6 |
| V1-R-I7 | — | Сохранено без отдельного finding | README: инвариант 7 |
| V1-R-RESULT | E6-02 | По индивидуальным verdicts выше | README: фиксация результатов; E6-S4 |
| V1-E1-PUR | — | Сохранено без отдельного finding | E1-S1/S5 |
| V1-E1-SC | E1-01, E1-02, E1-03, E1-04, E1-05, E1-06, E1-07, E1-08, E1-09 | По индивидуальным verdicts выше | E1-S1–S4 |
| V1-E1-ADD | E1-10 | По индивидуальным verdicts выше | E1-S2/S4 |
| V1-E1-REP | E1-01, E1-08 | По индивидуальным verdicts выше | E1-S4/S5 |
| V1-E1-AC | E1-06, E1-09 | По индивидуальным verdicts выше | E1-S5 |
| V1-E2-PROB | — | Сохранено без отдельного finding | E2-S1/S2 |
| V1-E2-REQ | E2-01, E2-07 | По индивидуальным verdicts выше | E2-S1/S2 |
| V1-E2-ALG | E2-03, E2-05 | По индивидуальным verdicts выше | E2-S3 |
| V1-E2-ERR | E2-02, E2-04, E2-06 | По индивидуальным verdicts выше | E2-S1/S3/S4 |
| V1-E2-AC | E2-02, E2-05, E2-07 | По индивидуальным verdicts выше | E2-S5 |
| V1-E3-PROB | — | Сохранено без отдельного finding | E3-S1 |
| V1-E3-CON | E3-01 | По индивидуальным verdicts выше | E3-S1/S2 |
| V1-E3-DEC | E3-02, E3-03, E3-05 | По индивидуальным verdicts выше | E3-S1/S3 |
| V1-E3-DEP | E3-02, E3-04, E3-06 | По индивидуальным verdicts выше | E3-S2–S4 |
| V1-E3-RES | E3-04, E3-05 | По индивидуальным verdicts выше | E3-S3/S5 |
| V1-E4-PROB | — | Сохранено без отдельного finding | E4-S1/S3 |
| V1-E4-REQ | R-03, E4-01, E4-02, E4-03, E4-05 | По индивидуальным verdicts выше | E4-S1 |
| V1-E4-PUB | E4-03, E4-04, E4-06 | По индивидуальным verdicts выше | E4-S2 |
| V1-E4-AC | E4-05, E4-06 | По индивидуальным verdicts выше | E4-S4 |
| V1-E5-PROB | — | Сохранено без отдельного finding | E5-S1 |
| V1-E5-REQ | E5-01, E5-02, E5-03 | По индивидуальным verdicts выше | E5-S1/S2 |
| V1-E5-DIAG | E5-04 | По индивидуальным verdicts выше | E5-S3 |
| V1-E5-AC | E5-02, E5-03, E5-05 | По индивидуальным verdicts выше | E5-S4 |
| V1-E6-GOAL | — | Сохранено без отдельного finding | E6-S2/S4 |
| V1-E6-SNAP | E2-04, E6-01, E6-03, E6-04 | По индивидуальным verdicts выше | README: инварианты; LC-S1–S3; E6-S2 |
| V1-E6-DOC | E6-01, E6-02 | По индивидуальным verdicts выше | E6-S1/S3 |
| V1-E6-AC | E6-03, E6-04 | По индивидуальным verdicts выше | E6-S4 |

Дополнительно сохранены ненумерованные детали v1: optional PDB без позднего дополнения
(E2-S2/S5), проверка нестабильности source и build-before-refresh (E2-S3), отсутствие
ссылок на partial staging (E2-S3/S4), отказ от private real-output fallback (E3-S2),
границы native/out-of-process (E3-S5), отсутствие auto-build и generated-name search
(README/E5-S1/S4), воспроизводимый disposable fixture без внешнего GenRepro (E1-S5),
исторические документы и независимые условия удаления workaround (E6-S1/S3).

## Покрытие normative-change-set

`NC-R-n` и `NC-Ek-n` — локальные ссылки на номера пунктов соответствующего раздела
[нормативного набора](../arbitration/normative-change-set.md), не новые требования.
Диапазоны включают каждый пункт. Всего: README 6 + эпохи 15/12/13/11/8/6 = **71**.

| Нормативные пункты | Раздел v2 |
| --- | --- |
| NC-R-1–2 | README: текущее поведение, целевые инварианты, эпохи |
| NC-R-3 | README: инвариант 2; E1-S4; E4-S1; LC-S1/S2 |
| NC-R-4 | README: зависимости; E2-S1; E3-S1; E4-S1 |
| NC-R-5–6 | README: термины/v1.3.5; LC-S1–S3; U-ARB-05 |
| NC-E1-1–3 | E1-S1/S5 |
| NC-E1-4–6 | E1-S2; LC-S1/S2 |
| NC-E1-7–10 | E1-S3 |
| NC-E1-11–13 | E1-S2/S5; E2-S5; E5-S4 |
| NC-E1-14–15 | E1-S4; E3-S2; E2-S5 |
| NC-E2-1 | E2-S1; E4-S1 |
| NC-E2-2–3 | E2-S2; E3-S2 |
| NC-E2-4–6 | E2-S1/S4; LC-S1/S2 |
| NC-E2-7–9 | E2-S3/S5 |
| NC-E2-10 | E2-S4/S5; E1-S4 |
| NC-E2-11 | E2-S4/S5; E6-S3 |
| NC-E2-12 | E2-S3/S4 |
| NC-E3-1–4 | E3-S1/S5; U-ARB-02 |
| NC-E3-5–6 | E3-S2; U-ARB-03 |
| NC-E3-7–10 | E3-S3/S5; U-ARB-03 |
| NC-E3-11–12 | E3-S4/S5; LC-S2 |
| NC-E3-13 | E3-S5; E2-S4 |
| NC-E4-1–5 | E4-S1/S4; E2-S1 |
| NC-E4-6–9 | E4-S1/S2/S4; LC-S2 |
| NC-E4-10–11 | E4-S3/S4; E1-S3/S4; [U-ARB-04 spec](u-arb-04-atomic-load-prepare.md) |
| NC-E5-1–2 | E5-S1; U-ARB-01 |
| NC-E5-3–5 | E5-S2 |
| NC-E5-6–7 | E5-S3 |
| NC-E5-8 | E5-S4; E1-S2 |
| NC-E6-1–2 | E6-S1/S2/S4; README: эпохи |
| NC-E6-3–4 | LC-S1/S2; E6-S2 |
| NC-E6-5–6 | LC-S3; E6-S3/S4 |

## Сквозные ограничения арбитража

Номера соответствуют [cross-cutting-risks.md](../arbitration/cross-cutting-risks.md).
Они уже входят в арбитраж и не являются POST-ARB.

| № | Finding / прежний риск Astra | Реализация в спецификации |
| --- | --- | --- |
| 1. Раздельное состояние | E1-04/09, E2-04, E3-06, E6-03; N-06 | README; E2-S1/S4; E3-S4; LC-S2 |
| 2. Mapping и операция | R-02/03, E2-04, E4-01/05; N-02 | E1-S4; E2-S1; E4-S1/S2 |
| 3. Неатомарная запись | E4-02/04/06; N-01 | E4-S1/S2/S4; LC-S2 |
| 4. Write/load safety | E1-07, E3-01, E4-03/06; N-04 | E1-S3/S4; E4-S3; E3-S5; [U-ARB-04 spec](u-arb-04-atomic-load-prepare.md) |
| 5. Content/dependency/CLR identity | E2-01/02, закрытый E2-07, E3-01/02 | E2-S2; E3-S1/S2 |
| 6. Shared cache integrity | E2-03/05/06; N-05 | E2-S3/S4/S5 |
| 7. Выбор исполняемого кода | E3-04, E5-01/02/04 | E3-S3; E4-S1; E5-S1–S3; U-ARB-01/03 |
| 8. Load/enable одной сессии | E1-02/04, E6-03; N-03 | README: сквозные границы; E1-S4; LC-S1 |
| 9. Изоляция тестов | E1-03, E2-05, E3-01/04; N-07 | E1-S4; E2-S5 |
| 10. Совместимость lifecycle | E1-04, E2-04, E3-05, E6-01/04 | LC-S1–S3; E6-S3/S4; UNRESOLVED-v2 |

## Проверка downstream-последствий

Для каждого принятого finding результирующие разделы основной таблицы проверены
по следующим аспектам. «Не добавляется» означает отсутствие такого требования
в арбитраже, а не скрытое решение новой архитектуры.

| Аспект | Применимые решения и результат проверки |
| --- | --- |
| Доменная модель | R-03, E1-04/09, E2-01/04, E3-06, E4-01/05, E5-03/04: session, mapping, generation, operation base, стадии и path states определены в README/E2-S1/E3-S4/E4-S1/E5-S2/S3 |
| Инварианты | R-01/02/03, E2-01–06, E3-01/04/05/06, E4-01–06, E5-02/05: целостность, immutable publication, no overlay persistence и truthful status согласованы; loader/provenance gates не объявлены выполненными |
| API-контракты | E1-01/02/04/05, E2-04, E3-01/05/06, E4-01/02/04, E5-04, E6-01/03: новые public API не нужны; bool остаётся sticky; внутренние статусы и context точны |
| Persistence | E2-01/02/03/05/06, E4-02/04/05/06: versioned manifest, no-replace reuse, preflight и reconciliation не смешаны; mapping остаётся в памяти |
| Транзакции и согласованность | R-02/03, E1-08/09, E2-03/04/05, E4-01/02/04/05/06, E6-03: publication readiness, atomic snapshot и multi-file persistence явно различаются |
| Авторизация/безопасность | E2-03/05/06, E3-02/03/04, E4-02/05, E5-02/03/04/05: ownership/containment, scoped executable selection, отказ unknown diffs. Новые роли/права/подписи/sandbox не добавляются |
| Фоновая обработка | R-02, E1-06/08/09, E2-04/06, E4-06, E6-03: FSW delivery отдельно от flush, reapply без analyzer I/O, auto-build/auto-GC не вводятся |
| Messaging | E1-08, E3-06, E4-04, E5-04, E6-03: события FSW и MCP summary/status описаны; отдельный broker, очередь и обещания exactly-once не требуются |
| Наблюдаемость | Все E1-01–10, E2-02–06, E3-01–06, E4-02/03/04/06, E5-02–05, E6-01/03: exact marker/path, раздельные стадии и причины, G/S/M/A/L/D/R, ресурсные измерения |
| Ошибки | E1-07/08/09/10, E2-03/04/05/06, E3-01/02/04/05/06, E4-01/02/04, E5-02/04: injected failure, stale refresh, unsupported execution, partial writes, cancellation и gate-ограничения включены в приёмку |
| Миграции/совместимость | E1-04, E2-01/04/06, E4-02, E6-01/02/04: новый namespace без in-place migration, edit reuse вместо pickup, stricter unknown-diff refusal, DOC-EARLY и upgrade note; U-ARB-05 = session-sticky для совместимости optional non-nullable bool |
| Тестирование | Все 39 принятых findings имеют указанные разделы приёмки/аудита; E1 — исследовательский baseline, 2/4 — runtime reuse/inverse, 3/5 — условные gates, 6 — документационный аудит |
| Нефункциональные требования | E1-01/08/10, E2-02/05/06, E3-02/03/04/05, E5-03, E6-01/04: bounded waits, process isolation, цена hash/evaluation, рост memory/disk, операционный бюджет и restart cost. Новых численных SLA нет |

## Результат второго прохода

Проверены весь README, шесть эпох, lifecycle matrix и реестры. Устранены устаревший
«полный reload», безусловный in-process gate, helper-only gate эпохи 2, независимость
эпохи 4 от mapping, запрет любой публикации после failed apply и смешение rewrite
с execution. Whole-list wipe не используется как ожидаемый результат inverse tests.

При partial persistence oracle проверяет сохранённый subset и статус, при полном
успехе — весь запрос. Файловый запрет reapply не распространяется на текстовый sync
или lazy loader. DOC-EARLY обозначен ранней задачей, а не якобы выполненной правкой
защищённых файлов. Условные ветви missing/inaccessible и loader режима остаются
условными. Новых существенных проблем сверх арбитража не выявлено.

Покрытие: **42 findings = 16 ACCEPT + 23 ACCEPT WITH MODIFICATION + 2 REJECT +
1 UNRESOLVED**; 71 нормативный пункт; 10 сквозных ограничений; 5 U-ARB сохранены.
Это документальная проверка; functional/runtime tests не запускались.

Техническая проверка артефактов: созданы 12 файлов v2; все 501 локальные Markdown
ссылки разрешаются; число колонок строк каждой таблицы согласовано; потерянных
или дублирующихся строк verdict нет. В changelog присутствуют все 39 принятых
findings. SHA-256 всех 28 непосредственных входных файлов (7 v1, 7 review,
8 review-response, 6 arbitration) совпали до и после создания v2. Код этим
проходом не изменялся. Git не показывает изменений tracked-файлов.

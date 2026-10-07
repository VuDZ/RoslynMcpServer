# Арбитраж workspace load cache v2

Статус: **нормативный арбитраж для следующей редакции спецификации**. Этот проход
не изменяет исходный план [`proposal-v1/`](../proposal-v1/README.md), proposal
[`proposal-pre-arb/`](../proposal-pre-arb/README.md), review или ответы Astra и не реализует код.

## Иерархия источников

1. Исходные цели и ограничения из [`proposal-v1/`](../proposal-v1/README.md), обозначенные в
   ответах Astra как O1–O8: ускорение нового PID, отсутствие VCS-зависимости,
   ограниченный обход, live freshness после sync, reuse после изменения
   source/membership, inner TFM и in-memory overlay, самостоятельные
   metadata/partial направления, correctness-first.
2. Принятые продуктовые ограничения A-LOAD, A-STICKY, A-WRITE, A-ADMISSION,
   A-PROVENANCE и A-LOADER, перечисленные в
   `review-response/sources-and-scope.md`.
3. Проверенные факты кода на commit
   `9867318ddb5294ce144bf024b9a61a1a2e3814c3` (source version 1.3.21).
4. Proposal Astra, review Grok и ответы Astra — evidence и позиции сторон, но не
   самостоятельные источники нормы.

Первичное пользовательское задание, отдельное от исходного плана, в материалах
не найдено. Поэтому арбитраж сохраняет явно зафиксированные O1–O8 и не разрешает
снимать O4/O5/O7 одним изменением этапности proposal. Численные бюджеты и точная
целевая монорепа не восстанавливаются из документов и не выдумываются.

## Результаты

- [finding-verdicts.md](finding-verdicts.md) — независимый вердикт по всем 50
  finding ID.
- [arbitration-summary.md](arbitration-summary.md) — компактная матрица решений.
- [normative-change-set.md](normative-change-set.md) — только обязательные
  изменения следующей редакции.
- [unresolved.md](unresolved.md) — решения, для которых не хватает требования
  или evidence.
- [cross-cutting-risks.md](cross-cutting-risks.md) — ограничения согласованности
  между принятыми изменениями.

Новых `NEW-ARB-xxx` finding нет: существенные дополнительные риски уже выявлены
Astra как N1–N7 и включены в нормативные изменения или unresolved-вопросы.

# Response: защита спецификации плагинов MCP

Каталог ответов на [`../review-astra/`](../review-astra/README.md) и [`../review-ds/`](../review-ds/README.md).
Спека, эпохи, `authoring.md` и оба `review-*` не менялись.

Опора, по порядку: решения обсуждения (общий `SolutionManager`, без вызова тулов, теневая копия, один каталог — один плагин, `plugin.json` без сканирования DLL, шаблон и руководство); текущий код хоста на `1.4.15`; спецификация. Спека сама по себе доказательством не считается.

Идентификатор finding — `<каталог>/<локальный ID>`. Одинаковые локальные ID разных каталогов — разные findings.

Сводка, кластеры, набор правок и ledger: [`summary.md`](summary.md).

| ID | Sev ревью | Problem | Correction |
| --- | --- | --- | --- |
| [review-astra/R-01](review-astra/README.md) | High | ACCEPT | MODIFY |
| [review-astra/C-01](review-astra/authoring.md) | High | ACCEPT | ACCEPT |
| [review-astra/C-02](review-astra/authoring.md) | Medium | ACCEPT | ACCEPT |
| [review-astra/E1-01](review-astra/epoch-1-registration.md) | High | ACCEPT | MODIFY |
| [review-astra/E1-02](review-astra/epoch-1-registration.md) | Medium | ACCEPT | ACCEPT |
| [review-astra/E2-01](review-astra/epoch-2-manifest-and-paths.md) | High | ACCEPT | ACCEPT |
| [review-astra/E3-01](review-astra/epoch-3-shadow-copy.md) | Medium | ACCEPT | MODIFY |
| [review-astra/E4-01](review-astra/epoch-4-sample-plugin.md) | Blocker | ACCEPT | ACCEPT |
| [review-astra/E5-01](review-astra/epoch-5-assembly-load.md) | Blocker | ACCEPT | ACCEPT |
| [review-astra/E5-02](review-astra/epoch-5-assembly-load.md) | High | ACCEPT | ACCEPT |
| [review-astra/E5-03](review-astra/epoch-5-assembly-load.md) | High | PARTIALLY ACCEPT | MODIFY |
| [review-astra/E6-01](review-astra/epoch-6-host-startup.md) | Medium | ACCEPT | ACCEPT |
| [review-ds/R-01](review-ds/README.md) | High | PARTIALLY ACCEPT | MODIFY |
| [review-ds/E1-01](review-ds/epoch-1-registration.md) | Medium | ACCEPT | MODIFY |
| [review-ds/E2-01](review-ds/epoch-2-manifest-and-paths.md) | Medium | ACCEPT | ACCEPT |
| [review-ds/E4-01](review-ds/epoch-4-sample-plugin.md) | Blocker | ACCEPT | ACCEPT |
| [review-ds/E5-01](review-ds/epoch-5-assembly-load.md) | Medium | ACCEPT | MODIFY |
| [review-ds/E6-01](review-ds/epoch-6-host-startup.md) | High | ACCEPT | MODIFY |
| [review-ds/E6-02](review-ds/epoch-6-host-startup.md) | Medium | ACCEPT | ACCEPT |

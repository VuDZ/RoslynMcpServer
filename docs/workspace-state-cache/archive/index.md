# История workspace state cache

Статус: **archived, 2026-10-07**. Это исторические inputs/results, не текущий канон.
Актуальные [требования и эпохи](../README.md) находятся в корне темы.

- [Исходная specification](README.md), её epoch directories, [execution](execution.md),
  [benchmark](benchmark.md) и [отчёт подготовки](preparation-report.md).
- [Инструкции первоначального ревью](review/README.md).
- Независимые ревью: [Astra](review-astra/result.md), [DeepSeek](review-ds/result.md),
  [Grok](review-grok/result.md).
- [Защита автора](defense/result.md).
- [Арбитраж](arbitration/result.md), [Decision Ledger](arbitration/decision-ledger.md),
  [Proposed Changes](arbitration/proposed-changes.md), unresolved/spikes и их evidence.

Источник, исходные IDs, решения и статистика сохранены. Markdown links rebased,
к исходному README добавлена archival notice. SHA-256 snapshots/JSON и исходный
validator арбитража оставлены как исторические данные до переноса: paths в них
отражают прежнее расположение, а pass counts — тот момент проверки. Не запускать
historical validator как проверку текущего канона: он рассчитан на прежние paths/
inventory и перезаписывает исторические validation outputs.

Текущая проверка всей темы выполняется [root validator](../validate-artifacts.py).
Связь прежних и нынешних paths/hashes — [relocation manifest](../relocation-manifest.json),
scope и результат — [relocation report](../relocation-report.md).

# Finding Registry

Дата: **2026-10-07**. Run: `workspace-state-cache-arbitration-2026-10-07`.
Вход: 16 findings из трёх `review-*`; author defense доступна для каждого.
Registry составлен до записи verdicts. Итоги — в [decision-ledger.md](decision-ledger.md).

| Source | Arbitration cluster | Первичная классификация |
|---|---|---|
| [review-astra/F-001](../review-astra/findings/F-001.md) | [ARB-005](findings/ARB-005.md) | CONSENSUS: generation binding |
| [review-astra/F-002](../review-astra/findings/F-002.md) | [ARB-006](findings/ARB-006.md) | CONSENSUS: environment policy |
| [review-astra/F-003](../review-astra/findings/F-003.md) | [ARB-015](findings/ARB-015.md) | CONSENSUS: resolution predicate |
| [review-ds/F-001](../review-ds/findings/F-001.md) | [ARB-003](findings/ARB-003.md) | CORRECTION DISPUTE: request identity |
| [review-ds/F-002](../review-ds/findings/F-002.md) | [ARB-004](findings/ARB-004.md) | PARTIAL / BOUNDARY: overlay scope |
| [review-ds/F-003](../review-ds/findings/F-003.md) | [ARB-002](findings/ARB-002.md) | CORRECTION DISPUTE: early capability gate |
| [review-ds/F-004](../review-ds/findings/F-004.md) | [ARB-007](findings/ARB-007.md) | PARTIAL / BOUNDARY: health versus coverage |
| [review-ds/F-005](../review-ds/findings/F-005.md) | [ARB-008](findings/ARB-008.md) | PARTIAL / BOUNDARY: lazy opt-in |
| [review-ds/F-006](../review-ds/findings/F-006.md) | [ARB-009](findings/ARB-009.md) | PARTIAL / BOUNDARY: artifact ownership |
| [review-ds/F-007](../review-ds/findings/F-007.md) | [ARB-010](findings/ARB-010.md) | CORRECTION DISPUTE: initialization versus apply |
| [review-ds/F-008](../review-ds/findings/F-008.md) | [ARB-011](findings/ARB-011.md) | CONSENSUS: role restoration |
| [review-grok/F-001](../review-grok/findings/F-001.md) | [ARB-001](findings/ARB-001.md) | UNRESOLVED REQUIREMENT: fidelity |
| [review-grok/F-002](../review-grok/findings/F-002.md) | [ARB-012](findings/ARB-012.md) | PROBLEM / CORRECTION DISPUTE: raw import evidence |
| [review-grok/F-003](../review-grok/findings/F-003.md) | [ARB-013](findings/ARB-013.md) | CONSENSUS: config discovery |
| [review-grok/F-004](../review-grok/findings/F-004.md) | [ARB-014](findings/ARB-014.md) | CONSENSUS: content-only eligibility |
| [review-grok/F-005](../review-grok/findings/F-005.md) | [ARB-003](findings/ARB-003.md) | CORRECTION DISPUTE: request identity |

## Обоснование кластеризации

**ARB-003** объединяет две части одного отсутствующего контракта: что именно
сравнивает disk lookup/admission. DS рассматривает omitted/effective globals,
Grok — параметры и semantic modes. Их prescriptions requested-only и effective+mode
нельзя принять независимо: нужен один непротиворечивый контракт identity.
Independent corroboration: **yes**, review-ds и review-grok; независимость заявлена
в coverage самих reviewers, скрытая история сессий не проверялась. Совпадение
не служит основанием verdict.

Остальные **14** clusters имеют по одному source. Не объединены:

- ARB-001 и ARB-002: допустимая fidelity и проверка достижимости выбранного контроля — разные решения.
- ARB-003 и ARB-004: правильный ключ не создаёт fresh analyzer provenance.
- ARB-005 и ARB-012: список dependencies не доказывает, какие bytes породили graph.
- ARB-007 и ARB-011: load health и occurrence roles не заменяют друг друга.
- ARB-013 и ARB-011: обнаружение нового config и восстановление роли известного input — разные failure modes.
- ARB-014 и ARB-015: зависимость options от source и metadata resolution требуют разных predicates.
- ARB-009 и ARB-002: executable artifact ownership и real capability имеют разное evidence.

Прямого противоположного verdict двух reviewers об одной проблеме нет.
Различия corrections по identity разбираются совместно в ARB-003; различие
«новый spike нужен / не нужен» по representability — совместно через ARB-001/002
и [S-001](spikes.md#s-001--capability-выбранного-real-control).

Defense не вводит самостоятельных D-findings. Новых A-NEW findings не создано.
Промпт пользователя имеет приоритет над форматом skill; все outputs остаются здесь.

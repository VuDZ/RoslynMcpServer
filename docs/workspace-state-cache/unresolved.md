# Открытое решение specification v2

Дата: **2026-10-07**. Статус: **PARTIALLY RESOLVED**: XML-doc часть решена H-001, strong-name часть **UNRESOLVED**.
Арбитраж завершён; это обязательный decision point для будущего positive scope,
а не новая оценка finding. Источник: [ARB-001](archive/arbitration/findings/ARB-001.md),
[полное решение с рисками](archive/arbitration/unresolved.md).

## U-001 — Fidelity положительного E0

Владелец исключил XML-документацию бинарных зависимостей из гарантии MVP:
[H-001 и точные границы](human-decisions.md#h-001--документация-бинарных-зависимостей-исключена-из-гарантии-mvp).
Исходники загруженных проектов и их комментарии сохраняются. Documentation-dependent
операции/потребители получают ordinary route либо явный отказ до side effects.

**Остаточный вопрос:** какую observable equivalence должны сохранять E0/E1 для
strong-name provider state, чьи search paths существующий strict capture публично
не читает? Требуется source-backed точное восстановление либо разрешён явно
ограниченный signing/diagnostic/emit contract? Ни одна strong-name альтернатива не выбрана.

**Решение требуется до:** принятия positive E0 scope и dependent codec/hydrate;
S-001 выполняется после выбора границы. Решение влияет на E0 schema/capture/restore/
round-trip, E1 support/write boundary, metadata reference semantics E2 и benchmark
compare key. Preliminary inventory/schema sketch и остальные текстовые требования
можно формулировать; XML граница выбрана, полный executable fidelity contract ожидает strong-name решения.

### A — Source-backed точное восстановление

Сохранить strict contract и подтвердить независимо captured constructor/effective
inputs strong-name provider, их version binding и восстановление в S-001.
Один symbol query сам по себе signing/emit fidelity не доказывает.

Преимущество — сохранение заявленной fidelity. Риски — пока недоказанный источник,
возможный дополнительный adapter/version checks и no-go real control. При отсутствии
evidence positive E0 blocked; более широкий profile не наследует успех узкого.

### B — Явно ограниченная observable семантика

Для оставшейся strong-name части владелец может разрешить конкретные исключения signing/emit функций,
сохранив реальный positive query и поддержанные writes. Тогда обязательны перечень
операций, наблюдаемые потери, unsupported cases и ordinary-load route до side effects.
Не сравнивать два поля недостаточно; help/outcome и acceptance matrix должны отражать
ограничение. No silent defaults сохраняется: сознательно ограниченная реконструкция
не называется точным переносом нечитаемого state.

Преимущество — достижимый узкий working slice. Риски — иные signing/emit
результаты и сокращение полезного corpus. Ordinary symbol query не доказывает
безопасность неизвестных операций. S-001 проверяет supported результаты и отказ/
fallback для excluded операций после явного изменения соответствующего contract.

### C — Strict contract и отложенный positive scope

Сохранить точность и отложить positive scope до достаточного evidence. Это не
обещает недоказанную fidelity, но останавливает positive E0/E1. Negative harness
остаётся evidence и не засчитывается как выполнение real-positive приёмки.
Blocked/deferred outcome требует решения владельца; редактор его не объявляет.

**Strong-name вариант не выбран.** XML-doc исключение уже принято H-001. Снятие real-positive требования и negative-only E0
success не являются эквивалентным исправлением. До решения нельзя терять references,
подставлять null/default, применять private reflection или объявлять неподтверждённый
reusable capture пригодным. Безопасный ordinary path сохраняется.

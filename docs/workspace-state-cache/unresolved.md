# Решение fidelity specification v2

Дата: **2026-10-07**. Статус: **RESOLVED — H-001 + H-002; вариант B**.
Открытых owner decisions в U-001 нет. Историческое unresolved решение арбитража
сохранено в [archive](archive/arbitration/unresolved.md); последующие решения
владельца не переписывают этот snapshot.

## U-001 — Fidelity положительного E0

**Принятый scope:** ограниченная observable семантика MVP.

- [H-001](human-decisions.md#h-001--документация-бинарных-зависимостей-исключена-из-гарантии-mvp):
  XML documentation provider/text бинарных references вне гарантии; source comments
  загруженных проектов сохраняются. Documentation-dependent consumers получают
  ordinary route либо явный отказ до execution/side effects.
- [H-002](human-decisions.md#h-002--ограниченный-strong-name-contract-mvp): точный
  перенос непубличного strong-name provider state не требуется для подтверждённого
  signing-independent scope. Signing-dependent и неопределённые requests идут через
  whole-request ordinary load; невозможность перехода означает отказ до операции.
  Emit/signing из восстановленной compilation вне первоначального scope.

Сохраняются semantic results и корректность поддержанных writes, metadata/reference
properties, source comments и remaining mandatory state. Signing flags/csproj/refs
не изменяются ради допуска. Подписанные внешние DLL не исключаются автоматически.
Один symbol query или `SignAssembly=false` не доказывает пригодность всего request.
Точные admission controls и объявленная public reconstruction policy проверяются
в S-001; private reflection и silent defaults остаются запрещены.

### Последствия выбора

Decision prerequisite S-001 выполнен. Task-00 и сам [S-001](spikes.md) ещё **planned /
not-run**; positive E0/dependent executable codec/hydrate требуют успешного real
capability verdict. Неудача эксперимента возвращает scope владельцу, а не превращает
negative-only harness в выполнение real-positive приёмки.

Вариант A (source-backed точное восстановление) не выбран как обязательный MVP scope;
вариант C (отложить positive scope) не выбран. Возможное будущее расширение fidelity
требует отдельного evidence и приёмки. Closure U-001 не означает реализации,
независимого review, принятия эпох или public activation.

Принятое решение и дата: **2026-10-07, B по H-002 с ранее принятым H-001**.
Применение к требованиям и проверка: [H-002 report](h-002-report.md).

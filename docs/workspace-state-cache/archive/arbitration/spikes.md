# Required Spikes

Дата: **2026-10-07**. Два ограниченных эксперимента, **not-run** в этом арбитраже.
Они описаны как результат adjudication; исходники/harness не изменялись.

## S-001 — Capability выбранного real control

Sources: [ARB-002](findings/ARB-002.md), review-ds/F-003.
Specification consequence: [P-001](proposed-changes.md#p-001--ранний-capability-gate).
Dependency: U-001.

### Question

Достижим ли positive ordinary capture/hydrate выбранного неизменённого реального
контроля под явно выбранным fidelity contract?

### Why evidence is insufficient

Исторические provider refusals доказали ограничения observer, не работоспособность
нового accepted subset. Concrete runtime providers, instance bindings и допустимый
источник constructor inputs выбранного контроля не исследованы этим арбитражем.

### Experiment

Зафиксировать server/toolset versions, control revision, properties, ожидаемый query
и выбранный U-001 outcome. Выполнить обычный MSBuildWorkspace load без удаления refs
или переписывания csproj ради успеха. Снять bounded inventory actual metadata/provider
types, publicly representable options, project-instance identities и источников
необходимого состояния. Проверить минимальную реконструкцию принятого contract через
public APIs; сравнить заранее названные observable results с ordinary baseline.
Prototype для проверки может быть минимальным, не требует готового production codec/store.

### Success criteria

Есть хотя бы один real positive capability verdict с actual metadata/project references,
однозначными instances и объяснимым источником каждого обязательного state. Нет silent
defaults, удаления refs и утверждения unsupported функций. Если outcome U-001=B,
явно проверены как поддержанные результаты, так и отказ/fallback excluded операций.
Готовность full cross-process E0 по-прежнему подтверждается task-06, не присваивается
одним inspection pass.

### Possible outcomes

- Supported: зафиксировать конкретный slice/evidence и продолжить schema/capture/codec/hydrate.
- Partial: сужение допускается только внутри принятого U-001; иначе возврат владельцу.
- Unsupported: E0 positive scope blocked; codec/hydrate успех synthetic fixture не закрывает gate.

### Decision deadline

После U-001, до начала dependent codec/hydrate реализации для выбранного positive scope.
Schema sketch/inspection не зависят от готового полного capture; dependency cycle не вводится.
Общий SDK/restore closure audit и повторный поиск уже отсутствующего getter исключены.

## S-002 — Raw import/restore evidence

Sources: [ARB-012](findings/ARB-012.md), review-grok/F-002.
Specification consequence: [P-011](proposed-changes.md#p-011--явный-evidence-source-и-его-проверка).

### Question

Какие required dependency paths и instance bindings фактически доступны в ordinary
design-time binlog при текущем ProjectImports=None, и каких данных не хватает?

### Why evidence is insufficient

Current ReplayOne не строит imports inventory; generic properties могут содержать
часть restore evidence, но их installed-version состав не подтверждён. Documentation
про file collection не является inventory raw events actual MSBuildWorkspace load.

### Experiment

На owned контрольной копии выполнить actual ordinary MSBuildWorkspace open с текущим
BinaryLogger mode, сохранив для анализа raw logs до cleanup. Зафиксировать versions,
event types, import paths, project/evaluation contexts и relevant restore properties.
Сопоставить expected controlled inputs с обнаруженными: nonstandard nested import,
conditional import/known absence, shared import с несколькими owners, explicit external
import, redirected assets/intermediate paths и применимые lock/NuGet configuration inputs.
Не считать один ProjectStarted dictionary или обычный CLI build эквивалентом всего
design-time capture. Unknown category должна быть показана отдельно.

### Success criteria

Для заявленного finite profile есть traceable mapping required category → event/property
source → actual path → all consumer instances; missing/conflicting context даёт
incomplete/unsupported. Expected imports/restore paths не выведены только из старого DTO
или conventional obj names. Есть один unchanged positive реальный request для выбранной
границы и negative controls, где недостающее evidence не допускает reusable capture.
Availability raw paths не считается consumed-generation proof P-004.

### Possible outcomes

- Existing channel sufficient: зафиксировать точные sources и дополнить extraction
  в пределах принятого adapter, сохраняя None и existing provenance gate semantics.
- Partial: перечислить отсутствующие категории; выбрать ограниченный допустимый profile
  либо обосновать дополнительный канал. Source embedding не включается автоматически.
- Insufficient for any real positive request: E1 manifest readiness blocked; отдельная
  корректировка scope/source, а не all-unsupported «успех».

### Decision deadline

До принятия E1/task-00 manifest adapter и разрешения reusable disk envelope.
Не блокирует изолированный E0 round-trip. Второй log, новый collection mode и их
стоимость выбираются только по обнаруженным gaps, не из слова None.

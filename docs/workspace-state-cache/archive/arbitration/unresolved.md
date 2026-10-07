# Unresolved

Дата: **2026-10-07**. Один обязательный human decision; он не подменён экспериментом
или предложенным P. Арбитраж завершён с этим открытым решением.

## U-001 — Fidelity положительного E0

### Decision required

Какую observable equivalence E0/E1 обязаны сохранять для XML documentation provider
и strong-name provider state, которые имеющийся strict capture публично не читает?
Разрешён ли явно ограниченный semantic contract либо требуется independently captured
constructor/effective-input evidence для точного восстановления?

Affected findings: [ARB-001](findings/ARB-001.md), source review-grok/F-001.
Зависимые: ARB-002/S-001, P-008/P-014 и дальнейший positive E1.

### Why now

От ответа зависит, какой capture считается успешным и что schema должна хранить.
Это необходимо до принятия positive E0 scope и dependent codec/hydrate, а не только
перед поздним rollout. Проверить уже известное отсутствие getter ещё раз недостаточно.

### Relevant requirements / constraints

- [E0 spec](../epoch-0-snapshot-roundtrip/spec.md): public API, точное представление
  либо отказ, no private reflection/silent defaults; real ordinary round-trip обязателен.
- [Task-00](../epoch-0-snapshot-roundtrip/task-00-current-seams-and-controls-med.md):
  неизменённый real control с metadata/project references.
- [Current experiment](../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs):
  stock reference и DesktopStrongNameProvider refusals; synthetic-owned state не доказывает ordinary capture.
- No evidence of разрешения терять XML-doc/signing поведение. Отдельно допустимая
  mvp-fast binary heuristic такого разрешения не создаёт.

### Option A — Сохранить fidelity и получить source-backed reconstruction

Получать необходимые constructor/effective inputs из проверяемого источника ordinary
loader для ограниченного профиля, затем восстанавливать public objects и проверять
поведение. Отсутствующий getter не обходится private reflection и не заменяется догадкой.

#### Advantages

Сохраняет текущий strict смысл «точное представление либо отказ» и уменьшает расхождение
ordinary/hydrated observable semantics; не требует молчаливого исключения функций.

#### Risks

Достаточный источник пока не доказан. Привязка к loader/toolset version может потребовать
дополнительного adapter и checks. XML sidecar path или один symbol query не доказывают
выбранный provider. Работа может закончиться no-go на выбранном real control.

#### Consequences

S-001 должен проверить наличие такого evidence и восстановление в заявленной границе.
При его отсутствии E0 blocked, не accepted. Schema включает только доказанные portable
inputs; более широкий профиль не наследует успех узкого.

### Option B — Явно сузить observable semantic contract

Владелец разрешает конкретные исключения для XML-doc/strong-name функций, сохраняя
реальный positive query и поддержанные writes. Нужно перечислить поддержанные операции,
потери, unsupported cases и ordinary-load route. «Не сравниваем два поля» недостаточно:
ограничение должно иметь наблюдаемый смысл для потребителя.

#### Advantages

Может сделать narrow working slice достижимым без общего восстановления скрытого
provider state. Позволяет продолжить staged MVP с явно измеряемой полезностью.

#### Risks

Меняется смысл эквивалентности, возможны разные XML-doc ответы или signing/emit outcomes.
Потери могут затронуть реальные tools; неизвестную операцию нельзя объявить безопасной
только потому, что baseline symbol query прошёл. Ограничения могут сузить полезный corpus.

#### Consequences

Требуется явная правка E0/E1 contract, help/outcome и acceptance matrix на следующей
стадии. Unsupported операции должны получать предусмотренный отказ/fallback до side
effects. No silent defaults остаётся запретом: разрешённая реконструкция описывается
как сознательная ограниченная семантика, а не как точный перенос нечитаемого состояния.

### Other viable options

**Option C — Сохранить strict contract и отложить positive scope до появления evidence.**

- Advantages: не обещает неподтверждённую fidelity и не требует немедленно расширять adapter scope.
- Risks: не даёт требуемого работающего snapshot MVP; прогресс E0/E1 останавливается.
- Consequences: E0 остаётся blocked/deferred по решению владельца; negative harness
  можно сохранить как evidence, но нельзя зачесть выполнение исходной positive приёмки.

Снятие real-positive требования и объявление negative-only E0 успехом не является
эквивалентным исправлением текущего запроса. Это отдельная смена цели, здесь не предложенная.

### Arbiter assessment

Предпочтительно сначала определить, какие observable операции действительно обязательны.
Если fidelity требуется без исключений, выбрать A с bounded S-001 и честным no-go.
Если владелец приоритетно принимает узкий working MVP, B допустим только с точным
перечнем потерь и независимыми behavioral controls. Недостаточно данных, чтобы выбрать
один из этих trade-offs за владельца; рекомендация не является финальным решением.

### Decision impact

E0 spec/task-00/02/03/05/06, reference/options schema, S-001, E1 support/write boundary
описание и benchmark compare key. До решения безопасно сохранять ordinary path и
refuse неподтверждённый reusable capture. Работа над самим arbitration завершена;
implementation/plan acceptance этим не выдана.

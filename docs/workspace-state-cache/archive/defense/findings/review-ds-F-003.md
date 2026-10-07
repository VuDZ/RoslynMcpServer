# review-ds/F-003 — Обязательный real positive capture может быть недостижим: нет gate и contingency

Дата: **2026-10-07**. Source: [review-ds/F-003](../../review-ds/findings/F-003.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Known provider/API refusals могут остановить E0 real positive round-trip поздно; нет раннего representability gate и явной contingency.

## Relevant requirements / constraints

- Положительный реальный round-trip обязателен; negative-only harness не считается MVP.
- No silent defaults, no private reflection и запрет удалить references ради success действуют до изменения владельцем semantic scope.
- Пользователь просил небольшие steps и повторное использование выполненных исследований, а не новую глубокую feasibility серию.

## Evidence checked

- [spec.md](../../epoch-0-snapshot-roundtrip/spec.md)
- [task-00-current-seams-and-controls-med.md](../../epoch-0-snapshot-roundtrip/task-00-current-seams-and-controls-med.md)
- [task-03-ordinary-capture-med.md](../../epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md)
- [Открытые вопросы representability](../../preparation-report.md)
- [Historical public API limitations](../../../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md)
- [CaptureAsync/CaptureCompilationOptions refusals](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs)
- [Unobservable_public_api_option_values_are_explicit_capture_refusals](../../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperimentTests.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю необходимость раннего verdict по representability и определённого no-go маршрута. Task-00 сейчас выбирает control и перечисляет gaps, но codec/hydrate могут начаться до проверенного положительного capture contract. Готовность схемы не делает ordinary reference publicly observable.

Модифицирую предложенный mandatory новый capture spike до task-02: повторный запуск старого strict capture, который отказывает любому stock file reference, не создаст отсутствующий getter. Для известных ограничений сначала нужен contract decision, отражённый в review-grok/F-001. Затем короткая ordinary inspection/capture capability check проверяет chosen slice; schema task не должна зависеть от ещё не реализованного полного нового capture adapter.

Не принимаю вариант «E0 negative-only accepted» как выполнение исходного пользователя: оно вновь оставит сохранение/загрузку без работающего результата. Нельзя также автоматически исключить XML-doc/signing fidelity без решения. Early gate может выявить blocked, но не должен стать большой closure-program.

## Position rationale

ACCEPT WITH MODIFICATION: раннее выявление и contingency нужны; повторное исследование уже известного отсутствующего API и круговая dependency schema→capture→schema не нужны.

## Proposed response

Suggested change: до downstream codec/hydrate закрепить принятое semantic fidelity решение и короткий positive-capability verdict выбранного real control. При отказе сохранить blocked reason и вернуть scope на решение, без silent defaults/удаления references. Известные API limits использовать прямо; динамическая проверка нужна для concrete selected instance/constructor context, не для повторного доказательства отсутствия getter. Выбор разрешённой потери свойств остаётся UNRESOLVED в отдельном исходном finding.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-grok/F-001 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-006 — связь для рассмотрения, не объединённый dispute.

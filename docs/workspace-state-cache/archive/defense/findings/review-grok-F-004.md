# review-grok/F-004 — Content-only не определяет target, который зависит от bytes уже известного source

Дата: **2026-10-07**. Source: [review-grok/F-004](../../review-grok/findings/F-004.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT

## Finding summary

Исключение source-bytes-dependent targets из content-only не имеет наблюдаемого support predicate; известный source path не гарантирует неизменность evaluated options.

## Relevant requirements / constraints

- Content-only reuse допускается при независимости graph/options от изменённых bytes.
- План не вводит общего анализа тел MSBuild tasks и не обещает arbitrary custom targets.
- Пользователь предпочёл staged MVP; граница supported profile должна быть конечной и проверяемой.

## Evidence checked

- [Результат и Правила dirty](../../epoch-2-content-refresh/spec.md)
- [task-00-startup-diff-roles-hi.md](../../epoch-2-content-refresh/task-00-startup-diff-roles-hi.md)
- [Custom targets с unknown additional inputs](../../README.md)
- [CustomTasks category unknown](../../../../../Services/Workspace/WorkspaceInputMapBuilder.cs)
- [ReplayOne не анализирует тела targets](../../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю: initial phrase «custom target читает source bytes» не стала исполняемым критерием. Файловый diff может быть полностью точным и всё равно дать неправильное reuse решение, если options зависят от изменившегося source. Known paths и «нет новых additional inputs» не являются доказательством независимости evaluation.

MVP-compatible outcome — явный finite support predicate для content-only, основанный на captured import/target profile и его проверенной policy. Нераспознанный target/condition path не анализируется на лету classifier-ом и не считается безопасным по default. Общий target interpreter пользователю не нужен.

Не следует приравнивать любой пользовательский .props файл к arbitrary executable task: простой verified configuration-only import может быть описан профилем. И наоборот, название файла .props не доказывает отсутствия target/property function effects. Список разрешённых форм/профилей и impact на real control ещё требуют выбора, а не blanket доверия имени SDK.

## Position rationale

ACCEPT: underlying contract gap существует независимо от того, какой конечный allowlist позже будет выбран.

## Proposed response

Suggested change: задать observable content-only eligibility и negative control, где existing source mutation меняет DefineConstants/AllowUnsafe через неподдержанный target. Unknown/non-admitted imports или conditions дают graph dirty/ordinary fallback; это проверка profile, не semantic body inference. Reviewer allowlist — подходящее направление; точный состав и вопрос поддержки пользовательских targets остаются owner/design выбором.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-grok/F-002 — связь для рассмотрения, не объединённый dispute.
- review-astra/F-003 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-008 — связь для рассмотрения, не объединённый dispute.

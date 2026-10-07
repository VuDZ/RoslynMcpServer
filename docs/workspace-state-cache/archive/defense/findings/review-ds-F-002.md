# review-ds/F-002 — Overlay mode не может получить fresh provenance на hydrated host: disk hit недостижим и не заявлен как ограничение

Дата: **2026-10-07**. Source: [review-ds/F-002](../../review-ds/findings/F-002.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

UNRESOLVED

## Finding summary

При active overlay текущий fresh provenance создаётся через MSBuild open; не определено, обязан ли E1 давать overlay disk hit или заранее выполнять ordinary fallback.

## Relevant requirements / constraints

- Existing same-session provenance, Banned/Unavailable и restart-required правила не ослабляются.
- Новый PID не наследует overlay session/provenance; task-05 допускает fresh adapter либо whole-request fallback.
- В пользовательском обсуждении не было отдельного решения, что overlay+disk hit обязателен для первого MVP.

## Evidence checked

- [E1: Интеграция](../../epoch-1-disk-cache-mvp/spec.md)
- [task-05-fresh-session-preparation-hi.md](../../epoch-1-disk-cache-mvp/task-05-fresh-session-preparation-hi.md)
- [task-09-restart-and-failure-integration-med.md](../../epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md)
- [E1 scenarios и overlay compare key](../../benchmark.md)
- [OpenAndCaptureAsync](../../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs)
- [null/foreign snapshot refusal](../../../../../Services/Analyzers/AnalyzerProvenanceCaptureGate.cs)
- [LoadAndPrepareAsync: unsuitable capture → Unavailable](../../../../../Services/Workspace/SolutionManager.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

На текущем production механизме замечание технически подтверждается: suitable snapshot для opt-in overlay получает ordinary open. Нет принятого исполняемого disk-only fresh binding adapter. Я не могу обещать overlay hit на основании планируемого task-05.

Уточняю часть claim: отсутствие fallback вообще или его absence во всех tasks не подтверждается. E1 spec прямо требует whole-request ordinary fallback при отсутствии fresh admission, а task-05 проверяет supported generator либо request fallback и sticky matrix. Однако явной mode support matrix и отдельного overlay benchmark/negative integration case действительно нет. Будущий adapter предусмотрен task-05, поэтому текущий код не доказывает невозможность любой будущей реализации.

Нужен выбор владельца: первый MVP сохраняет overlay через обычную загрузку либо получает отдельный portable admission scope. Нельзя молча выбрать постоянный запрет overlay hit или ослабить current gate. Пока положительный adapter не подтверждён, безопасное поведение — fallback до публикации hydrated candidate; это не должно искусственно превращать пригодный ordinary request в Unavailable. Если сама ordinary preparation запрещена, её настоящий Banned/Unavailable результат сохраняется.

## Position rationale

UNRESOLVED: требование к поддержке режима и допустимая дополнительная работа не определены; реальные ограничения и уже заданный fallback различены.

## Proposed response

Для разрешения: owner choice «overlay hit не входит в E1» либо конкретный fresh admission mechanism/evidence. Автор рекомендует для первого MVP mode-specific ordinary fallback с bounded reason, отдельный new-PID overlay negative-control и отдельную строку benchmark. Это предложение, не утверждённое ограничение. Успех E1 на overlay-off контроле не обобщать на overlay-on.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-grok/F-005 — связь для рассмотрения, не объединённый dispute.
- review-grok/F-001 — связь для рассмотрения, не объединённый dispute.

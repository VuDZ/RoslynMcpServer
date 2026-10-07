# review-ds/F-005 — Config-driven lazy load не может включить disk cache: путь и владелец флага не определены

Дата: **2026-10-07**. Source: [review-ds/F-005](../../review-ds/findings/F-005.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Config-driven lazy load не имеет заявленного disk-cache opt-in; scope и owner flag между явным tool и lazy entry не определены явно.

## Relevant requirements / constraints

- Первый MVP opt-in; default нельзя включать молча для lazy entry.
- Существующий lazy load использует тот же manager path и FileSettings load properties.
- Прямое пользовательское требование добавить новый config key отсутствует; принятие explicit-only scope нельзя выводить из недокументированного intent.

## Evidence checked

- [Proposed load_workspace flag и disabled semantics](../../README.md)
- [task-06-load-save-and-restore-hi.md](../../epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md)
- [EnsureWorkspaceFromConfigAsync](../../../../../Services/Workspace/SolutionManager.cs)
- [KnownKeys и load property fields](../../../../../Config/RoslynMcpFileSettings.cs)
- [Explicit LoadWorkspace entry](../../../../../Tools/WorkspaceTools.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю необходимость описать оба entry points и исход flag. Бенчмарк explicit tool не доказывает ускорение config-driven первого semantic call. Никакого ранее согласованного решения включать cache по умолчанию для lazy load нет.

Модифицирую claim про неопределённое значение и отсутствие всех arguments: естественное применение proposed default false даёт disabled, а lazy code уже передаёт FileSettings.Configuration/Platform/TargetFramework. В FileSettings есть эти ключи, не только workspace-path. Следовательно, ambiguity относится к продуктовому охвату opt-in и его документированию, а не к неизбежно неопределённым globals.

Оба suggested reviewer варианта допустимы. Для малого E1 предлагаю явно оставить config-driven load disabled/not-attempted и включение через explicit tool. Если владелец хочет прозрачный lazy disk cache, отдельный config key и precedence также небольшая работа, но не обязанность из текущего scope. Итоговый выбор ещё не утверждён.

## Position rationale

ACCEPT WITH MODIFICATION: doc/scope gap есть; он не требует автоматического нового config key или изменения default.

## Proposed response

Suggested change: E1 явно описывает disk policy для ExplicitLoad и ConfigFile. Добавить negative lazy-control с отсутствующим opt-in и сохранением текущих FileSettings globals. Proposed минимальный вариант — explicit-only disk opt-in, lazy disabled reason. Если selected scope включает config opt-in, зафиксировать ключ/precedence/identity tests; это owner choice, не сделанная здесь правка.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-001 — связь для рассмотрения, не объединённый dispute.

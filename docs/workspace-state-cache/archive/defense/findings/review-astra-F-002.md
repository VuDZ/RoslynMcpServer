# review-astra/F-002 — Не определена валидация evaluation environment между PID

Дата: **2026-10-07**. Source: [review-astra/F-002](../../review-astra/findings/F-002.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT

## Finding summary

File hashes и SDK/version fingerprint не задают evaluation environment нового PID; не определены current environment comparison и fallback при неизвестной зависимости.

## Relevant requirements / constraints

- Разные semantic graphs не объединяются одним cache identity; cross-process reuse должен соответствовать ordinary load того же входного контекста.
- Поддержка arbitrary MSBuild не обещается, но обычная property expansion не обязана использовать custom tasks.
- MVP остаётся небольшим: фиксируется выбранная environment policy, без обязательного исследования всех возможных процессов/машин.

## Evidence checked

- [README: Индекс входов, Поведение первого MVP](../../README.md)
- [E1: Стартовая проверка](../../epoch-1-disk-cache-mvp/spec.md)
- [Create: явно передаваемые global properties](../../../../../Services/Workspace/MsBuildWorkspaceProperties.cs)
- [LoadAndPrepareAsync/LoadCoreAsync](../../../../../Services/Workspace/SolutionManager.cs)
- [Microsoft Learn: Environment properties](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-properties?view=visualstudio#environment-properties), прочитано 2026-10-07.

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Проблему принимаю. Независимо от того, будет ли ключ строиться по requested или заранее разрешённым host globals, ambient MSBuild properties не сводятся к Configuration/Platform/TFM. Properties из environment могут изменить condition/DefineConstants при неизменных файлах. Microsoft Learn подтверждает загрузку environment properties при инициализации property collection; сохранённое старое effective value не является текущей проверкой.

Исходная программа v2 environment fingerprint предусматривала, но она отложена: её требования нельзя автоматически вернуть в MVP. Нужен собственный короткий contract. Принимаю defect независимо от совпадения терминов с v2.

Варианты controlled evaluation, broad fingerprint и finite relevant-variable evidence имеют разные compatibility/miss trade-offs. Их выбор этой защитой не утверждается. Одно allowlisted значение допустимо как гарантия только вместе с границей supported profile: иначе неизвестная переменная останется скрытым input.

## Position rationale

ACCEPT: отсутствие policy подтверждено по текущему README/spec и применимо к обычным MSBuild properties, а не только будущей сложной нагрузке.

## Proposed response

Suggested change: зафиксировать evaluation-environment policy, её support predicate и независимое получение current values до reuse; непроверенная значимая зависимость даёт fallback. Capture context и read context используют одинаковые правила. Добавить absent→present/changed-value new-PID controls. Не переносить raw secrets в envelope/logs; способ безопасного сравнения и выбор policy требуют отдельного design/owner решения.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-ds/F-001 — связь для рассмотрения, не объединённый dispute.
- review-grok/F-004 — связь для рассмотрения, не объединённый dispute.

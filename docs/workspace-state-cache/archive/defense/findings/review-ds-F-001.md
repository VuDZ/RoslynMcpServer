# review-ds/F-001 — Ключ disk cache не определяет сравнение omitted и effective load properties в новом PID

Дата: **2026-10-07**. Source: [review-ds/F-001](../../review-ds/findings/F-001.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT WITH MODIFICATION

## Finding summary

Cold disk lookup не определяет равенство omitted/requested/effective properties; перенос RAM wildcard matching на новый PID может выбрать Release envelope вместо ordinary defaults.

## Relevant requirements / constraints

- Существующие RAM omitted/sticky semantics сохраняются; новое cold disk matching должно сравнивать тот контекст, который действительно передаётся ordinary loader.
- Loaded defaults живут в сессии, FileSettings могут задавать configuration/platform/TFM; derived MSBuild defaults не берутся из старого envelope.
- Неподдержанный или неустановленный контекст означает miss, не guessing.

## Evidence checked

- [README: Поведение первого MVP](../../README.md)
- [task-06-load-save-and-restore-hi.md](../../epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md)
- [MatchesPassedLoadArguments и IsSameLoadCache](../../../../../Services/Workspace/MsBuildWorkspaceProperties.cs)
- [LoadAndPrepareAsync: passed → Loaded* → FileSettings](../../../../../Services/Workspace/SolutionManager.cs)
- [Configuration, Platform, TargetFramework](../../../../../Config/RoslynMcpFileSettings.cs)
- [LoadWorkspace argument contract](../../../../../Tools/WorkspaceTools.cs)

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю неоднозначность disk key и необходимость explicit absent markers. RAM matcher сравнивает только passed values, но его retained values принадлежат текущему host. Cold disk cache не должен воспроизводить их из выбранного envelope.

Не принимаю предлагаемый fix в части «ключ только по raw requested, effective лишь diagnostics» как достаточный для всех manager входов. До MSBuild evaluation уже вычислимы merged host globals: normalized passed override, Loaded value и FileSettings. Эти host-effective значения не тождественны evaluated defaults проекта. Например, два запроса с omitted configuration при разных FileSettings Debug/Release имеют одинаковый raw requested tuple, но разные ordinary globals. Аналогично переход к другому workspace в живом PID наследует Loaded properties.

В finding верно сказано, что final evaluated defaults нельзя узнать из старого envelope; неверно распространять это на все effective values. Спека использовала одно слово для двух уровней, что и нужно исправить. Requested-only lookup может быть locator, но admission обязан сравнить независимо resolved loader context.

## Position rationale

ACCEPT WITH MODIFICATION: defect реальный, однако raw requested equality само по себе оставляет config/inherited-state collision.

## Proposed response

Suggested change: разделить raw requested/absence, normalized merged globals перед обычным open и evaluated project properties. Cold lookup/admission сравнивает canonical path и независимо resolved pre-evaluation globals с явным отсутствием; source defaults покрываются input/environment validation. Не применять RAM wildcard matcher к disk envelope. Добавить cases omitted vs explicit Release, different FileSettings, inherited Loaded properties и normalized Any CPU. Сохранённые evaluated values — audit data, не oracle текущего lookup.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-grok/F-005 — связь для рассмотрения, не объединённый dispute.
- review-astra/F-002 — связь для рассмотрения, не объединённый dispute.
- review-ds/F-005 — связь для рассмотрения, не объединённый dispute.

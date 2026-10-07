# review-astra/F-003 — Неизменность metadata reference inventory нельзя вывести из неизменности путей

Дата: **2026-10-07**. Source: [review-astra/F-003](../../review-astra/findings/F-003.md).
Проверяемый пакет: `67564de6c48e6d2eb38c4542fd2ef714910da0b0` и текущий код.

## Author position

ACCEPT

## Finding summary

Изменённая DLL по прежнему пути может перестать удовлетворять MSBuild resolution constraints; старый evaluated inventory не доказывает возможность простого metadata refresh.

## Relevant requirements / constraints

- E2 обещает content reuse с результатом, согласованным со fresh ordinary load.
- Изменение evaluated reference inventory требует graph fallback; full hash или sample stamp служит обнаружению мутации, не доказательству сохранения resolution.
- В E1 любое обнаруженное binary change уже даёт whole-request miss; finding относится к расширению E2.

## Evidence checked

- [E2: Правила dirty](../../epoch-2-content-refresh/spec.md)
- [task-00-startup-diff-roles-hi.md](../../epoch-2-content-refresh/task-00-startup-diff-roles-hi.md)
- [task-03-fresh-metadata-references-med.md](../../epoch-2-content-refresh/task-03-fresh-metadata-references-med.md)
- [Microsoft Learn: ResolveAssemblyReference / SpecificVersion](https://learn.microsoft.com/en-us/visualstudio/msbuild/resolveassemblyreference-task?view=visualstudio), прочитано 2026-10-07.
- Локальный SDK 10.0.300: Microsoft.Common.CurrentVersion.targets, раздел ResolveAssemblyReference и условное `_FindDependencies` (2405–2507).

Исходные файлы читались; динамический failure scenario/benchmark этой защитой
не выполнялся. Current code — evidence доступных механизмов, а не новый нормативный scope.

## Author analysis

Принимаю. DTO знает уже разрешённый path и reference properties, но не обязательно restrictions исходного Reference item. Если A.dll перестала удовлетворять SpecificVersion, Roslyn reference к новым bytes не воспроизводит fresh MSBuild resolution. Relevant condition не выводится из неизменности csproj плюс существования HintPath.

По локальным SDK targets подтверждён реальный RAR путь; dependency traversal в design-time условный, поэтому я не расширяю finding до обязательного обхода любых transitive dependencies. Достаточен exact-version primary reference scenario. На выбранном будущем control он не воспроизведён; confidence ограничен профилем, но contract gap уже присутствует.

Согласованные пользователем быстрые binary stamps остаются допустимыми. Они отвечают на вопрос, замечена ли мутация, а не разрешают обход resolution при замеченной мутации. Не предлагаю вместо решения хешировать гигабайты полностью.

## Position rationale

ACCEPT: предпосылка unchanged evaluated inventory должна иметь собственный observable support predicate; именно его сейчас нет.

## Proposed response

Suggested change: до явного подтверждения resolution-preserving subset изменённый metadata binary ведёт к graph/ordinary fallback. Если нужен положительный refresh без DTB, описать достаточные ограничения и evidence сохранения relevant resolution, а не только одинаковую assembly identity. Добавить exact-version replacement negative control и соответствующий fresh ordinary comparison. Более широкий bounded resolution verification — вариант, не требование этой защиты.

Предложения не применены к specification и не являются нормативным change set.

## Related findings

- review-grok/F-004 — связь для рассмотрения, не объединённый dispute.

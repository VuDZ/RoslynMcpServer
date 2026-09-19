ID: E0-01
Severity: Medium
Category: Contract

Target:
[stage-0-hygiene.md](../proposal-v1/stage-0-hygiene.md), «Работы», пункт 3, строки 27–33.

Claim:
Windows CI test gate использует SDK из `global.json`.

Evidence:
В текущем дереве `global.json` отсутствует: `git ls-files '*global.json'`
не возвращает файлов. [ARCHITECTURE.md](../../../ARCHITECTURE.md), строка 167,
прямо фиксирует отсутствие SDK pin у этого репозитория. Сервер и тесты
таргетируют `net10.0`. Удалённый workflow в
`git show bdc953d:.github/workflows/build.yml` задавал `dotnet-version: "10.0.x"`,
а не чтение `global.json`. Среди работ Stage 0 нет создания pin или выбора
SDK при его отсутствии.

Failure scenario:
1. Исполнитель настраивает setup .NET на чтение репозиторного `global.json`,
   как требует Stage 0.
2. Чистый checkout не содержит этого файла; шаг выбора SDK не может
   выполнить указанный контракт, тесты не доходят до запуска.
3. Исполнитель либо получает неработающий gate, либо сам заменяет норму
   выбором установленного на runner SDK, и воспроизводимость остаётся
   зависимой от образа runner.

Suggested change:
Выбрать один воспроизводимый источник SDK: добавить `global.json` с
согласованной версией/roll-forward либо явно задать подходящую версию
в workflow. Приёмку выполнять на чистом runner, а отсутствие pin не
маскировать локально установленным SDK.

Confidence:
High

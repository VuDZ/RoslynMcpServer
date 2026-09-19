# Stage 0 — ответы

ID: E0-01
Verdict: PARTIALLY ACCEPT

Критика частично верна: Stage 0 требует SDK из `global.json`, которого в этом репозитории нет.

Требования:
Заказчик Stage 0: воспроизводимый Windows CI test gate (M-4 в [review-mapping.md](../proposal-v1/review-mapping.md)). Не требовал SDK pin этого репо.
Shipped: [ARCHITECTURE.md](../../../ARCHITECTURE.md) «Deployment and compatibility» — у репозитория нет `global.json`; выбор SDK следует установленному окружению; pin — норма для *consumer* репозиториев. TFM `net10.0`. Откатанный `bdc953d:.github/workflows/build.yml` задавал `dotnet-version: "10.0.x"`, не чтение pin-файла. `git ls-files '*global.json'` пуст (подтверждено).

Что менять:
[stage-0-hygiene.md](../proposal-v1/stage-0-hygiene.md) «Работы» п.3: убрать «SDK из `global.json`». MUST: явный источник SDK **в workflow** (`dotnet-version: "10.0.x"`, согласованный с TFM и откатанным GHA). Приёмка на чистом runner.

Что не менять:
Не делать создание `global.json` работой Stage 0. Это отдельное продуктовое решение (сейчас ARCHITECTURE сознательно без pin). Воспроизводимость gate закрывается pin в workflow, не маскировкой локальным SDK.

Последствия:
CI workflow; docs Stage 0. Packaging, catalog, tool schema — нет. Тесты: job должен стартовать setup-dotnet без отсутствующего файла.

Шире finding:
нет.

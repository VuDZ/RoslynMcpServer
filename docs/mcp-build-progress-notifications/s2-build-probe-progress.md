# S2 — progress на `run_dotnet_build`

Статус: **не выполнено**. Зависимость: [S1](s1-cli-runner-progress-seam.md).
Результат шага: живой build probe репортит границы шагов и периодический
heartbeat, пока идёт `dotnet build`.

## Основание

Первая поставка — только build. `DotNetBuildProbe` уже эскалирует
`build -v:minimal` → restore → `build -v:normal`. Агенту полезно видеть
текущий шаг, а не молчание на 5–15 мин большого `.sln`.

Targets: `DotNetBuildProbe`, `BuildTools` / `run_dotnet_build`.

## Работа

Передать reporter из tool method в probe. Репорт на смене шага и, если
шаг долгий, heartbeat из runner. Итог тула (diagnostics, SDK mismatch,
effective exit) не менять.

Не обещать в `[Description]`, что хост не оборвёт вызов. Catalog не
раздувать: Description трогать только если без этого агент начнёт
путать progress с результатом тула.

## Приёмка

- `run_dotnet_build` этого репо по-прежнему парсит ошибки и
  `MCP_MSBUILD_SDK_MISMATCH` как сейчас.
- На многошаговом probe в логе/тестовом sink видны отдельные шаги.
- Хост без progress token: успешный build, как до S2.
- Не требовать зелёный Cursor ACP на 60 с — это вне шага.

## Результат

Не выполнено.

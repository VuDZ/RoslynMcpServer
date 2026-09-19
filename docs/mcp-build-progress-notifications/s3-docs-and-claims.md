# S3 — честные формулировки

Статус: **выполнено (v1.3.25)**. Зависимость: [S2](s2-build-probe-progress.md).
Результат шага: docs не обещают, что progress лечит хост-таймаут.

## Основание

README уже отделяет OpenCode `"timeout"` от `timeoutSeconds`. После S2
легко написать «больше не будет -32001» — это ложь для Cursor ACP и
для хоста, который не делает `resetTimeoutOnProgress`.

## Работа

Короткий ряд в README «Agent tools by version»: progress на
`run_dotnet_build` — UX/heartbeat. Явно: не замена host timeout, не
лечение ACP ~60 с. `AGENTS.md.sample` не раздувать, если политика
сессии не меняется.

Patch-bump только если в S1–S2 ушло в код (см. version-bump rule).

## Приёмка

- EN + RU pointer согласованы.
- Нет фразы, что `-32001` исчезает благодаря progress.
- Catalog size записан, если Description меняли; иначе «без изменения».

## Результат

Выполнено. EN `README` → «Agent tools by version» **v1.3.25**: progress на
`run_dotnet_build` — heartbeat (шаг, elapsed, previous exit), без stdout;
явно «**not** a replacement for `timeoutSeconds`» и «**not** a fix for host
`tools/call` limits (`-32001`, Cursor ACP ~60 s)». RU pointer обновлён
(диапазон до v1.3.25), RU Reference `run_dotnet_build` — одна фраза про
heartbeat без обещаний. `get_mcp_server_info`-подсказки (EN + RU) → v1.3.25.
`AGENTS.md.sample` не менялся: политика сессии та же. Catalog size записан:
без изменения (full 63 / 45 868, lite 19 / 18 282).

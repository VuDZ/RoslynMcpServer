# 12 — Гибридный полный лог (P3, не блокер)

Pri **P3**. SemVer: **patch**, когда/если делать.
Не замена п. 2 и п. 5. Разбор: [../README.md](../README.md#54-полный-диагностический-output).

## Цель

Inline excerpt (`TruncatedProcessLog`) остаётся. При truncation / unparsed
failure дополнительно bounded full report.

## Условия ship (все обязательны)

- size cap и retention/cleanup
- атомарная запись
- политика секретов в process output
- путь, полезный клиенту (host temp бесполезен удалённому MCP)

Без этого пункта **не делать**. `TempReportWriter` форка не копировать.
StdOut/StdErr бюджеты `1.3.24` не выкидывать.

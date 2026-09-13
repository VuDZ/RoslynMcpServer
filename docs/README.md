# Docs

Индекс `docs/`. Агентам: читать **канон** темы, не `_archive/` и не `archive/`, пока не нужен audit trail.

| Роль | Что это |
|---|---|
| **Reference** | Как система ведёт себя сейчас: [ARCHITECTURE.md](ARCHITECTURE.md), корневой README, tool `[Description]` |
| **Active** | План, который ещё может менять код |
| **Contract** | Спека shipped-фичи, пока факты не полностью влиты в ARCHITECTURE |
| **Record** | Закрытые эпохи и process (review / ответы / арбитраж). Не норма |

## Active

| Тема | Канон | Статус |
|---|---|---|
| Workspace load cache | [workspace-load-cache/](workspace-load-cache/README.md) | post-arbitration spec, не в runtime |
| MCP build progress | [mcp-build-progress-notifications/](mcp-build-progress-notifications/README.md) | план, не начат |

## Contract

| Тема | Канон | Статус |
|---|---|---|
| Analyzer shadow copy | [analyzer-shadow-copy-improvements/v2/](analyzer-shadow-copy-improvements/v2/README.md), [v3/](analyzer-shadow-copy-improvements/v3/README.md) | shipped; U-ARB решения всё ещё контракт |

## Record

Не тащить в контекст по умолчанию.

| Где | Что лежит |
|---|---|
| [archive/](archive/README.md) | Закрытые серии без операторской ценности |
| `*/_archive/` | Review, ответы, арбитраж, superseded drafts той же темы |

## Правила

- После арбитража норма — корень темы (или `v2/`/`v3/`, если это текущий контракт). Process сразу в `_archive/`.
- После ship факты копируются в ARCHITECTURE / product README. Design-пакет помечается shipped и не правится «под факт».
- Accepted/rejected review не удалять. Протухший how-to/ложный reference — удалять или tombstone, не плодить второй канон.
- Старт работ по спеке: если рядом с каноном лежат `review/` / `review-response/` / `arbitration/` или pre-arb копии — предложить перенос в `_archive/`.
- Все эпохи приняты и UNRESOLVED/U-ARB закрыты: напомнить убрать тему в `docs/archive/`, если она уже не контракт. Правило для агента: `.cursor/rules/roslyn-mcp-docs-lifecycle.mdc`.

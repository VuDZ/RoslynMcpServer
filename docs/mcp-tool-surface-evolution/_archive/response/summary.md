# Summary: response to review MCP tool surface evolution

Каталог: [`README.md`](README.md). Спека и `review/` не менялись.

## Accepted

| ID | Sev | Суть |
| --- | --- | --- |
| E2-01 | Blocker | `SymbolKey` internal; запрет собственного handle делает Stage 2 нереализуемым на 5.9.0 |
| E2-02 | High | Optional `symbolId` при прежнем `required` не даёт ID-only вызовов из примеров стадии |
| E2-03 | High | Ключ без project/compilation не отличает два проекта с одной assembly identity; `FindDocumentAsync` берёт первый путь |
| E2-04 | High | Local-function key по индексу после вставки соседней `L()` silent retarget; disk sync это проявляет |

## Partially accepted

| ID | Принято | Не принято |
| --- | --- | --- |
| E0-01 | Stage 0 ссылается на несуществующий `global.json`; нужен явный источник SDK и приёмка на чистом runner | Обязательное добавление `global.json` в Stage 0. Достаточно `dotnet-version: "10.0.x"` в workflow (как откатанный GHA и TFM). Pin репо — отдельное решение, сейчас ARCHITECTURE без `global.json` |

## Rejected

Нет.

## Needs clarification

Нет. Развилки закрыты вердиктами (workflow SDK; session-scoped opaque handle; режимы required; project в identity; fail-closed для locals).

## Новые архитектурные риски

### NEW-D-01

Incoming §5–§6 говорит «stable» `symbolId`. Session-scoped store (ответ на E2-01) стабилен только внутри PID/load-сессии: не переживает `stop_mcp_server` / reload MCP / другой процесс. Это слабее «вставил строку в другой сессии». Спека Stage 2 уже почти это сказала для `reset_workspace`; нужно назвать lifetime явно, чтобы агент не кешировал id между сессиями.

### NEW-D-02

[`FindDocumentAsync`](../../../../Services/SolutionManager.cs) `FirstOrDefault` по пути — текущее поведение всех filePath semantic tools, не только будущего location-режима. Linked file / один путь в двух проектах уже может выбрать не тот Document. Stage 2 не должен притвориться, что дыра только у `symbolId`; либо чинить helper, либо ограничить гарантию location-режима тем же first-hit, что сейчас.

## Изменения в спеке, если принять вердикты

Без кода; следующая ревизия документов:

- [`stage-0-hygiene.md`](../proposal-v1/stage-0-hygiene.md): п.3 — SDK из workflow `10.0.x`, не из `global.json`; приёмка на чистом runner.
- [`stage-2-symbol-identity.md`](../proposal-v1/stage-2-symbol-identity.md):
  - снять MUST `SymbolKey` и запрет handle;
  - opaque session handle + `ProjectId`;
  - совместимость = старые вызовы валидны; addressing `required` ослабляется; режимы ID / location / legacy; конфликт селекторов → ошибка;
  - durable id после sync только для именованных деклараций + checksum; locals fail-closed;
  - тесты: same `AssemblyName`, linked document, вставка/перестановка local `L()`, ID-only schema.
- [`review-mapping.md`](../proposal-v1/review-mapping.md) R-§6: не наследовать «использовать SymbolKey» как норму.
- [`README.md`](../proposal-v1/README.md) серии: Stage 2 нельзя реализовывать, пока ревизия identity не в каноне (ревью уже так сказало).

Stage 1 (перенос body/rename) и отказ от сжатия `full` в 1.x — без изменений из этого review.

## На арбитраж

Нет несогласия с critic: дыры приняты.

Продуктовые развилки (требования допускают оба, автор выбрал первое; спор только если заказчик хочет второе):

1. E0-01: pin SDK только в GHA vs ещё и `global.json` в этом репо.
2. E2-01: public session-scoped handle vs явный internal adapter `SymbolKey` + envelope. Второе конфликтует с public-API-only прецедентом analyzer loader и само не закрывает E2-03/E2-04.

# Mapping: `roslyn_mcp_tool_review.md`

Incoming: [`roslyn_mcp_tool_review.md`](_archive/roslyn_mcp_tool_review.md)
(2026-09, полная критика tool surface). Это **не** канон. Канон серии —
[README.md](README.md) и файлы стадий (proposal v2). V1 mapping:
[_archive/proposal-v1/review-mapping.md](_archive/proposal-v1/review-mapping.md).

Вердикты входящего review:

- **ACCEPT** — берём в 1.x как написано (с учётом стадии).
- **ACCEPT WITH MODIFICATION** — идея верная, форма/срок другие.
- **DEFER** — в Stage 3 (v2 spec), не в 1.x код.
- **REJECT (1.x)** — не делаем в текущем major; можно пересмотреть в v2.
- **FACT** — поправка к фактам ревью, не к рекомендации.

Нумерация `R-§N` = секция полной статьи. Отдельный блок **M-*** — замечания
про зрелость из более короткого текста (packaging / CI / drift): в полном
файле их **нет**.

Арбитраж findings E0-01 / E2-01…E2-04: [TRACEABILITY-v2.md](TRACEABILITY-v2.md).

---

## Provenance: два входящих текста

| Источник | О чём | Куда легло |
| --- | --- | --- |
| Короткий «зрелость» (чат) | `PackageId=SampleMcpServer`, `PackageVersion=0.1.0-beta`, drift 54/56/63, GHA без `dotnet test` | [Stage 0](stage-0-hygiene.md) |
| Полный `roslyn_mcp_tool_review.md` | Tool surface, symbol-oriented API, группировка, v2 JSON | Stage 1–3 + этот файл |

Полный review **сильнее и аккуратнее** короткого: он явно говорит не резать
счётчик ради счётчика (§24), оставить три test-тула (§16), держать ILSpy
как optional quality (§15). Короткий текст звучал ближе к «63 → 20».
Норма серии следует **полному** файлу там, где они расходятся, кроме мест
с фактическими ошибками (ниже).

---

## Фактические поправки (до вердиктов)

**FACT-1 — состав `core` / `lite`.** Ревьюер в §1 оценивает 19 core-тулов
как будто там есть `get_method_body` и нет `get_code_skeleton`. Live catalog
(v1.3.24): `get_code_skeleton` **в** `core`; `get_method_body` **в** `files`.
Пара skeleton+body, которую §3 называет лучшей идеей проекта, в `lite`
разорвана. Это главный catalog-баг с точки зрения агента, не «ревью ошиблось
в оценке ценности».

**FACT-2 — `get_code_skeleton` не в `files`.** §14 включает его в группу
filesystem. Он в `core` (disk parse, без workspace). Рекомендация «keep
skeleton» уже выполнена членством.

**FACT-3 — CI.** Короткий текст описывает «текущий» GHA restore/build/publish
без test. Live tree: `.github/workflows` **нет** (добавлен `bdc953d`, откачен
`b9f2e02`). Диагноз «нет test gate» верный; формулировка «текущий workflow»
нет. В полном review CI/packaging нет вовсе.

**FACT-4 — 54 / 56.** Канон счётчика — 63. `ExpectedMinTools = 54` живёт в
[`publish-and-verify.ps1`](../../publish-and-verify.ps1). `56` — archive
compact-tools, не текущий README EN.

**FACT-5 — внутреннее напряжение полного review.** §20 кладёт в «компактный
default» (~22) ещё `get_code_fixes` / `apply_code_fix` /
`update_method_body` / `implement_interface` **и** три ILSpy-тула. §25 те же
code fixes и decompile держит **optional**. Серия разрешает так: 1.x `lite`
= нынешний `core` + body + rename; diagnostics/code-fix writes и ILSpy
остаются группами; v2 spec выбирает default заново.

---

## M — зрелость (нет в полном файле)

| ID | Claim | Вердикт | Стадия |
| --- | --- | --- | --- |
| M-1 | `PackageId=SampleMcpServer`, шаблонный `Description` | ACCEPT | 0 |
| M-2 | `PackageVersion=0.1.0-beta` надо поднять вместе с assembly | REJECT (1.x) | не bumpать, пока нет NuGet publish |
| M-3 | Drift 54/56 vs 63 | ACCEPT | 0 |
| M-4 | CI без `dotnet test` | ACCEPT (с FACT-3) | 0 |

---

## R — секции полного review

| ID | Тема | Вердикт | Куда |
| --- | --- | --- | --- |
| R-§1 | `lite` уже правильное направление; оставить маленький core | ACCEPT WITH MODIFICATION | Stage 1. Default **процесса** остаётся `full` (compat). `lite` — рекомендуемый agent default, не смена `ROSLYN_MCP_TOOL_PROFILE` |
| R-§2 | Слить `find_symbol_references` и `find_usages` в `find_references` | ACCEPT WITH MODIFICATION | Stage 2: ID-only / location / legacy на `find_usages`; ID-only на `find_symbol_references`. Имя не удалять в 1.x. Rename → Stage 3 |
| R-§3 | Skeleton + method body — центральная идея; обобщить до `get_symbol_source` / `get_symbol_outline` | ACCEPT | Stage 1: body в `core`. Stage 2: `get_symbol_source`. Outline: сначала существующий `get_class_skeleton`; generic `get_symbol_outline(symbolId)` — Stage 2 если дёшево, иначе Stage 3 |
| R-§4 | Semantic navigation — причина ставить MCP | ACCEPT | уже `core`; не размывать grep |
| R-§5 | `get_symbol_info` + `symbolId`; доп. поля (generics, XML, origin) | ACCEPT WITH MODIFICATION | Stage 2: минимальный JSON + session-scoped `symbolId`. Расширенные поля — не блокер первой поставки |
| R-§6 | Symbol-oriented API / `resolve_symbol` → handle | ACCEPT WITH MODIFICATION | Stage 2: непрозрачный session-scoped `symbolId` на публичных API Roslyn 5.9.0; не `SymbolKey` и не internal adapter. Отдельный `resolve_symbol` не обязателен, если `get_symbol_info` резолвит query/location/`symbolId`. Чистое имя `resolve_symbol` — Stage 3 |
| R-§7 | `rename_symbol` → core; `symbolId` + `previewOnly` | ACCEPT | Stage 1 membership; Stage 2 `symbolId` |
| R-§8 | Diagnostics → CodeActions — сила; `get_diagnostics(scope)` | ACCEPT WITH MODIFICATION | Workflow уже есть (`get_diagnostics_for_file` в core, fixes в `editing`). Scope file\|project\|solution — Stage 3. Docs/AGENTS усилить в Stage 1 без смены схемы. `apply_code_fix` в lite не тащить (write) |
| R-§9 | `editing` слишком гранулярный | ACCEPT как диагноз; REJECT (1.x) как слияние имён | Stage 3 |
| R-§10 | Оставить `update_method_body` | ACCEPT | остаётся; в `editing` до v2. Не core в Stage 1 (write, размер lite) |
| R-§11 | Оставить `implement_interface` | ACCEPT | как §10 |
| R-§12 | `add_using` / `organize_usings` вторичны | ACCEPT | уже не lite; не продвигать |
| R-§13 | Слить add field/property/method в `add_member` | DEFER | Stage 3. 1.x не вводит dispatcher и не удаляет три имени |
| R-§14 | Filesystem дублирует host; прятать от IDE-профиля | ACCEPT WITH MODIFICATION | `files` уже optional. `get_code_skeleton` остаётся в core (FACT-2). Не удалять `files` из `full`. Headless — валидный клиент |
| R-§15 | ILSpy-группа сильная; выровнять имена с source API | ACCEPT | группа остаётся optional. Align `get_decompiled_*` / общий shape с source — Stage 3. Не тащить ILSpy в lite Stage 1 |
| R-§16 | Три test-тула оставить раздельными | ACCEPT | не сливать. Расходится с «просто меньше tools» |
| R-§17 | NuGet optional; writes менее уникальны | ACCEPT | без смены групп |
| R-§18 | Прятать `execute_dotnet_command` | ACCEPT | уже `runtime`, не lite. Docs: fallback only |
| R-§19 | Scratchpad не Roslyn | ACCEPT WITH MODIFICATION | Stage 0: docs. Удаление имени — Stage 3, не 1.x |
| R-§20 | Компактный default ~20–22 тула | ACCEPT WITH MODIFICATION | Цель агентского default = `lite`, не сжатый `full`. Состав §20 шире нашего Stage 1 (см. FACT-5) |
| R-§21 | v2 JSON-сигнатуры | DEFER | [Stage 3](stage-3-v2-spec.md) — кандидат, не принятая норма |
| R-§22 | Стабильные id ещё для diagnostic/fix/test | DEFER | Stage 3; 1.x только session-scoped `symbolId` |
| R-§23 | Intent-oriented имена лучше implementation-oriented | ACCEPT | критерий для Stage 3; 1.x не переименовывает |
| R-§24 | Не минимизировать count ради count | ACCEPT | совпадает с нормой серии; полный файл важнее короткого |
| R-§25 | Перегруппировка core/editing/decompile/files-as-headless | ACCEPT WITH MODIFICATION | Stage 1 двигает только body+rename. Полная перекладка групп — Stage 3 |
| R-§26 | Product direction: reliability, tokens, safe edits, identity, measurable outcomes | ACCEPT | рамка серии; метрики (§26.6) не обещать в 1.x без измерения. Слово «stable» в §5–§6 = повторное использование в оговорённом lifetime, не переживание правок документа |

---

## Что полный review добавляет относительно короткого текста

Короткий чат не содержал или почти не содержал:

- `get_symbol_outline` рядом с `get_symbol_source` (§3, §21);
- отдельный `resolve_symbol` и ambiguous candidates (§6, §21);
- diagnostics **scope** file\|project\|solution (§8);
- явное «keep» для `update_method_body` и `implement_interface` (§10–11);
- слияние add_* в `add_member` + `remove_symbol` (§13, §21);
- ILSpy naming alignment и общий shape source/decompiled (§15, §21);
- защиту трёх test-тулов как **хорошую** дубликацию (§16, §24);
- `execute_dotnet_command` как advanced fallback (§18);
- `diagnosticId` / `codeFixId` / `testId` (§22);
- критерий intent vs implementation (§23);
- `files` как compatibility/headless profile, не «удалить» (§25);
- measurable outcomes (§26).

Серия принимает это как **вход в Stage 3**, кроме того, что уже ложится в
Stage 1–2 без breaking.

## Чего в полном review нет (и зачем Stage 0 всё равно нужен)

Packaging template, NuGet `PackageId`, documentation drift счётчиков,
отсутствие CI test gate. Это про зрелость поставки, не про API. Полный
файл их не отменяет: без test gate symbol-API будет так же «быстро выросшим».

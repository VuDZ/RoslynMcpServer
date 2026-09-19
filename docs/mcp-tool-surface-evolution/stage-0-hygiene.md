# Stage 0 — гигиена и CI

Статус: **post-arbitration spec; реализация не начата**. Schema MCP-тулов
не меняется.

Источник: короткий текст про зрелость (в полном
[`roslyn_mcp_tool_review.md`](../../roslyn_mcp_tool_review.md) этого блока
нет). Mapping: M-1…M-4 в [review-mapping.md](review-mapping.md).
Арбитраж: E0-01 — ACCEPT WITH MODIFICATION.

## Цель

Поставка выглядит как продукт, а не MCP SDK sample. Рискованные части
(parsers, path/SDK, VSTest, write boundary, catalog) проходят через CI, а
не только через локальный dogfood.

## Работы

1. **Packaging** в `RoslynMcpServer.csproj`:
   - `PackageId`: не `SampleMcpServer` (ожидаемо `RoslynMcpServer`, если нет
     другого публичного id).
   - `Description`: одно предложение про C# / Roslyn MCP, не шаблон SDK.
   - `PackageVersion=0.1.0-beta` **не** менять (нет NuGet publish).
2. **Счётчики:**
   - `publish-and-verify.ps1` `ExpectedMinTools` → актуальный full count
     (на момент записи **63**).
   - подтянуть drift в README (RU byte sizes / expect-version
     `get_mcp_server_info`, если ещё отстаёт от csproj).
3. **CI test gate** (Windows; SDK **не** из `global.json`):
   - явный setup .NET SDK семейства `10.0.x` в workflow (`dotnet-version:
     "10.0.x"`). Это выбор семейства, не точная patch-версия и не pin
     репозитория. Файл `global.json` в этой стадии **не** создавать.
   - в логе job зафиксировать фактически выбранный SDK (`dotnet --info`
     или `dotnet --version`).
   - перед restore выяснить причину revert `b9f2e02` (не копировать
     publish-only workflow вслепую). Результат этой проверки не выводить
     из одного лишь факта отката.
   - required job: `dotnet test --filter Category!=AnalyzerLifecycle`;
   - отдельный job `Category=AnalyzerLifecycle`, длинный timeout; skip
     остаётся skip, не silent pass;
   - оба test jobs запускать на **чистом** Windows runner после успешного
     setup SDK;
   - publish/artifact — после зелёных тестов, optional.
4. **Scratchpad (R-§19):** одна строка в README Reference — не Roslyn
   concern; prefer host memory; удаление имени не в этой стадии. Код тула
   не трогать.

## Не входит

- Смена состава `core` / `lite` (Stage 1).
- Новые tools (Stage 2).
- Удаление `manage_agent_scratchpad`.
- Публикация пакета на nuget.org.
- Точный pin SDK для всего репозитория и политика patch-обновлений
  ([U-ARB-01](UNRESOLVED-v2.md#u-arb-01--pin-net-sdk-репозитория)).

## Приёмка

- Catalog/tool schema: без изменений (те же 63 имени).
- Чистый Windows runner: успешный setup SDK `10.0.x`, в логе версия SDK.
- Оба test jobs запущены с прежними фильтрами и таймаутом.
  `dotnet test` без AnalyzerLifecycle зелёный в CI.
- Script/docs не требуют 54/56 tools.
- Packaging metadata больше не говорит `SampleMcpServer` / SDK sample
  description.
- В спеке и workflow нет ссылки на отсутствующий `global.json`.

## Версия

Patch, если меняется csproj, который агенты сверяют с
`get_mcp_server_info`. Только YAML workflow — без bump.

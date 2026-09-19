# Stage 0 — гигиена и CI

Статус: **план; реализация не начата**. Schema MCP-тулов не меняется.

Источник: короткий текст про зрелость (в полном
[`roslyn_mcp_tool_review.md`](../../../../roslyn_mcp_tool_review.md) этого блока
нет). Mapping: M-1…M-4 в [review-mapping.md](review-mapping.md).

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
3. **CI test gate** (Windows, SDK из `global.json`):
   - перед restore выяснить причину revert `b9f2e02` (не копировать
     publish-only workflow вслепую);
   - required job: `dotnet test --filter Category!=AnalyzerLifecycle`;
   - отдельный job `Category=AnalyzerLifecycle`, длинный timeout; skip
     остаётся skip, не silent pass;
   - publish/artifact — после зелёных тестов, optional.
4. **Scratchpad (R-§19):** одна строка в README Reference — не Roslyn
   concern; prefer host memory; удаление имени не в этой стадии. Код тула
   не трогать.

## Не входит

- Смена состава `core` / `lite` (Stage 1).
- Новые tools (Stage 2).
- Удаление `manage_agent_scratchpad`.
- Публикация пакета на nuget.org.

## Приёмка

- Catalog/tool schema: без изменений (те же 63 имени).
- `dotnet test` без AnalyzerLifecycle зелёный в CI.
- Script/docs не требуют 54/56 tools.
- Packaging metadata больше не говорит `SampleMcpServer` / SDK sample
  description.

## Версия

Patch, если меняется csproj, который агенты сверяют с
`get_mcp_server_info`. Только YAML workflow — без bump.

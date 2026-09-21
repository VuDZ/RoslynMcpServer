# 1 — `DOTNET_CLI_UI_LANGUAGE=en-US`

Pri **P1**. SemVer: **patch**. Зависимостей нет.
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#1-dotnet_cli_ui_languageen-us).

## Цель

Все дочерние `dotnet` (build/test/run/audit) печатают английский UI, парсеры
не зависят от ru-RU (`Пройдено!`).

## Файлы

- `Services/DotNetCliRunner.cs` — `CreateProcessStartInfo` (~327)
- тест рядом с существующими CLI-тестами (`DotNetCliRunner*` /
  `DotNetTestArgumentsTests`)

## Правка

В той же фабрике, где уже стоит `MSBUILDDISABLENODEREUSE=1`:

```csharp
psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
```

Показать значение в run metadata. Не ставить в отдельных tool-методах.

## Тесты

`ProcessStartInfo.Environment` содержит `DOTNET_CLI_UI_LANGUAGE=en-US` **и**
`MSBUILDDISABLENODEREUSE=1`.

## Acceptance

На ru-RU `run_dotnet_test` даёт разобранный Total/Passed/Failed, не `partial`.

## Не копировать

Файл `DotNetCliRunner` форка целиком (у них нет node-reuse disable, другой
timeout/progress).

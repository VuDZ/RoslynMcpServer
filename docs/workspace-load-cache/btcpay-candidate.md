# BTCPayServer — candidate workload

Статус: **baseline снят; текущий `sdk-project-v1` не допускает request**.
U-ARB-03 остаётся открытым. OrchardCore/Roslyn не обязательны: владелец
2026-10-03 подтвердил, что прежние корпуса были выбраны случайно.

## Scope и воспроизведение

BTCPayServer `v2.4.3`, commit
`f78d6c20f0aa184f61eb4533bcf1dee20e366579`. Загружается
`BTCPayServer/BTCPayServer.csproj` со всеми транзитивными ProjectReference:
Server, Abstractions, Client, Common, Data и Rating. Тесты и PluginPacker
не входят в этот scope. Файлы корпуса не изменялись; checkout чистый.

Протокол — [baseline benchmark](baseline-benchmark.md), token
`baseline-benchmark-4`. Команда:

```powershell
python docs/workspace-load-cache/baseline_bench.py --corpus btcpay-web
```

Клон — `E:\Devel\roslyn-mcp-bench\clones\btcpay-web`, вне серверного
репозитория. SDK из PATH: `10.0.300`; у корпуса нет `global.json`.
Compare key: `f78d6c20f0aa184f61eb4533bcf1dee20e366579|10.0.300|`.
`targetFramework`, configuration/platform и analyzer shadow-copy opt-in
не передавались. Client использует `netstandard2.1`, остальные пять —
`net10.0`; проектов с `TargetFrameworks` в клоне не найдено.

## Измеренный baseline

Прогон 2026-10-03, `VuDZ-PC`, Windows 11, 16 логических процессоров,
95904 MB RAM; опубликованный MCP server file version `1.5.4.0`.
С этим хостом и этой версией нельзя напрямую сравнивать выбранный
офисный baseline `bondarev` / `1.4.14.0`.

Отчёт — [report.md](../../artifacts/workspace-load-baseline/btcpay-v2.4.3-v4-external/report.md).
Все 10 warm-попыток — success для load и первого semantic
`find_symbol_definition(InvoiceEntity)`:

- `load_workspace`: median **1.94 s**, p95 **1.96 s**;
- `find_symbol_definition`: median **5.39 s**, p95 **5.47 s**;
- `useful (load + semantic)`: median **7.32 s**, p95 **7.42 s**.

Числа скопированы из отчёта, warm median не пересчитывалась. p95 при n=10
совпадает с max. Restore, post-restore и profiled-warm в эти числа не входят.
Пик working set warm-попыток в таблице отчёта — 596–619 MB. OS file cache
не сбрасывался; эти попытки не являются cold.

Trace `dotnet-sampled-thread-time` успешно записан и обработан `topN`:
[trace summary](../../artifacts/workspace-load-baseline/btcpay-v2.4.3-v4-external/btcpay-web/btcpay-web-profiled-warm-0-trace.txt).
Это managed stack samples, включая ожидания, не kernel CPU profile.
Доминирование ожиданий в summary не доказывает CPU bottleneck.

Первый технический прогон `btcpay-v2.4.3-v4` не используется: клон был
ошибочно помещён под серверный репозиторий, наследовал его
`Directory.Build.props` и попал в Compile glob сервера. Повтор выше выполнен
после переноса в штатный внешний каталог. Скрипт теперь отвергает
`--bench-root` внутри серверного репозитория.

## Admission evidence и препятствия

После замеров отдельно выполнена MSBuild evaluation: `-getItem:Compile`,
`-getProperty:TargetFramework`, `DesignTimeBuild=true`,
`SkipCompilerExecution=true`, без build и test корпуса. Это не DTB и не
проверка semantic equivalence. Inventory —
[admission-inventory.json](../../artifacts/workspace-load-baseline/btcpay-v2.4.3-v4-external/admission-inventory.json).

Evaluation вернула 1258 Compile items: Server 856, Abstractions 54,
Client 152, Common 14, Data 148, Rating 34. Это evaluation membership,
не полный generated-document set. В Server один внешний Compile item:
`jetbrains.annotations.sources/2026.2.0/contentFiles/cs/any/JetBrains.Annotations/Annotations.Internal.cs`
в NuGet package store, вне каталога проекта и SDK.

Generated `*.nuget.g.props/targets` содержат пакетные импорты, в том числе
Serilog, Fido2, Mono.TextTemplating и Microsoft.CodeAnalysis.Analyzers.
Preprocess основного проекта подтвердил импорты Serilog, Fido2,
Microsoft.AspNetCore.App.Internal.Assets и Microsoft.CodeAnalysis.Analyzers.
Сами импортированные файлы прочитаны; список Target declarations —
[package-targets.json](../../artifacts/workspace-load-baseline/btcpay-v2.4.3-v4-external/package-targets.json).

- Serilog.targets содержит ItemGroup, без Target: расширение файла не
  доказывает наличие пользовательского target.
- Fido2 содержит `ErrorForFrameworksMissingNETStandardAPIs` с условием
  `net461`/`net462`. Условие не соответствует этому root TFM, но профиль
  сейчас отвергает сам Target вне SDK.
- Mono.TextTemplating содержит InitialTargets с проверкой совместимости TFM.
- Microsoft.CodeAnalysis.Analyzers содержит targets перед `CoreCompile`,
  добавляющие global analyzer config и AdditionalFiles. Общая политика
  «NuGet targets не влияют на семантику» здесь не обоснована.

Пользовательский `CopyAditionalFiles` в Tests находится вне выбранного
графа; он не является причиной отказа этого request. SourceLink в Client
и Abstractions условен для Release; этот результат не доказывает admission
Release-конфигурации.

По текущему `sdk-project-v1` внешний source не покрыт разрешённым регионом,
а Target вне SDK даёт `unknown`. Поэтому нужен ordinary load всего request.
Runtime detector ещё не реализован: это вывод по спецификации и прочитанным
файлам, а не измеренный runtime miss.

## Что ещё решить для U-ARB-03

Корпус подходит для воспроизводимого эксперимента, но размер 6 проектов /
1258 Compile items не утверждён как репрезентативная большая нагрузка.
До activation отдельно нужны достаточность размера, workload mix,
численные median/p95, miss-overhead, hit-rate и resource budgets.

Чтобы сохранить этот корпус как supported target, нужно отдельно доказать
расширение профиля для явного source из закреплённого NuGet package и для
конкретных пакетных imports/targets, включая analyzer configs. Простое
доверие всем пакетам или отсутствие выполнения target в одном прогоне
этого доказательства не заменяет. Эти расширения в данной работе не выбраны.
Cache hit/miss, capture и semantic equivalence пока не измерялись.

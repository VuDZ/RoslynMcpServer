# Task-02 — финальная validation и приёмка под SDK 10

Дата: **2026-11-18**, Windows x64. Статус: **accepted** координатором после [ACCEPT независимого ревью раунда 4](task-02-review-round-4.md). Scope — внутренний расчёт отпечатков типов верхнего уровня и сборки из `IlAssemblyImage`, неизменяемые снимки и сравнение changed/added/removed/assembly. Решение владельца M-09/D-09 закреплено в task-02 и регрессии. Публичный инструмент, workspace lifecycle, версия и CHANGELOG не меняются; task-03 не начата.

## Окружение и команды

Baseline: `f7e6d96`. SDK **10.0.402**, MSBuild **18.9.15**, testhost **.NET 10.0.15**. Изолированный `sdk10-host` содержит только SDK10; `DOTNET_ROOT`, `DOTNET_ROOT_X64`, `DOTNET_HOST_PATH` и первый элемент `PATH` указывают на него, `DOTNET_MULTILEVEL_LOOKUP=0`. Унаследованные MSBuild/SDK resolver overrides удалены, node reuse отключён. Это окружение получают subprocesses интеграционных тестов. Runner — PowerShell: специализированные Roslyn build/test tools не выражают необходимые переменные окружения; AGENTS.md разрешает такой runner для требуемого integration environment.

Выполнено последовательно против окончательного кода, Release, без конкурирующих сборок/тестов:

```powershell
dotnet --version
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~RoslynMcpServer.Tests.Testing.Il|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure' --logger 'trx;LogFileName=review-4-owner-green.trx' --results-directory TestResults/task02-sdk10
dotnet build RoslynMcpServer.sln -c Release --no-restore --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --filter 'Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk' --logger 'trx;LogFileName=main.trx' --logger 'console;verbosity=normal' --results-directory TestResults/task02-sdk10
./.github/scripts/Assert-NoSkippedTests.ps1 -ResultsDirectory TestResults/task02-sdk10/main-only
```

`main-only` содержит копию только окончательного `main.trx`: исторические red/mutation TRX не смешаны с финальной проверкой. Фильтр соответствует Windows main shard `.github/workflows/test-suite.yml`; исключение категории фильтром не выдаётся за skip.

## Результаты окончательного кода

- **Focused: 518/518 passed**, 0 skips: **289 новых fingerprint + 106 прежних IL + 123 SourceStructure**. Включён новый deterministic witness generated-member renumbering после ответа владельца. Логи: `TestResults/task02-sdk10/review-4-owner-green.log`, `.trx`.
- **Solution Release build: 0 errors, 3 прежних warnings** вне task-02: CS8603 `NuGetFallbackAssemblyResolver.cs:72`, CS8601 `HostSession.cs:1179`, xUnit1031 `WpfTemporaryProjectCallbackTests.cs:195`. Лог: `release-build.log`.
- **Main: 2405/2405 passed**, total=executed=passed=2405; failed/error/timeout/aborted/notExecuted=0. Все остальные TRX counters также 0. Включены SourceStructure **123/123**, fingerprint **289/289**, весь IL **395/395**. Начало `2026-11-18T15:56:07.7372776+03:00`, окончание `16:00:41.7665620+03:00`; runner duration **4.5670 min**. Логи: `main.log`, `main.trx`.
- Репозиторный `Assert-NoSkippedTests.ps1`: **All 2405 discovered tests executed and passed**.

Предыдущий положительный main **2404/2404** до добавления последней регрессии сохранён отдельно как `main-before-d01-test.log/.trx`; он не подменяет итоговый main. История TDD и review fixes, включая точные red counters и отдельные BaseType/InterfaceImpl omission-прогоны, сохранена в [отчёте реализации](../task-02-implementation-report.md). Первоначальный red не объявляется индивидуальным red для позднее добавленных тестов.

## Замер на окончательных сборках

Запуск координатора: **2026-11-18T13:01:02Z**, после финального main. Временный [harness с полным исходником](task-02-timing-harness.md) выполнен под тем же SDK10 вне репозитория. К двум основным аргументам документированной команды добавлены Release DLL lifecycle host, DeadlockWitnessHost и sample plugin. Ни одна измеряемая сборка не загружается для исполнения; сервис читает её буфер.

По одному предварительному чтению и одному прогреву на образ; затем пять измеренных `Compute`. Файловое чтение, прогрев, проверка повторяемости и форматирование исключены. Повторные assembly hash и число типов стабильны. Это локальные наблюдения без performance-порога; `total_ms` — сумма пяти вычислений одной сборки, а не сумма медиан или время всей команды.

Точный stdout `TestResults/task02-sdk10/timing-final.log` (пути сокращены до имён; все DLL из соответствующих `bin/Release/net10.0`):

```text
RoslynMcpServer.dll: bytes=1809408, types=368, warmup=1, measured=5, min_ms=297.480, median_ms=345.031, total_ms=1841.371, samples_ms=427.411,437.468,333.980,345.031,297.480
RoslynMcpServer.Tests.dll: bytes=2970112, types=368, warmup=1, measured=5, min_ms=378.370, median_ms=398.911, total_ms=2004.848, samples_ms=389.940,398.911,378.370,427.356,410.270
RoslynMcpServer.LifecycleTestHost.dll: bytes=88576, types=14, warmup=1, measured=5, min_ms=8.491, median_ms=13.158, total_ms=87.081, samples_ms=31.024,9.118,13.158,25.290,8.491
DeadlockWitnessHost.dll: bytes=9728, types=2, warmup=1, measured=5, min_ms=0.671, median_ms=0.731, total_ms=3.898, samples_ms=0.671,0.870,0.920,0.706,0.731
RoslynMcpPlugin.dll: bytes=5120, types=3, warmup=1, measured=5, min_ms=0.080, median_ms=0.087, total_ms=0.474, samples_ms=0.135,0.090,0.087,0.082,0.080
```

Предыдущий timing до последнего теста сохранён как `timing-before-d01-test.log`.

## Прослеживаемость и границы

Локальные сырые артефакты находятся в ignored `TestResults/task02-sdk10`; этот отчёт сохраняет counters, команды, результаты и их SHA256:

```text
review-4-owner-green.trx B109F712A6B6096CC75F2B0B185672B4287EAF013B2C2ECAF274F9C395EC281B
release-build.log       3724F0B6F9E2AC3398930208DC1BFD593D7E4FAAF7647E03FEBE45CD20B3D7D2
main.trx                EDA9627173A445001E2239B037F95240953E8D982B0B3A69E1B7EA4F49BC957D
timing-final.log        08668DA7A44EC9F8FC42E65C01C4DEBAB613548EF92AD9F3426CAE94606386FB
```

AnalyzerLifecycle дополнительно не запускался: новые helpers не вызываются из workspace load/prepare/publication и не меняют analyzer/shadow-copy lifecycle, host или конфигурацию тестов/сборки. Linux, RequiresPinnedSdk, CAS/native execution и большие solution не проверялись. Замер не доказывает время на больших solution.

Принятые границы: generated/anonymous renumbering и недетерминированная компиляция могут дать лишний выбор; точные имена и порядок полей сохраняются. Неоднозначное native RVA в AnyCPU и превышение лимита 4096 промежуточных enum-интерпретаций явно отклоняют весь образ, частичного снимка нет. Контракт отказа и controls проверены; потребитель/fallback остаётся задачами последующих этапов. Известные инвентари SourceStructure не ослаблены. Task/index/report статусы согласованы после этих результатов; незакрытых MUST-FIX нет.

# linux-x64/task-04 — Поиск SDK, сообщения и README для Linux

- Дата постановки: 2026-10-21.
- Рекомендуемые модели: DeepSeek 4.1 Flash, GLM 5.3; альтернатива: GPT-6.1 Sol.
- Рекомендуемый reasoning: GPT: low.
- Примерная сложность: 2/5; класс `low`.
- Риск ошибки: низкий — точечные правки и документация; поведение на
  Windows не меняется.
- Необходимые способности: `csharp`, `документация`.
- Статус: planned.
- Depends on: — (входит в первый релиз с Linux, см. [task-03](task-03-release-linux-x64-med.md)).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Код

1. `Diagnostics/MsBuildInstanceSelector.cs` — запасной поиск SDK вне Windows
   (`FindLatestCompatibleDotNetSdkDirectory`) смотрит только в
   `DOTNET_ROOT`, `/usr/local/share/dotnet` и `/usr/share/dotnet`. Добавить
   `/usr/lib/dotnet` (пакеты Ubuntu) и `~/.dotnet` (`dotnet-install.sh`).
   Основной путь через `MSBuildLocator` не меняется. Копию в `spikes/` не трогать.
2. Сообщения с `Program Files\dotnet` в `Services/Build/DotNetCliRunner.cs`
   (строки около 85, 183, 508 на 2026-10-21) сделать платформонезависимыми:
   на Windows можно оставить путь, вне Windows — `DOTNET_ROOT`/`PATH`.
3. `Services/McpServerInfoHelper.cs` — подсказка
   `dotnet publish -c Release -r win-x64` берёт RID текущего процесса
   (`RuntimeInformation.RuntimeIdentifier`), а не фиксированный `win-x64`.
4. `Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs` — словари
   `_loadedByPath` и `_loadedByIdentity` сравнивают пути через
   `OrdinalIgnoreCase` на любой ОС. Пути сравнивать по правилу остального
   кода (регистр учитывается вне Windows); для identity — оставить как есть
   или обосновать. Сейчас вреда нет: shadow-пути содержат хэши.

Для каждого пункта — тест или обоснование, почему тест не нужен.

## Документация

1. Корневой README (английская и русская части): таблица поддерживаемых
   платформ (`win-x64`, `linux-x64`; прочие RID собираются, но не
   поддерживаются); установка на Linux из `.tar.gz`; требования:
   - self-contained — `libicu` и glibc не ниже базовой версии .NET 10;
   - framework-dependent — runtime .NET 10 и `DOTNET_ROOT`, если runtime
     стоит не в стандартном месте;
   - SDK для анализируемого проекта; SDK из пакетов дистрибутива — best effort.
2. Пример конфигурации MCP-клиента с Unix-путями.
3. `install2opencode.ps1` на Linux требует `pwsh`; указать это рядом
   с описанием скрипта.
4. `docs/ARCHITECTURE.md` — строка о поддерживаемых платформах со ссылкой
   на эту тему.

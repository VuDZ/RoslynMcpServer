# Task-01: O-04 — поддержка plugins отложена

## Главное

- **Выбор владельца, 2026-10-29:** плагины пока отложить. Основной shared spike — plugins=none; Sample допустим как небольшой контроль существующего механизма, без поддержки произвольных plugins и редизайна API.
- Владелец ожидает одинаковый набор при общих/global или проектных настройках. Это разумное ожидание при одинаковых startup inputs/файлах; разный состав plugins не объявляется типичным источником проблем.
- По source сейчас plugins читаются при старте из drop-in рядом с exe, RoslynMcp.jsonc из exe/cwd и ROSLYN_MCP_PLUGINS. Переключение load_workspace не перечитывает config/plugins из каталога нового решения. Разные launch inputs/время запуска могут дать расхождение; это возможные сценарии, не наблюдённые отказы владельца.
- Совместимость одинакового plugin с несколькими клиентами — отдельный вопрос: API разрешает shared singleton/static state. Возможные session leaks не выдаются за уже найденный дефект конкретного plugin.
- На базе `ed614a2`, production 1.5.18, изменены только документы. O-03/O-04/O-05 выбраны в указанном scope; O-01/O-02 открыты, task-01 in progress, task-02/task-06 planned. Код/plugin config/activation не меняются.

Исполнитель — GPT Sol-6.1 (`gpt-6.1-sol`), Codex.

## Проверка источников

[Program:58](../../../Program.cs#L58) читает file settings, затем
[Program:96](../../../Program.cs#L96) запускает единственный plugin startup pass до
IHost.Build/Run. Поиск LoadFromDefaultLocations/LoadFromDirectories/PluginStartup.Run
в production не нашёл runtime перечитывания при load_workspace.

[RoslynMcpFileSettings:149](../../../Config/RoslynMcpFileSettings.cs#L149) использует
AppContext.BaseDirectory и process cwd; массив plugins в cwd заменяет exe array
([merge:260](../../../Config/RoslynMcpFileSettings.cs#L260)). Поэтому «настройки проекта»
в нынешней реализации означают cwd запуска, а не автоматически выбранный позднее .sln.

[PluginDiscovery:24](../../../Plugins/PluginDiscovery.cs#L24) принимает три источника:
drop-in, настроенные пути, environment paths. Первый принятый plugin id выигрывает.
Два MCP одного старта/конфигурации обычно получают одинаковый набор. Возможные
расхождения: другой exe/cwd, иное ROSLYN_MCP_PLUGINS или изменение plugin files
между startup двух процессов. Частота этих случаев здесь не измерялась.

[IRoslynMcpPlugin](../../../Plugins/IRoslynMcpPlugin.cs#L5) и
[context.Services](../../../Plugins/RoslynMcpPluginContext.cs#L26) допускают регистрацию
общих сервисов. Даже один и тот же plugin может хранить данные клиента в singleton;
это вопрос будущего audit многоклиентного host, а не различия списков plugins.

## Scope и validation

Решение записано в [design](../design.md#общая-admission-и-plugins),
[README](../README.md#o-04-plugins-deferred) и task-01/task-02. Shared attachment с
неподдерживаемым plugin отклоняется явно; ordinary plugin mode остаётся прежним.
Плагины не отключаются молча, private fresh не отменяет admission checks.

Применён research-task-authoring: наблюдение/ожидание владельца отделены от source
facts и предположений о runtime. Это запись после раунда 9 без нового независимого
ревью; counters task-01 9/14 и задач E0 16/39 не меняются. Validation: 20 Markdown,
529 local links/anchors без ошибок, 54/54 уникальных tools и 42/42 state IDs;
dependency headers/index семи задач совпадают, DAG без циклов, git diff --check PASS.
Исходные review catalogs не изменены. Build/tests/runtime не
запускаются: C#/tests/build settings не меняются.

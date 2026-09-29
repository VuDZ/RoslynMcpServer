# Response: review-astra/epoch-6-host-startup.md

## review-astra/E6-01

### Reviewer claim

Приёмка эпохи 6 вызывает `PluginStartup` в процессе теста. Она не доказывает, что `Program` передаёт `ROSLYN_MCP_PLUGINS` и что первый `tools/list` настоящего stdio-хоста содержит плагин.

### Related findings

Complements `review-astra/E3-01` (удержание файла — другая граница) и `review-ds/E6-01` (лог до `Build`).

### Problem assessment

`ACCEPT`

### Problem rationale

Тесты эпохи, как они написаны, передают каталог и строку переменной аргументами. Ни один gate не запускает entry point, не шлёт `initialize` и не читает первый `tools/list`. Ошибка «в `Program` ушла пустая строка вместо переменной» или «вызов не стоит на пути старта» остаётся зелёной. Цель эпохи — первый `tools/list` процесса. Это measurement gap, не ошибка самой идеи `PluginStartup`.

### Correction assessment

`ACCEPT`

### Correction rationale

Обязательный subprocess smoke: собранный хост, только переменная окружения на dev-выход шаблона, `initialize`, первый `tools/list`, `tools/call` шаблонного тула, stderr при битом соседе, stdout без постороннего текста. In-process тесты остаются для префикса, DI и drop-in. Smoke не заменяет их.

### Evidence

Эпоха 6, цель и список тестов. `Program.cs`, место вызова ещё не существует, но эпоха назначает его единственной связью с процессом. README серии и authoring: первый `tools/list` уже видит тулы.

### Classification

`DOCUMENTATION DEFECT`

### Severity

Medium у ревьюера. Совпадает.

### Required outcome

Нельзя закрыть эпоху 6, не увидев плагин в первом `tools/list` процесса, запущенного с `ROSLYN_MCP_PLUGINS`.

### Proposed specification change

Эпоха 6, раздел «Тесты»: добавить smoke и явно оставить in-process тесты как локализацию, не как доказательство проводки `Program`.

### Downstream consequences

Тестовый процесс stdio. Таймаут и разбор протокола. Поведение загрузчика не меняется.

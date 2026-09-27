# Response: review-ds/epoch-4-sample-plugin.md

## review-ds/E4-01

### Reviewer claim

Шаблон под корнем SDK-проекта попадает в `Compile` хоста. Исключения `samples\**` нет, эпоха 4 не разрешает править csproj, эпоха 7 запрещает менять csproj. Каталог падает на лишнем `[McpServerTool]`, повторная сборка ещё и тащит generated `.cs` из `obj` шаблона.

### Related findings

Duplicate of `review-astra/E4-01`.

### Problem assessment

`ACCEPT`

### Problem rationale

Подтверждено чтением `RoslynMcpServer.csproj`: `Compile Remove` есть только для Tests и LifecycleTestHost. Проба ревьюера `msbuild -getItem:Compile` на временном `samples/.../Probe.cs` вернула этот путь; то же для файла под `obj` и `bin` шаблона. `DiscoverAttributedTools` смотрит сборку `WorkspaceTools`. Лишний атрибут роняет статический `McpToolCatalog.All`. Дополнительный шаг CS0579 на generated-файлах — тот же глоб, не отдельная архитектура.

### Correction assessment

`ACCEPT`

### Correction rationale

Как у `review-astra/E4-01`: `Compile Remove` и `None Remove` для `samples\**` в эпохе 4. Не добавлять шаблон в каталог. Эпоха 7 не должна запрещать уже сделанное исключение. Размещение шаблона вне дерева хостового csproj тоже сработало бы; путь `samples/RoslynMcpPlugin` уже записан в authoring, поэтому исключение, а не перенос.

Независимое совпадение двух ревьюеров здесь усиливает уверенность. Доказательство — глоб SDK и текущий csproj.

### Evidence

`RoslynMcpServer.csproj`. `Hosting/McpToolCatalog.cs`. Эпохи 4 и 7. Проба ds с `Probe.cs`.

### Classification

`ARCHITECTURE DEFECT`

### Severity

Blocker у ревьюера. Совпадает.

### Required outcome

Тот же, что у `review-astra/E4-01`.

### Proposed specification change

Одна правка, `P-010`.

### Downstream consequences

Те же. Повторная сборка solution не должна видеть `AssemblyInfo.cs` шаблона в хосте.

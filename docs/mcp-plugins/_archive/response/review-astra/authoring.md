# Response: review-astra/authoring.md

## review-astra/C-01

### Reviewer claim

Обычный `dotnet build` class library не кладёт NuGet-зависимости рядом с DLL. Рецепт и эпоха 4 обещают самодостаточный выход, а приёмка шаблона частной зависимости не имеет.

### Related findings

Дополняет `review-astra/E3-01` и `review-astra/E5-03`: копия и resolver видят только то, что реально лежит в выходе.

### Problem assessment

`ACCEPT`

### Problem rationale

В рецепте нет `EnableDynamicLoading` и нет `CopyLocalLockFileAssemblies`. Для SDK class library значение по умолчанию — не копировать lock-file сборки. Проба ревьюера (Newtonsoft.Json, SDK 10.0.204, resolver → `null`) согласуется с этим правилом SDK. Фраза authoring «свои пакеты попадают туда же» текущим csproj не обеспечивается. Тест эпохи 4 проверяет только DLL шаблона и отсутствие двух сборок хоста.

### Correction assessment

`ACCEPT`

### Correction rationale

Свойство SDK, которое копирует частные зависимости, задаётся в эпохе 4, не в эпохе 7. Исключения хоста (`Private=false`, `ExcludeAssets=runtime`, `PrivateAssets` на пакете MCP) сохраняются, иначе в выход попадут сборки, которые загрузчик обязан не копировать. Приёмка: реальная частная managed-зависимость, вызов её кода из теневой копии, когда этой DLL нет в publish хоста.

### Evidence

`authoring.md`, дерево выхода и csproj-рецепт. Эпоха 4, строки про ссылки и тест «рядом нет RoslynMcpServer.dll». Документация .NET по plugin dependencies, на которую ссылается ревью. Проба ревьюера вне репозитория.

### Classification

`DOCUMENTATION DEFECT`

### Severity

High у ревьюера. Совпадает: первый же пакет эвристики ломает предписанный dev-путь, зелёный шаблон этого не видит.

### Required outcome

Выход `dotnet build` плагина содержит его частные managed-зависимости. Сборки контракта хоста по-прежнему не копируются.

### Proposed specification change

`authoring.md` и эпоха 4: явное свойство доставки частных зависимостей и отрицание копирования host-сборок. Тест эпохи 4 или 5 с пакетом, которого нет у хоста.

### Downstream consequences

Шаблонный csproj. Теневая копия (эпоха 3) начинает переносить эти DLL, правило исключений хоста не меняется. Тест.

---

## review-astra/C-02

### Reviewer claim

`Reference` на опубликованный `RoslynMcpServer.exe` не даёт метаданных: exe — native apphost, контракт лежит в `RoslynMcpServer.dll`.

### Related findings

Нет.

### Problem assessment

`ACCEPT`

### Problem rationale

`RoslynMcpServer.csproj` задаёт `OutputType=Exe`, self-contained publish, `PublishSingleFile=false`. В таком publish `.exe` — apphost без CLR-метаданных. Ревьюер прочитал metadata существующего publish и получил `PE image does not have metadata`. `Private=false` не делает apphost сборкой. Абзац authoring прямо разрешает ссылку на exe.

### Correction assessment

`ACCEPT`

### Correction rationale

Ссылка без исходников — на `RoslynMcpServer.dll` из того же publish, плюс compile-time ссылки, без которых не видны `IRoslynMcpPlugin` и `SolutionManager` (хост, MCP, Roslyn — те же исключения runtime-копирования). Отдельная проверка сборки плагина по каталогу publish, без `ProjectReference` на исходники, принимается как критерий authoring, не как ещё один проект в sln.

### Evidence

`RoslynMcpServer.csproj`: `OutputType`, `PublishSelfContained`, `PublishSingleFile`. `authoring.md`, абзац про exe. Проба metadata ревьюера.

### Classification

`DOCUMENTATION DEFECT`

### Severity

Medium у ревьюера. Совпадает: ломается только сценарий «нет исходников», основной `ProjectReference` жив.

### Required outcome

Authoring больше не предлагает ссылаться на apphost.

### Proposed specification change

`authoring.md`: `HintPath` на `RoslynMcpServer.dll` в publish. Эпоха 7 сверяет этот абзац с фактом, не оставляет формулировку про exe.

### Downstream consequences

Только руководство. Runtime-загрузчик по-прежнему грузит DLL плагина, не exe хоста.

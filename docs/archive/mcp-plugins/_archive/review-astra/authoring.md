ID: C-01
Severity: High
Category: Support profile

Target:
[authoring.md](../v1/authoring.md), строки 32–35, 39–61, 76–77, 205, 218–220; [эпоха 4](../v1/epoch-4-sample-plugin.md), строки 18–22, 61–67.

Claim:
После обычного `dotnet build` свои пакеты плагина лежат рядом с DLL; каталог можно целиком передать загрузчику или скопировать в drop-in.

Evidence:
В csproj рецепта нет ни `EnableDynamicLoading`, ни `CopyLocalLockFileAssemblies`. SDK `10.0.204`, `Microsoft.PackageDependencyResolution.targets`:35–42 устанавливает для такой class library `CopyLocalLockFileAssemblies=false`. Изолированная библиотека `net10.0` с `Newtonsoft.Json 13.0.3` успешно собралась, но DLL пакета отсутствовала; resolver не нашёл её. [Документация .NET по плагинам](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support#plugin-with-library-dependencies) отдельно требует включить доставку зависимостей для build или использовать publish. Тест E4 проверяет только собственную DLL и отсутствие двух host-сборок; частной зависимости у шаблона нет.

Failure scenario:
1. Автор копирует проект и добавляет обычный NuGet-пакет для эвристики.
2. `dotnet build` успешен; автор подключает `bin/Debug/net10.0`, как предписано.
3. Теневая копия содержит deps.json, но не сам пакет. Загрузка типа или первый вызов эвристики завершается ошибкой отсутствующей сборки; sample без зависимости остаётся зелёным.

Suggested change:
Сделать build-output самодостаточным для частных зависимостей, явно задав необходимые SDK properties и сохранив исключения host-контрактов. В приёмку добавить реальную частную зависимость и вызов её кода из копии при отсутствии зависимости в host publish. Обновлять проект в эпохе 4, а не откладывать исправление проекта до документационной эпохи 7.

Confidence:
High

---

ID: C-02
Severity: Medium
Category: Contract

Target:
[authoring.md](../v1/authoring.md), строки 71–74 — альтернатива ProjectReference без исходников хоста.

Claim:
На опубликованный exe допустима сборочная `Reference` с `Private=false`.

Evidence:
[RoslynMcpServer.csproj](../../../../../RoslynMcpServer.csproj):5, 14–15 задаёт executable, self-contained publish и `PublishSingleFile=false`. В таком publish `.exe` — native apphost, а управляемая сборка контракта лежит в `RoslynMcpServer.dll`. Проверка metadata существующего опубликованного exe вернула `PE image does not have metadata`. `Private=false` регулирует копирование ссылки, но не превращает apphost в CLR assembly.

Failure scenario:
1. Автор получает только publish и следует разрешённому сценарию без исходников.
2. Добавляет `Reference` / `HintPath` на `RoslynMcpServer.exe`.
3. Компилятор не получает метаданные `IRoslynMcpPlugin` и `SolutionManager`; плагин не собирается, хотя версия exe правильная.

Suggested change:
Задать reference на управляемую DLL из publish и необходимый набор compile-time зависимостей. Проверить отдельный сценарий сборки плагина по опубликованным артефактам, без ProjectReference на исходники сервера.

Confidence:
High

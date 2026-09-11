ID: E3-01
Severity: Blocker
Category: Lifecycle

Target:
epoch-3-loader-contract-and-dependencies.md / line 16-18, 22

Claim:
После успешной сборки и явного полного reload workspace в том же процессе сервер исполняет новую версию генератора. Эксперимент: замена тела без смены assembly name/version `V1` → `V2`.

Evidence:
`IAnalyzerAssemblyLoader` один на процесс (`SolutionManager._analyzerAssemblyLoader`). `ClearWorkspaceAsync` / полный reopen workspace его не создаёт заново. `LoadFromPath` кеширует по path. `Assembly.LoadFrom` для той же assembly identity обычно возвращает уже загруженную сборку. «Полный reload workspace» (E1-02) чаще cache hit и даже не dispose `MSBuildWorkspace`. Эксперимент на строке 22 проверяет ровно тот случай, где новый path (эпоха 2) недостаточен.

Failure scenario:
1. Эпоха 2 дала новый каталог для V2.
2. `reset_workspace` + `load_workspace`: новый `AnalyzerFileReference`, тот же loader, `LoadFrom(V2 path)` → identity V1.
3. Целевой контракт эпохи 3 красный; промежуточный restart (lines 42-45) — фактический максимум. Критерий завершения lines 68-70 требует, чтобы проверки «проходили», не «были измерены и провалены».

Suggested change:
Сначала прогнать эксперимент на текущем loader. Если V1 остаётся — целевой контракт in-process update не является требованием эпохи, а гипотезой. Статус complete не может требовать прохождения V1→V2 same-identity. Restart-контракт тогда не «промежуточный релиз», а результат эпохи.

Confidence:
High

---

ID: E3-02
Severity: High
Category: Design

Target:
epoch-3-loader-contract-and-dependencies.md / line 33-35, 49-51

Claim:
Кандидат — отдельный `AssemblyLoadContext` на поколение. Обнаружение приватных runtime DLL: build metadata, manifest или иной проверенный механизм. Не считать весь output-каталог корректным набором без анализа.

Evidence:
`LoadFrom` и так probing рядом с загруженной DLL. Текущий shadow копирует только main+PDB, поэтому helper не находится. Копирование helper *в тот же каталог поколения* закрывает типичный SDK output без ALC. `AnalyzerReference` в `MSBuildWorkspace` не несёт deps.json/metadata набора runtime DLL; «проверенный механизм» скорее всего потребует второй evaluation, которую эпоха 5 ещё только допускает. Запрет «весь output-каталог» без доступного API оставляет пустое место. ALC не решает discovery.

Failure scenario:
1. Эпоха 3 начинает ALC, потому что helper не грузится.
2. Helper не грузится, потому что его нет в shadow-каталоге.
3. После ALC тот же miss, плюс type-identity Roslyn.

Suggested change:
Порядок эксперимента: (1) положить рядом с main все `*.dll` из каталога `CompilationOutputInfo` кроме уже известного набора контрактных Roslyn/framework; (2) измерить helper-only и version conflict; (3) ALC только если (2) показал fusion same-identity. Не стартовать с ALC.

Confidence:
High

---

ID: E3-03
Severity: High
Category: TypeIdentity

Target:
epoch-3-loader-contract-and-dependencies.md / line 37-40

Claim:
При изоляции необходимо сохранить совместимую идентичность типов Roslyn. Нельзя без разбора грузить отдельные копии контрактных сборок. Политику записать явно.

Evidence:
Для `IIncrementalGenerator` / `DiagnosticAnalyzer` это не опциональная политика, а единственный рабочий режим: контрактные сборки только из default ALC хоста. «Записать явно» после эксперимента — значит кандидат ALC не специфицирован. Реализация «ALC на поколение» без этой политики ломает генерацию целиком (тип генератора не `Microsoft.CodeAnalysis.IIncrementalGenerator` хоста).

Failure scenario:
1. Collectible ALC грузит Generator.dll вместе с его `Microsoft.CodeAnalysis.dll` из output/NuGet.
2. `is IIncrementalGenerator` ложно, SG молчит, diagnostics Consumer чистые.
3. Эпоха 1 oracle без SG text (E1-01) снова зелёный.

Suggested change:
Если ALC остаётся кандидатом, политика sharing CodeAnalysis/System.* — входное условие эксперимента, не выходное. Иначе кандидат заведомо невалиден.

Confidence:
High

---

ID: E3-04
Severity: High
Category: Resolve

Target:
epoch-3-loader-contract-and-dependencies.md / line 54-57, 63-64

Claim:
Не разрешать приватную зависимость из произвольного каталога другого генератора только по simple name. Не грузить приватные DLL из real build output как fallback. Описать снятие глобальных обработчиков при освобождении.

Evidence:
Текущий `ResolveDependencyFromKnownDirectories` итерирует все `_dependencyDirectories` и берёт первый существующий `simpleName.dll` (`InProcessAnalyzerAssemblyLoader.cs:78-96`). Handler вешается на `AppDomain.CurrentDomain.AssemblyResolve` и никогда не снимается (`_resolveHandlerRegistered` только вверх). `AddDependencyLocation` в этом репозитории никто не вызывает; вызов зависит от `AnalyzerFileReference` внутри Roslyn. Fallback в real output текущий код не делает; LoadFrom default probing может, если original path когда-либо загружался (E1-07).

Failure scenario:
1. Два поколения/генератора, оба в `_dependencyDirectories`.
2. Resolve `Helper` — первый ключ ConcurrentDictionary, не «свой» каталог.
3. Эпоха 3 пишет политику, оставляя один процесс-глобальный handler — требование строки 54 нарушено самой сохранившейся архитектурой loader singleton.

Suggested change:
Либо один loader/ALC на поколение без глобального AssemblyResolve, либо явный отказ от isolation helpers в одном процессе. «Снять глобальные обработчики» при одном AppDomain handler и живых compilation — на практике не выполняется; не писать как приёмку.

Confidence:
High

---

ID: E3-05
Severity: High
Category: Completion

Target:
epoch-3-loader-contract-and-dependencies.md / line 42-45, 68-70

Claim:
Если in-process update не реализован, допустим промежуточный релиз с требованием перезапуска; это не выполнение целевого контракта, остаток deferred. Эпоха завершена, когда целевые проверки проходят, real output доступен для принудительной пересборки, документация описывает подтверждённый контракт.

Evidence:
Два критерия завершения конфликтуют. Lines 42-45 разрешают не выполнить целевой контракт. Lines 68-70 требуют прохождения целевых проверок (включая V1→V2 same-identity). «Промежуточный релиз» vs «эпоха complete» не ортогональны: по 45 deferred ≠ complete, по 68 complete только если V2 исполняется. Нет третьего статуса «эпоха закончена измерением, контракт = restart».

Failure scenario:
1. Эксперимент: V2 не исполняется без restart.
2. Команда либо держит эпоху 3 открытой бесконечно, либо объявляет complete вопреки 45, либо закрывает по 68 ложно (oracle E1-01).

Suggested change:
Complete = записан измеренный контракт (in-process V2 | restart required) + lock real output проверен + нет resolve из чужого каталога, если helpers поддержаны. Целевой in-process V2 — optional stretch, не gate.

Confidence:
High

---

ID: E3-06
Severity: Medium
Category: API

Target:
epoch-3-loader-contract-and-dependencies.md / line 26-27, 59

Claim:
Проверять исполняемый результат, не только путь в `AnalyzerFileReference`. Успех подготовки файлов не равен успеху загрузки генератора.

Evidence:
`RewriteResult.Applied` ставится после `File.Copy`, до любого `LoadFrom` (`AnalyzerReferenceShadowCopier.cs:141-150`). Load происходит лениво при compilation overlay solution. Ошибка загрузки в RewriteResult не попадает; Consumer видит отсутствие generated members / analyzer exception в compilation diagnostics. Эпоха 3 хочет в ошибке указать генератор, поколение, зависимость — нет точки, где это ловится, кроме первого `GetSemanticModelAsync`. Принудительный eager load на overlay time меняет стоимость load_workspace и не специфицирован.

Failure scenario:
1. Prepare Applied=true, helper missing.
2. `load_workspace` summary «N rewritten».
3. Первая семантическая операция — непонятный SG miss; статус «успех подготовки» уже ушёл клиенту.

Suggested change:
Либо eager `LoadFromPath` + проверка `IIncrementalGenerator` на prepare (с оговоркой стоимости), либо summary `rewritten` не считать успехом генерации (инвариант 5). Сейчас инвариант 5 уже нарушен shipped кодом; эпоха 3 это не чинит, пока Applied := copy.

Confidence:
High

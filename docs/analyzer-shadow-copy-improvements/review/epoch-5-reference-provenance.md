ID: E5-01
Severity: Blocker
Category: Matching

Target:
epoch-5-reference-provenance.md / line 13-25

Claim:
Разделить поиск по имени и подтверждение происхождения. При недостаточности данных оставить исходную ссылку. Не предполагать, что analyzer `ProjectReference` есть в `Project.ProjectReferences`. Сохранить исходный repro с отсутствующим неправильным путём; равенство исходного пути правильному output задачу не решает.

Evidence:
Shipped fix работает *потому что* filename == уникальный `AssemblyName`, а копируется `CompilationOutputInfo` matched-проекта, не original path (`AnalyzerReferenceShadowCopier.cs:93-127`). `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` часто не даёт обычный `ProjectReference` в модели — спека это признаёт. Тогда «подтверждение происхождения» из Roslyn model пусто. Правило 4: не подтвердили → не заменять. Правило 5: исходный repro должен чиниться. Одновременно оба правила невыполнимы, если данных provenance нет. «Сначала проверить, какие данные доступны» не задаёт fallback именно для missing-path repro.

Failure scenario:
1. Проверяют API: нет связи AnalyzerReference→project id.
2. Реализуют skip-on-unconfirmed.
3. GenRepro снова CS0103. Эпоха 5 «безопаснее», feature мертва.

Suggested change:
Пока provenance API нет, правило замены для shipped repro: unique AssemblyName match AND matched output exists AND (original path missing/unreadable OR original path is the matched project's output). Skip только если original path существует и это другой файл (NuGet). Не делать unconfirmed=skip дефолтом. Зафиксировать результат разведки API *до* смены matcher; если данных нет — эпоха 5 не меняет matcher.

Confidence:
High

---

ID: E5-02
Severity: High
Category: Matching

Target:
epoch-5-reference-provenance.md / line 9-10, 20-21, 41-43

Claim:
Ошибочная замена меняет исполняемый анализатор даже при существующем корректном исходном пути. Наличие пути, похожего на output проекта, не заменяет подтверждение связи. Исходный repro с перенаправленным OutputPath продолжает исправляться.

Evidence:
Опасный случай — original path *существует* и указывает на NuGet/другой DLL с тем же filename. Текущий matcher заменит его in-solution output. Безопасный skip здесь правилен. Исходный repro — original path *не существует* и filename совпал. Спека запрещает path-similarity как подтверждение, но не вводит positive rule на missing file. Без неё E5-01 схлопывается в skip.

Failure scenario:
1. Пишут matcher: replace iff provenance token X.
2. X недоступен; missing-path не проходит.
3. Acceptance «внешний analyzer с тем же именем не заменяется» зелёный на существующем NuGet path; acceptance «repro чинится» красный. Выбирают первое, потому что эпоха названа «подтверждение происхождения».

Suggested change:
Два разных решения, оба в приёмке: (existing foreign path → never replace); (missing/wrong path + unique in-solution AssemblyName + output exists → replace, даже без ProjectReference). Не искать одно «происхождение» на оба.

Confidence:
High

---

ID: E5-03
Severity: Medium
Category: TFM

Target:
epoch-5-reference-provenance.md / line 26-27, 45-46

Claim:
Configuration/TFM учитывать там, где они меняют выбор сборки. Если вариант неоднозначен, пропустить замену вместо первого совпадения. Проверены разные конфигурации и multi-targeting: правильный выбор или безопасный пропуск.

Evidence:
`load_workspace` уже выбирает inner TFM через `targetFramework` (иначе missing `Compile`, отдельный fail). `CompilationOutputInfo` / `OutputFilePath` — выход уже загруженного inner project. Повторный перебор TFM из csproj создаёт ложную неоднозначность на успешно загруженном одном inner. Текущий matcher уже skip при двух проектах с одним `AssemblyName` (`GroupBy` Count==1). Multi-targeting как два inner project в одной solution — редкость без двух project entries.

Failure scenario:
1. Реализация смотрит все TargetFrameworks в xml, видит netstandard2.0;net10.0, skip.
2. Workspace уже загружен как net10.0, output есть, shipped repro сломан «ради безопасности».
3. Либо берут «первый» TFM в props — другой класс ошибок, который спека запрещает, но skip хуже для цели флага.

Suggested change:
Выбор сборки = output уже резолвленного `Project` в loaded solution. Неоднозначность = два `Project` с одним AssemblyName в этом solution, не два TFM в файле. Не сканировать unevaluated TFM список.

Confidence:
High

---

ID: E5-04
Severity: Medium
Category: Diagnostics

Target:
epoch-5-reference-provenance.md / line 35-38, 47

Claim:
Различать: подтверждённая замена, неоднозначность, происхождение не подтверждено, output отсутствует, ошибка подготовки. Отсутствие output и неподтверждённая связь различимы.

Evidence:
`RewriteResult` сейчас `Applied` + свободный `SkipReason`. Различение output-missing vs unconfirmed vs ambiguous реально только если matcher их разделяет. После E5-01 unconfirmed и «нет provenance API» совпадут с большинством in-solution analyzer refs, включая успешный repro. Диагностика станет шумом: все skip «происхождение не подтверждено», original missing path неотличим от NuGet same-name в логах, если original path не логируют как exists=false.

Failure scenario:
1. Клиент видит summary `0 rewritten, N skipped`.
2. N skip reason одинаковые.
3. Агент не включает флаг повторно / считает feature broken без различия «собери генератор» vs «это NuGet, не трогаем».

Suggested change:
Минимум два skip code: `source_output_missing` vs `original_exists_unrelated` vs `ambiguous_assembly_name`. Не вводить `provenance_unconfirmed` как корзину, пока нет API подтверждения.

Confidence:
High

---

ID: E5-05
Severity: Medium
Category: Independence

Target:
epoch-5-reference-provenance.md / line 3, README.md / line 25

Claim:
Зависимость: эпоха 1; может выполняться независимо от эпох 2–4.

Evidence:
Смена matcher без выноса I/O (эпоха 2) оставляет recopy на каждом edit. Неверный matcher будет заново применяться и при reoverlay. Независимость от 2–4 формально есть для *алгоритма выбора проекта*, но не для безопасного внедрения: сломанный matcher сразу в production overlay. Эпоха 1 без executable oracle (E1-01) не отличит «заменили не тот analyzer» (исполняется другой generator с тем же exported type shape).

Failure scenario:
1. Эпоха 5 до эпохи 2, matcher заменяет NuGet analyzer на in-solution DLL.
2. Diagnostics чистые, тип с тем же именем есть.
3. Поведенческий баг только в runtime генерации; epoch-1 baseline его не видит.

Suggested change:
Не независима от oracle эпохи 1. Фикстура «NuGet analyzer same filename + in-solution project» обязательна в эпохе 1, иначе эпоха 5 не имеет регрессии.

Confidence:
High

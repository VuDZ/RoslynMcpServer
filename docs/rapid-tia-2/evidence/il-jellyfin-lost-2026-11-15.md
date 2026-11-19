# Потери `discovery` и слой `discoveryWithNamedLoads`, 2026-11-15

Шаг 5а [rapid TIA 2](../README.md). Спайк — `d766638`. Тесты заново не запускались: каталоги `classes`
шага 5 и Debug-вывод те же. Сводки повтора:

- `jellyfin` recall: [named-loads](il-jellyfin-recall-2026-11-15-named-loads-summary.json),
  measure: [named-loads](il-jellyfin-2026-11-15-named-loads-summary.json);
- `orchard-wide` recall: [named-loads](il-orchard-wide-recall-2026-11-15-named-loads-summary.json),
  measure: [named-loads](il-orchard-wide-2026-11-15-named-loads-summary.json);
- `roslyn-deep` measure: [named-loads](il-roslyn-deep-2026-11-15-named-loads-summary.json).

`recall-raw.json` и `raw.json` остаются в `LOCAL_ROOT/il-runs` (`*-recall-named-2026-11-15`,
`*-named-2026-11-15`).

## Повтор recall и measure

Команды те же, что в шагах 4 и 5, бинарник собран из `d766638`.

| Корпус | Слой | recall | filesLosing | selectedPairs | precision | Медиана доли уровня 0 | p90 | Классов до загрузки по имени |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `jellyfin` | `discovery` | 99,10% | 16 из 1012 | 131 490 | 15,43% | 24,7% | 78,3% | 73 |
| `jellyfin` | `discoveryWithLoads` | 99,15% | 10 из 1012 | 185 727 | 10,93% | 47,7% | 79,5% | 73 |
| `jellyfin` | `discoveryWithNamedLoads` | 99,13% | 11 из 1012 | 180 739 | 11,23% | 46,2% | 79,5% | 73 |
| `orchard-wide` | `discovery` | 38,20% | 1016 из 1572 | 39 907 | 23,68% | 0% | 29,4% | 60 |
| `orchard-wide` | `discoveryWithLoads` | 100% | 1 из 1572 | 354 470 | 6,98% | 31,3% | 32,2% | 60 |
| `orchard-wide` | `discoveryWithNamedLoads` | 100% | 1 из 1572 | 354 470 | 6,98% | 31,3% | 32,2% | 60 |
| `roslyn-deep` | `discovery` | — | — | — | — | 80,0% | 99,1% | 4065 |
| `roslyn-deep` | `discoveryWithLoads` | — | — | — | — | 99,8% | 168,6% | 4065 |
| `roslyn-deep` | `discoveryWithNamedLoads` | — | — | — | — | 99,9% | 168,6% | 4065 |

На `jellyfin` пар 20 472. `discoveryWithLoads` теряет 174 пары (держит 20 298).
`discoveryWithNamedLoads` теряет 179 пар (держит 20 293): на 5 пар меньше, не столько же.
`selectedPairs` меньше на 4 988 (185 727 против 180 739). Широкое правило увеличивает ответ
относительно `discovery` на 41%, узкое — на 37%. Медиана доли уровня 0 падает с 47,7% до 46,2%.
До `AppDomain.GetAssemblies` доходят 70 тестовых классов (4 типа с таким вызовом).

Пять пар, которые узкое правило не возвращает, — это `PlaylistsFolder.cs` (1 класс) и ещё 4 класса
у `ItemTypeLookup.cs`. Оба файла лежат в `Emby.Server.Implementations`. Тест до них доходит только через
`GetAssemblies`, а тип в этой сборке он заранее не называет, поэтому узкое правило её не сканирует.
Подтипы `BaseItem` в `MediaBrowser.Controller` (`MusicVideo`, `Trailer`, `LiveTvChannel`, `LiveTvProgram`,
`Playlist`) узкое правило сохраняет: `typeof(BaseItem)` уже достижим, и сборка сканируется.

На `orchard-wide` слои совпали: `selectedPairs`, recall и `filesLosing` одни и те же.
`AppDomain.GetAssemblies` не вызывает ни один тип; все загрузки — `Assembly.Load` (5 типов, 37 классов).
Одна потерянная пара остаётся в обоих слоях.

На `roslyn-deep` узкий слой чуть шире по пустым ответам: файлов без ответа 0 против 2 у широкого,
медиана 99,9% против 99,8%, p90 тот же 168,6%. Сумма этапов 9,5 с, пик 581 МБ.

## Шестнадцать файлов `jellyfin`

`lost.discovery.byFile` шага 5. `why` — из `d766638`, тот же Debug-вывод.
Где потерянных классов больше десяти, в `recall-raw.json` лежат первые десять имён; остальные — другие
классы той же тестовой сборки, у которых `why` по этому типу отвечает `not reached`.

Две причины.

### Данные теории соседнего класса

`dotnet test --filter` всё равно ищет тесты всей сборки. xUnit при поиске вычисляет `MemberData` и
`ClassData` каждого класса, не только того, который потом запускается. Конструкторы из этих данных
попадают в покрытие класса, который их не вызывает. В IL потерянного класса токена нет. Токен есть
у соседнего класса, и `discovery` этот соседний класс для файла сохраняет. Правилом по IL потерянного
класса это не поймать: правило увидело бы зависимость, которой у этого класса нет.

| Файл | Потеряно из исполняющих | Откуда исполнение |
| --- | --- | --- |
| `MediaBrowser.Model\Cryptography\PasswordHash.cs` | 12 из 17 | `PasswordHashTests.Parse_Valid_TestData`, `new PasswordHash` |
| `Emby.Naming\AudioBook\AudioBookFileInfo.cs` | 22 из 29 | `AudioBookResolverTests.Resolve_ValidFileNameTestData`, `new AudioBookFileInfo` |
| `Emby.Naming\Video\VideoFileInfo.cs` | 18 из 31 | `VideoResolverTests`, `new VideoFileInfo` в данных теории |
| `MediaBrowser.Model\MediaInfo\SubtitleTrackEvent.cs` | 8 из 9 | `SsaParserTests.Parse_MultipleDialogues_TestData`, `new SubtitleTrackEvent` |
| `MediaBrowser.Model\Dto\MediaSourceInfo.cs` | 5 из 27 | `SubtitleEncoderTests.GetReadableFile_Valid_TestData`, `new MediaSourceInfo` |
| `MediaBrowser.Controller\LiveTv\TimerInfo.cs` | 7 из 11 | `RecordingHelperTests.GetRecordingName_Success_TestData`, `new TimerInfo` |
| `MediaBrowser.Controller\Entities\Audio\Audio.cs` | 11 из 104 | `IndexNumberComparerTests` и `ParentIndexNumberComparerTests`, `new Audio()` в `MemberData` |
| `MediaBrowser.Controller\Entities\BaseItem.cs` | 18 из 176 | `VideoImageProviderTests.GetImage_UnsupportedInput_ReturnsNoImage_TestData`, `new Movie`; в сборке реализаций — `ClassData` `AiredEpisodeOrderComparerTests` (`new Movie`, `new Episode`) |
| `MediaBrowser.Controller\Entities\Video.cs` | 18 из 140 | те же `new Movie` / `new Episode`: конструктор проходит через `Video` |
| `Emby.Server.Implementations\Data\ItemTypeLookup.cs` | 59 из 121 | `ItemTypeLookupTests.MappedKinds`, `new ItemTypeLookup()` |

Проверка `why` для класса без токена: `MimeTypesTests` → `PasswordHash`, `CleanDateTimeTests` →
`AudioBookFileInfo`, `HdHomerunHostTests` → `TimerInfo`, `AssParserTests` → `SubtitleTrackEvent`,
`EncoderValidatorTests` → `MediaSourceInfo`, `InstallationManagerTests` → `Audio` и `ItemTypeLookup`,
`TmdbUtilsCastTests` → `BaseItem`. Все слои, включая оба слоя с загрузкой, отвечают `not reached`.
`CleanDateTimeTests` → `VideoFileInfo` достижим за 2 шага по ссылке (`VideoResolver`): этот класс
в число 18 потерянных не входит.

Числа сходятся со сборками. В `Jellyfin.LiveTv.Tests` 11 классов, `TimerInfo` исполняют все 11,
теряются 7. В `Jellyfin.MediaEncoding.Tests` 9 классов, `SubtitleTrackEvent` исполняют все 9, теряются 8.

### Рефлексия по загруженным сборкам

`BaseItemKindTests` сам, не сосед. `BaseItemKind_TestData` берёт `AppDomain.CurrentDomain.GetAssemblies()`,
на каждой сборке вызывает `GetTypes()` и оставляет неабстрактных наследников `typeof(BaseItem)`.
Второй тест вызывает конструктор без параметров через `GetConstructor().Invoke`. Имена наследников
в IL нет.

Только этот класс теряет по одному файлу:

- `Emby.Server.Implementations\Playlists\PlaylistsFolder.cs`;
- `MediaBrowser.Controller\Entities\MusicVideo.cs`;
- `MediaBrowser.Controller\Entities\Trailer.cs`;
- `MediaBrowser.Controller\LiveTv\LiveTvChannel.cs`;
- `MediaBrowser.Controller\LiveTv\LiveTvProgram.cs`;
- `MediaBrowser.Controller\Playlists\Playlist.cs`.

`why` для `PlaylistsFolder`: `references`, `discovery`, `wholeAssembly` и `discoveryWithNamedLoads` —
`not reached`; `discoveryWithLoads` — один шаг `LoadedByName`. В IL видны вызовы `GetAssemblies`,
`GetTypes`, `IsSubclassOf` и токен `BaseItem`. Имя конкретного наследника — нет. Широкое правило
отдаёт все сборки рядом с тестовой, и `Emby.Server.Implementations.dll` среди них есть. Узкое правило
сканирует только сборки, до типов которых тест уже дошёл: `MediaBrowser.Controller` (там лежит `BaseItem`)
попадает, `Emby.Server.Implementations` — нет. Поэтому `PlaylistsFolder` остаётся в 11 файлах узкого слоя
и выходит из 10 файлов широкого.

Правило «`GetTypes` и `IsSubclassOf(T)` дают наследников `T`» по IL написать можно: вызовы и `T` в токене.
Какие сборки уже загружены, строка не называет. Это тот же предел, что у `GetAssemblies` в описании слоя.

## Пара `orchard-wide`

`HttpsSettingsMigrationsTests` и `src\OrchardCore.Modules\OrchardCore.Https\Migrations\HttpsSettingsMigrations.cs`.
Класс миграции внутренний. Тест находит его так:

```csharp
var migrationType = typeof(HttpsSettings).Assembly.GetType("OrchardCore.Https.Migrations.HttpsSettingsMigrations");
var migration = Activator.CreateInstance(migrationType, siteService);
var method = migrationType.GetMethod("CreateAsync");
```

`why`: `references`, `discovery`, оба слоя с загрузкой — `not reached`. `wholeAssembly` — один шаг:
`typeof(HttpsSettings).Assembly` уже записан как сканирование сборки, и этот слой берёт каждый её тип.
`discovery` берёт только типы, обращённые к фреймворку, и наследников уже достигнутого типа.
`HttpsSettingsMigrations : DataMigration`, а токена `DataMigration` у теста нет: имя метода тоже строка.
База лежит в решении, не в пакете, поэтому тип не считается обращённым к фреймворку.

В IL виден токен `HttpsSettings`, вызов `get_Assembly`, вызов `GetType` и строка
`OrchardCore.Https.Migrations.HttpsSettingsMigrations` (`ldstr`). Токена самого типа миграции нет.
Правило, которое сопоставляет такую строку с типом сканируемой сборки, поймало бы эту пару и не брало
бы остальные типы сборки. Текущий `discovery` строки не читает. `wholeAssembly` пару уже держит и шире.
`GetAssemblies` на этом корпусе не встречается, поэтому узкий слой пару не возвращает: она остаётся
единственной потерей обоих слоёв с загрузкой.

## Выводы исполнителя

Владелец их не подтверждал. Шаг 7 и Q-01 этим не закрываются, новых правил в спайк нет.

Узкое правило для `AppDomain.GetAssemblies` на этих корпусах не окупается. На `jellyfin` широкое правило
добавляет к `discovery` 54 237 пар; узкое снимает из них 4 988 и одновременно теряет 5 пар, которые
широкое правило держало. Медиана доли уровня 0 сдвигается с 47,7% до 46,2%. На `orchard-wide` слои
совпадают, потому что `GetAssemblies` там нет. На `roslyn-deep` узкий слой чуть шире широкого: пустых
ответов 0 против 2, p90 остаётся 168,6%.

Десять из шестнадцати файлов `jellyfin` — шум прогона, а не дыра графа. Хост VSTest при поиске тестов
сборки исполняет `MemberData` и `ClassData` соседнего класса, и покрытие записывает это на класс, который
фильтр потом не запускает. Токен типа есть в IL соседа, и `discovery` этого соседа для файла сохраняет.
Правило по IL потерянного класса увидело бы зависимость, которой у класса нет.

Две потери видны в IL частично, и обе уже закрывает более широкий слой.

- `BaseItemKindTests` вызывает `GetAssemblies`, `GetTypes` и `IsSubclassOf(typeof(BaseItem))`. Вызовы и
  токен базы в IL есть, имён наследников нет. Широкое правило отдаёт каталог рядом с тестовой сборкой и
  возвращает `PlaylistsFolder`. Узкое правило эту сборку не сканирует: тест заранее не дошёл ни до одного
  её типа. Правило «`GetTypes` плюс `IsSubclassOf(T)` даёт наследников `T`» по токенам написать можно;
  список уже загруженных сборок в IL не назван.
- `HttpsSettingsMigrationsTests` передаёт имя типа строкой в `Assembly.GetType`. В IL есть `ldstr` и токен
  `HttpsSettings`. Сопоставление строки с типом сканируемой сборки поймало бы одну пару. `wholeAssembly`
  эту пару уже держит вместе с остальными типами сборки.

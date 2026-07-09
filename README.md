# TSLib

Клиентская библиотека TeamSpeak 3/5 (full client + server query) для .NET.

Форк [TSLib из Splamy/TS3AudioBot](https://github.com/Splamy/TS3AudioBot/tree/master/TSLib), выделенный в отдельный репозиторий для использования в [MobileTS](https://github.com/Errorovich/MobileTS). Лицензия — [OSL-3.0](LICENSE), как у апстрима.

## Сборка

```
git clone --recursive https://github.com/Errorovich/TSLib.git
dotnet build TSLib.csproj
```

`--recursive` обязателен: декларации протокола ([ReSpeak/tsdeclarations](https://github.com/ReSpeak/tsdeclarations)) подключены сабмодулем в `Declarations/`.

## Структура модулей

Папка = неймспейс (`Shared/` → `TSLib.Shared` и т.д.); исключения: `Helper/R.cs` намеренно объявляет результат-типы `R`/`E` в `namespace System`.

| Модуль | Содержимое |
|---|---|
| `Shared/` | Пассивные типы протокола: ID-структуры (`Types`), enum'ы (`TsEnums`), генерённые таблицы (`TsErrorCode`, `TsPermission`, `TsVersion`), `ConnectionData`, `DisconnectEventArgs`, `TsDnsResolver`, примитивы проводного формата `TsString` (escape/unescape) и `TsConst` (лимиты сервера) |
| `ClientBase/` | Общий каркас обоих клиентов: `TsBaseFunctions` (база full/query + генерённые обёртки команд), `MessageProcessor`, `WaitBlock`, `EventDispatcher`, `LazyNotification` |
| `Crypto/` | Идентичность и криптография: статические утилиты `TsCrypt` (identity, хеши, подписи), `IdentityData`, `License` (сюда же ляжет авторизация TS5/TS6) |
| `Commands/` | Исходящее направление (клиент → сервер): построитель `TsCommand` и виды параметров |
| `Messages/` | Входящее направление (сервер → клиент): `Deserializer`, генерённые классы уведомлений/ответов |
| `Full/` | Полный (голосовой) клиент: `TsFullClient`, `ConnectionContext`, `FullClientHandshake`; `Full/Transport/` — пакетный уровень (`Packet`, `PacketHandler`, `PacketCipher` — per-connection шифр AES-EAX, `VoicePacket`, `QuickerLz`…); `Full/Book/` — реплицируемое состояние сервера |
| `Query/` | Query-клиент (`TsQueryClient`) |
| `Audio/` | Аудио-пайплайн: интерфейсы и `Audio/Pipes/` (сегменты), `Audio/Opus/` (кодек) |
| `Helper/`, `Scheduler/`, `Logging/` | Утилиты, планировщик (`DedicatedTaskScheduler`), логирование |

Зависимости модулей однонаправленные: per-connection пакетный шифр (`Full/Transport/PacketCipher`) живёт рядом с `Packet`/`PacketHandler` и зовёт статические утилиты `Crypto/TsCrypt` — восходящих ссылок Crypto → Full нет.

## Кодогенерация (T4)

Часть исходников генерируется T4-шаблонами из `Declarations/`; результат закоммичен, поэтому для обычной сборки перегенерация не нужна. Сгенерированные файлы носят суффикс `.gen.cs` и лежат **рядом со своим `.tt` в модуле по смыслу** (`Shared/Types.gen.cs`, `Shared/TsErrorCode.gen.cs`, `ClientBase/TsBaseFunctions.gen.cs`, `Messages/Messages.gen.cs`, `Full/Book/Book.gen.cs` и т.д.) — пара `.tt`+`.gen.cs` обязана жить в одной папке (`LastGenOutput`/`DependentUpon` в csproj заданы голыми именами). Парсеры деклараций (`.ttinclude`) — тоже по модулям (`Messages/MessageParser.ttinclude`, `Full/Book/BookParser.ttinclude`, `Shared/ErrorParser.ttinclude`; общие `Util`/`NotificationUtil` — в корне). Неймспейс генерённого кода зашит в шаблоне, а `include`/`Host.ResolvePath` — относительные, поэтому при переносе `.tt` правьте и пути внутри него. **Не редактируйте сгенерированные `.cs` руками** — правьте `.tt`/`.ttinclude` и перегенерируйте. После обновления сабмодуля `Declarations` перегенерация обязательна.

Как перегенерировать (любой вариант):

- Visual Studio → *Transform All Templates*;
- CLI: `TextTransform.exe` из VS (`Common7/IDE/TextTransform.exe`), по файлу: `TextTransform.exe Messages\Messages.gen.tt -out Messages\Messages.gen.cs`.

Требование: шаблоны ссылаются на `Nett.dll` по пути `%userprofile%/.nuget/packages/nett/0.13.0/lib/Net40/Nett.dll` — пакет [Nett 0.13.0](https://www.nuget.org/packages/Nett/0.13.0) должен лежать в NuGet-кэше (достаточно распаковать nupkg в эту папку).

Нюансы движка (VS2022+/`dotnet-t4` против движка времён VS2019, которым генерировались файлы апстрима):

- перевод строки после инлайнового `<# #>`-блока в конце текстовой строки съедается — statement-блоки держим на отдельных строках;
- начальные пробелы автономной контрольной строки эмитятся в вывод как текст — контрольные строки держим на нулевой колонке, отступами внутри WriteLine-регионов управляем через `PushIndent`/`PopIndent` (и `ClearIndent` перед возвратом к тексту шаблона — `CurrentIndent` действует и на литеральный текст);
- съедание переводов строк работает только с CRLF — `.gitattributes` принудительно чекаутит `*.tt`/`*.ttinclude` с CRLF; не переписывайте шаблоны инструментами, молча конвертирующими в LF (генерация «поедет» пустыми строками).

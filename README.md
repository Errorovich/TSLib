# TSLib

Клиентская библиотека TeamSpeak 3/5 (full client + server query) для .NET.

Форк [TSLib из Splamy/TS3AudioBot](https://github.com/Splamy/TS3AudioBot/tree/master/TSLib), выделенный в отдельный репозиторий для использования в [MobileTS](https://github.com/Errorovich/MobileTS). Лицензия — [OSL-3.0](LICENSE), как у апстрима.

## Сборка

```
git clone --recursive https://github.com/Errorovich/TSLib.git
dotnet build TSLib.csproj
```

`--recursive` обязателен: декларации протокола ([ReSpeak/tsdeclarations](https://github.com/ReSpeak/tsdeclarations)) подключены сабмодулем в `Declarations/`.

## Кодогенерация (T4)

Часть исходников генерируется T4-шаблонами из `Declarations/`; результат закоммичен, поэтому для обычной сборки перегенерация не нужна. Сгенерированные файлы носят суффикс `.gen.cs` и лежат **рядом со своим `.tt` в модуле по смыслу** (`Full/Book/Book.gen.cs`, `Messages/Messages.gen.cs`, `TsErrorCode.gen.cs` в корне и т.д.); парсеры деклараций (`.ttinclude`) — тоже по модулям (`Messages/MessageParser.ttinclude`, `Full/Book/BookParser.ttinclude`, общие `Util`/`NotificationUtil`/`ErrorParser` — в корне). **Не редактируйте сгенерированные `.cs` руками** — правьте `.tt`/`.ttinclude` и перегенерируйте. После обновления сабмодуля `Declarations` перегенерация обязательна.

Как перегенерировать (любой вариант):

- Visual Studio → *Transform All Templates*;
- CLI: `TextTransform.exe` из VS (`Common7/IDE/TextTransform.exe`), по файлу: `TextTransform.exe Messages\Messages.gen.tt -out Messages\Messages.gen.cs`.

Требование: шаблоны ссылаются на `Nett.dll` по пути `%userprofile%/.nuget/packages/nett/0.13.0/lib/Net40/Nett.dll` — пакет [Nett 0.13.0](https://www.nuget.org/packages/Nett/0.13.0) должен лежать в NuGet-кэше (достаточно распаковать nupkg в эту папку).

Нюансы движка (VS2022+/`dotnet-t4` против движка времён VS2019, которым генерировались файлы апстрима):

- перевод строки после инлайнового `<# #>`-блока в конце текстовой строки съедается — statement-блоки держим на отдельных строках;
- начальные пробелы автономной контрольной строки эмитятся в вывод как текст — контрольные строки держим на нулевой колонке, отступами внутри WriteLine-регионов управляем через `PushIndent`/`PopIndent` (и `ClearIndent` перед возвратом к тексту шаблона — `CurrentIndent` действует и на литеральный текст);
- съедание переводов строк работает только с CRLF — `.gitattributes` принудительно чекаутит `*.tt`/`*.ttinclude` с CRLF; не переписывайте шаблоны инструментами, молча конвертирующими в LF (генерация «поедет» пустыми строками).

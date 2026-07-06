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

Часть исходников генерируется T4-шаблонами из `Declarations/` (`*.gen.cs`, `Generated/*.cs`); результат закоммичен, поэтому для обычной сборки перегенерация не нужна. **Не редактируйте сгенерированные `.cs` руками** — правьте `.tt`/`.ttinclude` и перегенерируйте. После обновления сабмодуля `Declarations` перегенерация обязательна.

Как перегенерировать (любой вариант):

- Visual Studio → *Transform All Templates*;
- CLI: `TextTransform.exe` из VS (`Common7/IDE/TextTransform.exe`), по файлу: `TextTransform.exe Generated\Messages.tt -out Generated\Messages.cs`.

Требование: шаблоны ссылаются на `Nett.dll` по пути `%userprofile%/.nuget/packages/nett/0.13.0/lib/Net40/Nett.dll` — пакет [Nett 0.13.0](https://www.nuget.org/packages/Nett/0.13.0) должен лежать в NuGet-кэше (достаточно распаковать nupkg в эту папку).

Нюанс: современный движок T4 (VS2022+, `dotnet-t4`) обрабатывает переводы строк после инлайновых `<# #>`-блоков иначе, чем движок времён VS2019, которым были сгенерированы исходные файлы апстрима. Шаблоны в этом репозитории приведены к виду, дающему корректный вывод на современном движке (statement-блоки вынесены на отдельные строки); при правке шаблонов не ставьте `<# ... #>` в конец текстовой строки — перевод строки после него будет съеден.

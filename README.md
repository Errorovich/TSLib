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

Часть исходников генерируется T4-шаблонами из `Declarations/` (`*.gen.cs`, `Generated/*.cs`); результат закоммичен, поэтому для обычной сборки перегенерация не нужна. **Не редактируйте сгенерированные `.cs` руками** — правьте `.tt`/`.ttinclude` и перегенерируйте через Visual Studio → *Transform All Templates* (CLI `dotnet` шаблоны не выполняет). После обновления сабмодуля `Declarations` перегенерация обязательна.

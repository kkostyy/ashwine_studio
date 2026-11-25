# Скачивает готовые бесплатные плагины с umod.org (план 1.4) в plugins\ и server\oxide\plugins\.
# Запуск: powershell -ExecutionPolicy Bypass -File tools\get-umod.ps1
# В репозиторий эти файлы не попадают (.gitignore) — у них свои авторы и лицензии.
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Dst = @("$Root\plugins", "$Root\server\oxide\plugins")
$List = @(
    @{ File = "CopyPaste.cs";           Why = "копия/вставка построек — нужен Blueprints" },
    @{ File = "ZoneManager.cs";         Why = "зоны с флагами" },
    @{ File = "Spawns.cs";              Why = "база точек появления — нужна CustomSpawnPoints" },
    @{ File = "CustomSpawnPoints.cs";   Why = "свои точки появления" },
    @{ File = "ImageLibrary.cs";        Why = "картинки для UI — нужна SignArtist" },
    @{ File = "SignArtist.cs";          Why = "картинки на табличках" },
    @{ File = "AdminRadar.cs";          Why = "радар для админов" },
    @{ File = "Vanish.cs";              Why = "невидимость админа" },
    @{ File = "PlayerAdministration.cs";Why = "окно управления игроком" },
    @{ File = "Telekinesis.cs";         Why = "двигать и вращать объекты (вместо Entity Editor)" },
    @{ File = "BuildingGrades.cs";      Why = "улучшить/понизить всю постройку командой" },
    @{ File = "RemoverTool.cs";         Why = "удаление построек и объектов (/remove)" },
    @{ File = "BetterNoStability.cs";   Why = "стройка без устойчивости (/ns)" },
    @{ File = "StackSizeController.cs"; Why = "большие стаки предметов" },
    @{ File = "InstantCraft.cs";        Why = "мгновенный крафт" },
    @{ File = "FurnaceSplitter.cs";     Why = "авто-раскладка в печах" },
    @{ File = "QuickSort.cs";           Why = "быстрая сортировка инвентаря и ящиков" },
    @{ File = "NoDecay.cs";             Why = "постройки не гниют" },
    @{ File = "PermissionsManager.cs";  Why = "права через окно (/perms)" }
)
foreach ($p in $List) {
    $url = "https://umod.org/plugins/$($p.File)"
    try {
        $tmp = Join-Path $env:TEMP $p.File
        Invoke-WebRequest $url -OutFile $tmp -UseBasicParsing
        foreach ($d in $Dst) { if (Test-Path $d) { Copy-Item $tmp (Join-Path $d $p.File) -Force } }
        Write-Host ("OK   {0,-26} {1}" -f $p.File, $p.Why) -ForegroundColor Green
    } catch {
        Write-Host ("FAIL {0,-26} скачай вручную: {1}" -f $p.File, $url) -ForegroundColor Yellow
    }
}
Write-Host "`nДальше: права — см. docs/FIRST_RUN.md, раздел «Готовые плагины»."

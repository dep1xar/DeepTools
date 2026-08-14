# Скрипт сборки DeepTools.
# Запуск: powershell -ExecutionPolicy Bypass -File build.ps1
# Собирает DeepTools.exe из всех .cs в папке. Если программа запущена - просит закрыть.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Host "csc.exe не найден. Нужен .NET Framework 4.x" -ForegroundColor Red
    exit 1
}

$sources = Get-ChildItem *.cs | ForEach-Object { $_.Name }

# Managed-зависимости встраиваем в exe как ресурсы вида DeepTools.Embedded.<Имя>.dll.
# EmbeddedAssemblies.cs достаёт их оттуда в рантайме - релиз становится одним файлом.
$embedDlls = @(
    "LibreHardwareMonitorLib.dll",
    "HidSharp.dll",
    "DiskInfoToolkit.dll",
    "RAMSPDToolkit-NDD.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Threading.AccessControl.dll"
)
# Ресурсы встраиваются сжатыми (gzip), EmbeddedAssemblies/SensorDriver распаковывают
# их в рантайме - exe худеет примерно вдвое. Сжатые копии складываются во временную папку
$gzDir = Join-Path $env:TEMP "deeptools_build_gz"
New-Item -ItemType Directory -Force -Path $gzDir | Out-Null
Add-Type -AssemblyName System.IO.Compression | Out-Null

function Compress-ToGz([string]$srcPath, [string]$gzPath) {
    $data = [System.IO.File]::ReadAllBytes((Resolve-Path $srcPath))
    $fs = [System.IO.File]::Create($gzPath)
    $gz = New-Object System.IO.Compression.GZipStream($fs, [System.IO.Compression.CompressionLevel]::Optimal)
    $gz.Write($data, 0, $data.Length)
    $gz.Close(); $fs.Close()
}

$resourceArgs = @()
foreach ($dll in $embedDlls) {
    if (-not (Test-Path $dll)) {
        Write-Host "Не найдена DLL для встраивания: $dll" -ForegroundColor Red
        exit 1
    }
    # Имя ресурса = простое имя сборки (без -NDD и т.п. суффиксов файла) + .dll.gz
    $asmName = [System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $dll)).Name
    $gzPath = Join-Path $gzDir "$asmName.dll.gz"
    Compress-ToGz $dll $gzPath
    $resourceArgs += "/resource:$gzPath,DeepTools.Embedded.$asmName.dll.gz"
}

# Встраиваем установщик драйвера датчиков PawnIO, чтобы предлагать его прямо из программы,
# когда LibreHardwareMonitor заблокирован (Целостность памяти в Windows 11)
if (Test-Path "PawnIO_setup.exe") {
    $gzPawn = Join-Path $gzDir "PawnIO_setup.exe.gz"
    Compress-ToGz "PawnIO_setup.exe" $gzPawn
    $resourceArgs += "/resource:$gzPawn,DeepTools.PawnIO_setup.exe.gz"
}

Write-Host "Компиляция $($sources.Count) файлов, встраивание $($embedDlls.Count) DLL..." -ForegroundColor Cyan

& $csc /nologo /target:winexe /out:DeepTools_build.exe `
    /win32icon:logo.ico /win32manifest:app.manifest `
    /reference:System.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Core.dll `
    /reference:System.Management.dll `
    /reference:LibreHardwareMonitorLib.dll `
    $resourceArgs `
    $sources

if ($LASTEXITCODE -ne 0) {
    Write-Host "Ошибка компиляции" -ForegroundColor Red
    exit 1
}

# Заменяем рабочий exe; если программа запущена - пробуем с паузой до 5 раз
$replaced = $false
for ($i = 0; $i -lt 5; $i++) {
    try {
        Move-Item DeepTools_build.exe DeepTools.exe -Force -ErrorAction Stop
        $replaced = $true
        break
    } catch {
        Write-Host "DeepTools.exe занят - закрой программу (трей -> Выход), попытка $($i+1)/5..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
    }
}
if (-not $replaced) {
    Write-Host "Не удалось заменить DeepTools.exe - новая сборка лежит как DeepTools_build.exe" -ForegroundColor Red
    exit 1
}

$ver = (Get-Item DeepTools.exe).VersionInfo.FileVersion
$size = [math]::Round((Get-Item DeepTools.exe).Length / 1KB)
Write-Host "Готово: DeepTools.exe v$ver ($size КБ)" -ForegroundColor Green

# DeepTools build script
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#
# Compiles DeepTools.exe from every .cs file in this folder and embeds the
# managed DLLs as resources. The only requirement is the .NET Framework 4.x
# compiler that ships with Windows.
#
# Every step prints what it is doing, and a failure prints one clearly marked
# block, so a crash is never silent.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Write-Banner([string]$text, [string]$color) {
    Write-Host ""
    Write-Host "=============================================================" -ForegroundColor $color
    Write-Host " $text" -ForegroundColor $color
    Write-Host "=============================================================" -ForegroundColor $color
}

function Fail([string]$message) {
    Write-Banner "BUILD FAILED: $message" "Red"
    exit 1
}

# ---------------------------------------------------------------------------
# 1. Compiler
# ---------------------------------------------------------------------------
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Fail "csc.exe (.NET Framework 4.x compiler) was not found. Install .NET Framework 4.x."
}

# ---------------------------------------------------------------------------
# 2. Sources and required files
# ---------------------------------------------------------------------------
$sources = @(Get-ChildItem -Path $PSScriptRoot -Filter *.cs | ForEach-Object { $_.Name })
if ($sources.Count -eq 0) {
    Fail "No .cs files found next to build.ps1."
}

$required = @("program.cs", "mainform.cs", "AssemblyInfo.cs", "app.manifest", "logo.ico")
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $PSScriptRoot $file))) {
        Fail "Missing file: $file"
    }
}

# ---------------------------------------------------------------------------
# 3. Managed DLLs embedded as resources
#    DeepTools.Embedded.<Name>.dll, extracted at runtime by EmbeddedAssemblies.cs
# ---------------------------------------------------------------------------
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

foreach ($dll in $embedDlls) {
    if (-not (Test-Path (Join-Path $PSScriptRoot $dll))) {
        Fail "Missing DLL: $dll (it must sit next to build.ps1)."
    }
}

# Resources are embedded compressed (gzip) to keep the release a single file
$gzDir = Join-Path $env:TEMP "deeptools_build_gz"
New-Item -ItemType Directory -Force -Path $gzDir | Out-Null
Add-Type -AssemblyName System.IO.Compression | Out-Null

function Compress-ToGz([string]$srcPath, [string]$gzPath) {
    $data = [System.IO.File]::ReadAllBytes((Resolve-Path $srcPath))
    $fs = [System.IO.File]::Create($gzPath)
    try {
        $gz = New-Object System.IO.Compression.GZipStream($fs, [System.IO.Compression.CompressionLevel]::Optimal)
        try { $gz.Write($data, 0, $data.Length) } finally { $gz.Close() }
    } finally { $fs.Close() }
}

$resourceArgs = @()
foreach ($dll in $embedDlls) {
    # Resource name = simple assembly name (strips -NDD and similar suffixes)
    $asmName = [System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $dll)).Name
    $gzPath = Join-Path $gzDir "$asmName.dll.gz"
    Compress-ToGz $dll $gzPath
    $resourceArgs += "/resource:$gzPath,DeepTools.Embedded.$asmName.dll.gz"
}

# PawnIO sensor driver installer, offered from the app when LibreHardwareMonitor
# is blocked (Memory Integrity in Windows 11)
if (Test-Path (Join-Path $PSScriptRoot "PawnIO_setup.exe")) {
    $gzPawn = Join-Path $gzDir "PawnIO_setup.exe.gz"
    Compress-ToGz "PawnIO_setup.exe" $gzPawn
    $resourceArgs += "/resource:$gzPawn,DeepTools.PawnIO_setup.exe.gz"
}

# ---------------------------------------------------------------------------
# 4. Compile
# ---------------------------------------------------------------------------
$outExe = "DeepTools_build.exe"
if (Test-Path $outExe) { Remove-Item $outExe -Force -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "Compiling $($sources.Count) files, embedding $($embedDlls.Count) DLLs..." -ForegroundColor Cyan

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/out:$outExe",
    "/win32icon:logo.ico",
    "/win32manifest:app.manifest",
    "/reference:System.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Core.dll",
    "/reference:System.Management.dll",
    "/reference:LibreHardwareMonitorLib.dll"
)
$cscArgs += $resourceArgs
$cscArgs += $sources

$compilerOutput = & $csc $cscArgs 2>&1
$compilerExit = $LASTEXITCODE

if ($compilerOutput) {
    foreach ($line in $compilerOutput) {
        Write-Host "  $line"
    }
}

if ($compilerExit -ne 0) {
    Write-Banner "COMPILATION ERROR (csc exit code $compilerExit)" "Red"
    Write-Host " The compiler messages are printed above this banner." -ForegroundColor Red
    Write-Host " Most common cause: a .cs file is missing or was edited badly." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $outExe)) {
    Fail "csc reported success but $outExe was not produced."
}

# ---------------------------------------------------------------------------
# 5. Install the new exe
#    A running DeepTools.exe cannot be overwritten, but it CAN be renamed,
#    so we move it aside instead of reporting a bogus failure.
# ---------------------------------------------------------------------------
$target = Join-Path $PSScriptRoot "DeepTools.exe"
$oldExe = Join-Path $PSScriptRoot "DeepTools_old.exe"

if (Test-Path $oldExe) { Remove-Item $oldExe -Force -ErrorAction SilentlyContinue }

$running = @(Get-Process -Name "DeepTools" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host ""
    Write-Host "DeepTools is running (PID $($running.Id -join ', '))." -ForegroundColor Yellow
    Write-Host "Renaming the running exe out of the way..." -ForegroundColor Yellow
}

$installed = $false
try {
    Move-Item $outExe $target -Force -ErrorAction Stop
    $installed = $true
} catch {
    # Locked by the running instance - rename it, then put ours in place
    try {
        Move-Item $target $oldExe -Force -ErrorAction Stop
        Move-Item $outExe $target -Force -ErrorAction Stop
        $installed = $true
    } catch {
        $installed = $false
    }
}

if (-not $installed) {
    Write-Banner "BUILD OK, but the exe could not be installed" "Yellow"
    Write-Host " The compiler succeeded. The new binary is: $outExe" -ForegroundColor Yellow
    Write-Host " Close DeepTools (tray icon -> Exit) and run the build again." -ForegroundColor Yellow
    exit 0
}

if (Test-Path $oldExe) {
    Write-Host "Old binary moved to DeepTools_old.exe (delete it after closing DeepTools)." -ForegroundColor Yellow
}

$ver = (Get-Item $target).VersionInfo.FileVersion
$sizeKB = [math]::Round((Get-Item $target).Length / 1KB)
Write-Banner "BUILD OK: DeepTools.exe v$ver ($sizeKB KB)" "Green"
exit 0

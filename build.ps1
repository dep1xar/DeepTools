# DeepTools build script.
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
# Compiles DeepTools.exe from all .cs files in the folder.

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Host "csc.exe not found. .NET Framework 4.x is required." -ForegroundColor Red
    exit 1
}

$sources = Get-ChildItem *.cs | ForEach-Object { $_.Name }

# Managed dependencies are embedded as resources: DeepTools.Embedded.<Name>.dll
# EmbeddedAssemblies.cs extracts them at runtime - release becomes a single file.
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

# Resources are embedded compressed (gzip). Compressed copies go to a temp folder.
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
        Write-Host "DLL not found for embedding: $dll" -ForegroundColor Red
        exit 1
    }
    # Resource name = simple assembly name (without -NDD etc. suffixes) + .dll.gz
    $asmName = [System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $dll)).Name
    $gzPath = Join-Path $gzDir "$asmName.dll.gz"
    Compress-ToGz $dll $gzPath
    $resourceArgs += "/resource:$gzPath,DeepTools.Embedded.$asmName.dll.gz"
}

# Embed PawnIO sensor driver installer so it can be offered from the app
# when LibreHardwareMonitor is blocked (Memory Integrity in Windows 11)
if (Test-Path "PawnIO_setup.exe") {
    $gzPawn = Join-Path $gzDir "PawnIO_setup.exe.gz"
    Compress-ToGz "PawnIO_setup.exe" $gzPawn
    $resourceArgs += "/resource:$gzPawn,DeepTools.PawnIO_setup.exe.gz"
}

Write-Host "Compiling $($sources.Count) files, embedding $($embedDlls.Count) DLLs..." -ForegroundColor Cyan

$cscArgs = @("/nologo", "/target:winexe", "/out:DeepTools_build.exe", "/win32icon:logo.ico", "/win32manifest:app.manifest", "/reference:System.dll", "/reference:System.Drawing.dll", "/reference:System.Windows.Forms.dll", "/reference:System.Core.dll", "/reference:System.Management.dll", "/reference:LibreHardwareMonitorLib.dll")
$cscArgs += $resourceArgs
$cscArgs += $sources

& $csc $cscArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Compilation error" -ForegroundColor Red
    exit 1
}

# Replace the working exe; if the program is running - retry up to 5 times
$replaced = $false
for ($i = 0; $i -lt 5; $i++) {
    try {
        Move-Item DeepTools_build.exe DeepTools.exe -Force -ErrorAction Stop
        $replaced = $true
        break
    } catch {
        Write-Host "DeepTools.exe is locked - close the program (tray -> Exit), attempt $($i+1)/5..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
    }
}
if (-not $replaced) {
    Write-Host "Could not replace DeepTools.exe - new build is at DeepTools_build.exe" -ForegroundColor Red
    exit 1
}

$ver = (Get-Item DeepTools.exe).VersionInfo.FileVersion
$sizeKB = [math]::Round((Get-Item DeepTools.exe).Length / 1KB)
Write-Host "Done: DeepTools.exe v$ver ($sizeKB KB)" -ForegroundColor Green
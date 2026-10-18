<#
  Builds the Microsoft Store package (MSIX) from a published BPGPUpdater.exe.
    -Exe      folder with BPGPUpdater.exe (dotnet publish output)
    -Version  app version, e.g. 1.0.1 or 1.0.1-beta1; the Store wants four numbers, so it becomes 1.0.1.0
    -Output   path of the .msix to write
  Uses makeappx.exe from the Windows 10/11 SDK (installed on GitHub's windows-latest runners).
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Output
)
$ErrorActionPreference = 'Stop'

# 1.0.1-beta1 -> 1.0.1.0 (the Store requires the fourth number to be 0)
$numbers = ($Version -replace '^v', '' -replace '[-+].*$', '').Split('.')
while ($numbers.Count -lt 3) { $numbers += '0' }
$msixVersion = ($numbers[0..2] -join '.') + '.0'

$stage = Join-Path ([IO.Path]::GetTempPath()) ("bpgp-msix-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $stage 'Assets') | Out-Null
Copy-Item (Join-Path $Exe 'BPGPUpdater.exe') $stage
if (Test-Path (Join-Path $Exe 'BPGPUpdater.exe.config')) { Copy-Item (Join-Path $Exe 'BPGPUpdater.exe.config') $stage }
Copy-Item (Join-Path $PSScriptRoot 'Assets\*.png') (Join-Path $stage 'Assets')
$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
Set-Content (Join-Path $stage 'AppxManifest.xml') ($manifest.Replace('{VERSION}', $msixVersion)) -Encoding UTF8

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe not found: install the Windows 10/11 SDK." }

if (Test-Path $Output) { Remove-Item $Output }
& $makeappx.FullName pack /d $stage /p $Output /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }
Remove-Item $stage -Recurse -Force
"Built $Output (version $msixVersion)"

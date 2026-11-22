# Capri Remote's PC-side checks: builds RemoteTests.exe with her csc, then vectors, units, qr, e2e against wrangler dev
# or -Relay, and live. Nothing deploys or signs in.
# powershell -NoProfile -ExecutionPolicy Bypass -File app\RemoteTests\run.ps1 [-OutputDirectory <dir>] [-Only vectors,units,qr,e2e,live] [-Relay wss://<host>]
param([string]$OutputDirectory, [string[]]$Only = @('vectors', 'units', 'qr', 'e2e', 'live'), [string]$Relay)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) 'capri-remote-tests' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'csc.exe not found: the tests need the .NET Framework 4.8 that comes with Windows 10 and 11.' }
$exe = Join-Path $OutputDirectory 'RemoteTests.exe'
$sources = @('RemoteCrypto', 'Remote', 'RemoteLink', 'RemoteQr', 'RemoteVectors', 'RemoteBrainKeys') | ForEach-Object { Join-Path $root "app\$_.cs" }
$sources += Join-Path $PSScriptRoot 'RemoteTests.cs'
# The screen view and call, and their drawn desktop and stand-in ears.
$sources += @('RemoteScreen', 'RemoteCall') | ForEach-Object { Join-Path $root "app\$_.cs" }
$sources += Join-Path $PSScriptRoot 'RemoteLiveTests.cs'
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Security.dll', 'System.Runtime.Serialization.dll') | ForEach-Object { "/reference:$_" }
& $compiler /nologo /target:exe /platform:anycpu /optimize+ /warn:4 /utf8output /nowarn:649 $references "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'RemoteTests.exe did not build.' }

$results = @()
function Step([string]$name, [scriptblock]$run) {
    Write-Host "== $name"
    # Node and Python log to stderr, which isn't a failure: only the exit code is.
    $ErrorActionPreference = 'Continue'
    & $run
    $passed = $LASTEXITCODE -eq 0
    $script:results += ('{0} {1}' -f $(if ($passed) { 'PASS' } else { 'FAIL' }), $name)
}
if ($Only -contains 'vectors') { Step 'vectors' { & $exe vectors (Join-Path $root 'remote\test\vectors.json') } }
if ($Only -contains 'units') { Step 'units' { & $exe units } }
if ($Only -contains 'qr') {
    $codes = Join-Path $OutputDirectory 'qr'
    if (Test-Path -LiteralPath $codes) { Remove-Item -LiteralPath $codes -Recurse -Force }
    Step 'qr' { & $exe qr $codes; if ($LASTEXITCODE -eq 0) { & python (Join-Path $PSScriptRoot 'check-qr.py') $codes } }
}
if ($Only -contains 'e2e') {
    Push-Location $root
    $e2e = @((Join-Path $PSScriptRoot 'remote-e2e.mjs'), $exe)
    if ($Relay) { $e2e += @('--relay', $Relay) }
    try { Step $(if ($Relay) { 'e2e live' } else { 'e2e' }) { & node $e2e } }
    finally { Pop-Location }
}
if ($Only -contains 'live') {
    Push-Location $root
    $live = @((Join-Path $PSScriptRoot 'remote-live-e2e.mjs'), $exe)
    if ($Relay) { $live += @('--relay', $Relay) }
    try { Step $(if ($Relay) { 'live on the relay' } else { 'live' }) { & node $live } }
    finally { Pop-Location }
}
Write-Host ''
$results | ForEach-Object { Write-Host $_ }
if ($results -match '^FAIL') { exit 1 }

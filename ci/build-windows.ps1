param([string]$OutputDirectory, [switch]$SmokeTest, [int]$SmokeSeconds = 60, [switch]$Store)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
# Which compiler, and why it matters beyond speed.
#
# The csc that ships with Windows is the old one, C# 5, with no /deterministic: it stamps the PE header with
# the time and the assembly with a fresh GUID, so the same source compiled twice gave two different exes.
# Measured: two builds back to back, nothing changed, hashes 6d8007126c68f533 and 18b04dacdfd87344. Every
# install of a rebuild therefore carried a different exe, which is no use to anybody trying to certify one
# or to tell whether the thing on disk is the thing that was built.
#
# Roslyn, from Visual Studio or the dotnet SDK, takes /deterministic and gives byte-identical output. It is
# asked for C# 5 so the language is exactly what the old compiler allowed and nothing new creeps in, and
# /pathmap keeps this machine's folder names out of the binary.
#
# When no Roslyn is here the build still works on the old compiler and says plainly that the exe will not be
# reproducible, because a quietly unreproducible build is worse than a loud one.
$deterministic = $true
$candidates = @(
    'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'),
    (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe')
)
$compiler = $null
foreach ($guess in $candidates) { if ($guess -and (Test-Path -LiteralPath $guess)) { $compiler = $guess; break } }
if (-not $compiler) {
    $compiler = Join-Path $framework 'csc.exe'
    $deterministic = $false
    Write-Warning 'No Roslyn compiler found, so this build is not reproducible: the same source will give a different exe every time. Install the Visual Studio build tools to get a stable hash.'
}
$wpf = Join-Path $framework 'WPF'
$winmd = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata') -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+(\.\d+)+$' } | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not (Test-Path -LiteralPath $compiler)) { throw 'csc.exe not found. Capri needs the .NET Framework 4.8 that comes with Windows 10 and 11.' }
if (-not $winmd) { throw 'Windows.winmd not found. Install the Windows 10 or 11 SDK.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'out\windows' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$app = Join-Path $out 'Capri.exe'
$setup = Join-Path $out 'Capri-Setup.exe'
$icon = Join-Path $root 'assets\chip.ico'
$shared = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/warn:4', ('/win32manifest:' + (Join-Path $root 'app\app.manifest')), ('/win32icon:' + $icon), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$references = @('/reference:System.Runtime.Serialization.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Security.dll', ('/reference:' + (Join-Path $wpf 'UIAutomationClient.dll')), ('/reference:' + (Join-Path $wpf 'UIAutomationTypes.dll')), ('/reference:' + (Join-Path $wpf 'WindowsBase.dll')), ('/reference:' + (Join-Path $wpf 'PresentationCore.dll')), ('/reference:' + (Join-Path $wpf 'System.Speech.dll')), ('/reference:' + $winmd), ('/reference:' + (Join-Path $framework 'System.Runtime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll')))
$resources = @(('/resource:' + $icon + ',Capri.Chip.ico'), ('/resource:' + (Join-Path $root 'assets\chip.png') + ',Capri.Chip.png'), ('/resource:' + (Join-Path $root 'assets\fonts\ShareTechMono-Regular.ttf') + ',Capri.Font.Mono.ttf'))
$resources += ('/resource:' + (Join-Path $root 'assets\fonts\CapriMainframe-Bold.ttf') + ',Capri.Font.Title.ttf')
$resources += ('/resource:' + (Join-Path $root 'assets\cursor.png') + ',Capri.Cursor.png')
# The Store copy carries no driver: policy 10.1.5 leaves non-Microsoft drivers out of what an app may bring in.
if (-not $Store) { $resources += ('/resource:' + (Join-Path $root 'assets\vbcable\VBCABLE_Driver_Pack45.zip') + ',Capri.VBCABLE.zip') }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\sprites') -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Sprite.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\emotes') -Filter *.zip | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Emote.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\voice') -Filter *.wav | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Voice.' + $_.Name }
$sources = @('Capri', 'Asides', 'Bolts', 'Commentary', 'Lookout', 'Brain', 'Leaks', 'PlanBrain', 'Workspace', 'WorkspaceWindow', 'WorkspaceParts', 'WorkspaceLook', 'WorkspaceWords', 'Talk', 'Ribbons', 'Emotes', 'Mainframe', 'MainframeKeys', 'MainframeMods', 'MainframeModsWindow', 'MainframeSlide', 'Mods', 'ModDirectory', 'ModApi', 'CodeMods', 'ModHooks', 'ModMusic', 'SoundsLike', 'SkinMods', 'SkillMods', 'ArtColors', 'MainframeTips', 'TipChecks', 'MediumChecks', 'FreeKeyChecks', 'FirstRun', 'FirstRunChecks', 'Neon', 'Fonts', 'Glyphs', 'Layout', 'SlimBar', 'LogSheet', 'Notice', 'Engine', 'Ledges', 'Voice', 'Ears', 'MicMix', 'CableSetup', 'Packaged', 'Dictation', 'HearingStatus', 'Agent', 'Hands', 'GameHud', 'Computer', 'Arrange', 'Mixer', 'Usage', 'Network', 'Play', 'VoicePack', 'Startup', 'Presence', 'Player', 'Ocr', 'GameEars', 'GameNotes', 'GameKeys', 'Frames', 'Reminders', 'GameSense', 'GameWatch', 'SelfKnow', 'Care', 'Clips', 'ClipSettings', 'ClipChecks', 'Chimes', 'ChimeSettings', 'Minis', 'Chess', 'ChessBoard', 'ChessTalk', 'Files', 'FileFinder', 'Websites', 'Ink', 'Clones', 'ChaosScreen', 'Screensaver', 'Icons', 'FollowUps', 'WatchAlong', 'CallMode', 'Husk', 'CallEars', 'CallPanel', 'FileEditor', 'FileChecks', 'Reactions', 'ToolChecks', 'LeakChecks', 'ErrorChecks', 'Remote', 'RemoteCrypto', 'RemoteLink', 'RemoteQr', 'RemoteVectors', 'RemoteBrainKeys', 'RemoteBrains', 'RemoteChat', 'MainframeRemote', 'RemoteScreen', 'RemoteCall', 'RemoteLive', 'SmokeRun', 'Twitch', 'Chatters', 'Playlist', 'Commands', 'VirtualCamera', 'StreamSafe', 'SysLines', 'Personalities', 'Hours', 'Away', 'Favourites', 'Probes', 'Working', 'Them', 'Dynamics', 'Mood', 'Sayings', 'VoiceProfiles', 'Interests', 'LineFile', 'Moments', 'Angles', 'Lore', 'Touch', 'Notes', 'Lint', 'Grammar', 'Tucked', 'Slots', 'Zones', 'Setup', 'AssemblyInfo') | ForEach-Object { Join-Path $root ('app\' + $_ + '.cs') }
# Anything in app\ named *.local.cs is compiled in when it is there and skipped when it is not. Nothing is
# written down here by name, so the build has no list to read and no feature to describe. A build with none
# present is the ordinary one: feature excluded on purpose.
$local = @(Get-ChildItem -LiteralPath (Join-Path $root 'app') -Filter '*.local.cs' -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
# Silent when there is nothing to compile in: an ordinary build says nothing about what it does not have.
$stable = @()
if ($deterministic) { $stable = @('/deterministic', '/langversion:5', ('/pathmap:' + $root + '=.')) }
& $compiler ($shared + $stable + $references + $resources + @('/unsafe+', ('/out:' + $app)) + $sources + $local)
if ($LASTEXITCODE -ne 0) { throw 'Capri compilation failed.' }
if ($Store) {
    # The Store copy gets its number too. It is a different exe from the ordinary one on purpose, with
    # no driver in it, so the two hashes are meant to differ and each is meant to repeat.
    Write-Output "Built $app for the Microsoft Store"
    Write-Output ("Hash     " + (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant())
    return
}
Copy-Item -LiteralPath $app -Destination $setup -Force
# What was built, so the thing on disk can be told apart from the thing that was built. The same source and
# the same version give the same number every time now, which is the whole point of it.
$stamp = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Built $app and $setup"
Write-Output "Hash     $stamp"

if (-not $SmokeTest) { return }
$runtime = Join-Path $out 'smoke\runtime'
$package = Join-Path $out 'smoke\package'
$check = Start-Process -FilePath $app -ArgumentList @('--smoke-test', ('"' + $runtime + '"')) -PassThru -WindowStyle Hidden
if (-not $check.WaitForExit($SmokeSeconds * 1000)) { $check.Kill(); throw 'Runtime smoke test timed out.' }
if (Test-Path -LiteralPath (Join-Path $runtime 'smoke-test.txt')) { Get-Content -LiteralPath (Join-Path $runtime 'smoke-test.txt') }
elseif ($check.ExitCode -eq 0) { throw 'Runtime smoke test wrote no results. Another smoke test probably held the lock; rerun it when that one ends.' }
if ($check.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $runtime 'failure.txt')) { Get-Content -LiteralPath (Join-Path $runtime 'failure.txt') }
    throw 'Runtime smoke test failed.'
}
$verify = Start-Process -FilePath $setup -ArgumentList @('--verify', ('"' + $package + '"')) -PassThru -WindowStyle Hidden
if (-not $verify.WaitForExit($SmokeSeconds * 1000)) { $verify.Kill(); throw 'Installer check timed out.' }
if ($verify.ExitCode -ne 0) {
    if (Test-Path -LiteralPath (Join-Path $package 'failure.txt')) { Get-Content -LiteralPath (Join-Path $package 'failure.txt') }
    throw 'Installer check failed.'
}
if ((Get-FileHash -LiteralPath (Join-Path $package 'Capri.exe')).Hash -ne (Get-FileHash -LiteralPath $app).Hash) { throw 'Installer payload differs from the app executable.' }
Get-Content -LiteralPath (Join-Path $package 'packaging-test.txt')
Write-Output 'PASS installer payload matches the app'

param([string]$OutputDirectory, [switch]$SmokeTest, [int]$SmokeSeconds = 60, [switch]$Store, [switch]$Checks)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
# Which compiler, and why it matters beyond speed.
#
# Windows' own csc (C# 5) has no /deterministic and stamps a time and a GUID, so the same source gave a different
# exe each build; the stamps are fixed after compiling so a build can be checked against its source.
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
# Her checks are in the test build only (-Checks); the build that ships carries none of them. A smoke run after the
# build needs them, so -SmokeTest makes the test build.
if ($SmokeTest) { $Checks = $true }
if ($Checks) { $shared += '/define:CHECKS' }
$references = @('/reference:System.Runtime.Serialization.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Security.dll', '/reference:System.Management.dll', ('/reference:' + (Join-Path $wpf 'UIAutomationClient.dll')), ('/reference:' + (Join-Path $wpf 'UIAutomationTypes.dll')), ('/reference:' + (Join-Path $wpf 'WindowsBase.dll')), ('/reference:' + (Join-Path $wpf 'PresentationCore.dll')), ('/reference:' + (Join-Path $wpf 'System.Speech.dll')), ('/reference:' + $winmd), ('/reference:' + (Join-Path $framework 'System.Runtime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')), ('/reference:' + (Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll')))
$resources = @(
    ('/resource:' + $icon + ',Capri.Chip.ico'),
    ('/resource:' + (Join-Path $root 'assets\chip.png') + ',Capri.Chip.png'),
    ('/resource:' + (Join-Path $root 'assets\fonts\ShareTechMono-Regular.ttf') + ',Capri.Font.Mono.ttf')
)
$resources += ('/resource:' + (Join-Path $root 'assets\fonts\CapriMainframe-Bold.ttf') + ',Capri.Font.Title.ttf')
$resources += ('/resource:' + (Join-Path $root 'assets\cursor.png') + ',Capri.Cursor.png')
$resources += ('/resource:' + (Join-Path $root 'assets\words\common-words.txt') + ',Capri.CommonWords.txt')
$resources += ('/resource:' + (Join-Path $root 'assets\mod-fixes.json') + ',Capri.ModFixes.json')
# Capri Camera, her own camera device (Lachu), built with Visual Studio's C++ tools when they are here.
# Built once into out\camera and again only when its sources change, so most builds skip the C++ step.
$camera = Join-Path $root 'out\camera'
$cameraDlls = @('64', '32') | ForEach-Object { Join-Path $camera ('CapriCamera' + $_ + '.dll') }
$cameraNewest = (Get-ChildItem -LiteralPath (Join-Path $root 'native\softcam') -Recurse -File | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
$cameraFresh = -not ($cameraDlls | Where-Object { -not (Test-Path -LiteralPath $_) -or (Get-Item -LiteralPath $_).LastWriteTimeUtc -lt $cameraNewest })
if ($cameraFresh -or (& (Join-Path $PSScriptRoot 'build-camera.ps1') -OutputDirectory $camera)) { foreach ($bits in @('64', '32')) { $resources += ('/resource:' + (Join-Path $camera ('CapriCamera' + $bits + '.dll')) + ',Capri.Camera' + $bits + '.dll') } }
# Mods she carries and lays into her mods folder herself.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods\door-intro') -Filter 'door-intro.capri-code'  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Mod.' + $_.Name }
# Her screensaver and her pet game, carried and laid the same way; the pet game is laid in off.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods') -File | Where-Object { $_.Name -eq 'capri-screensaver.capri-code' -or $_.Name -eq 'capri-tamagotchi.capri-code' }  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Mod.' + $_.Name }
# And the door's sound and pop-up pictures, which live in the same folder as the door and are laid into its own
# folder the first time the door is stocked.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods\door-intro') -File | Where-Object { $_.Extension -ne '.capri-code' }  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.DoorArt.' + $_.Name }
# Door 2, carried and laid the same way, its sound and pictures in its own folder.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods') -File | Where-Object { $_.Name -eq 'startup-intros.capri-code' -or $_.Name -eq 'grammar.capri-code' }  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Checked.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods\Door2Intro') -Filter 'door2-intro.capri-code'  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Mod.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods\Door2Intro') -File | Where-Object { $_.Extension -ne '.capri-code' }  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Door2Art.' + $_.Name }
# Spout2's SpoutLibrary (BSD 2-Clause), for Transparent through OBS's Spout2 plugin; laid in her folder when used.
$spout = Join-Path $root 'native\spout\SpoutLibrary.dll'
if (Test-Path -LiteralPath $spout) { $resources += ('/resource:' + $spout + ',Capri.Spout.SpoutLibrary.dll') }
# Skills she carries with her tools, laid into her mods folder the same way.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'mods') -File | Where-Object { $_.Name -eq 'planner-planning.capri-skill' } | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Skill.' + $_.Name }
# The modding guide, laid into her mods folder for Make a mod.
$resources += ('/resource:' + (Join-Path $root 'mods\MODDING.md') + ',Capri.Modding.md')
# Mod templates, laid as loose files into her mods\templates, never read as mods.
$templates = Join-Path $root 'mods\templates'
$resources += Get-ChildItem -LiteralPath $templates -File -Recurse | Sort-Object FullName | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Template.' + $_.FullName.Substring($templates.Length + 1).Replace('\', '/') }
# Her stories, carried so a new install has them with nothing fetched. Only where the pack sits
# beside the source; a build without it fetches them from its release instead.
$lorePack = Join-Path (Split-Path -Parent $root) 'packs\lore.md'
if (Test-Path -LiteralPath $lorePack) {
    # Compressed: a third of the size, and her prose isn't left as plain strings in the exe.
    $loreGz = Join-Path $out 'lore.md.gz'
    $plain = [IO.File]::ReadAllBytes($lorePack)
    $packed = [IO.File]::Create($loreGz)
    $zip = New-Object IO.Compression.GZipStream($packed, [IO.Compression.CompressionLevel]::Optimal)
    $zip.Write($plain, 0, $plain.Length); $zip.Dispose(); $packed.Dispose()
    $resources += ('/resource:' + $loreGz + ',Capri.Lore.md.gz')
}
# The topics and hobbies list she writes into topics.md.
$resources += ('/resource:' + (Join-Path $root 'assets\topics.md') + ',Capri.Topics.md')
# Her menus and labels in other languages, plain files of English and translation, built in as the defaults.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'languages') -Filter 'gui-*.txt' -File -ErrorAction SilentlyContinue  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Gui.' + $_.Name.Substring(4) }
# And her prank sounds, laid beside her in sounds\pranks.
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\pranks') -File  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.PrankSound.' + $_.Name }
# The Store copy carries no driver: policy 10.1.5 leaves non-Microsoft drivers out of what an app may bring in.
if (-not $Store) { $resources += ('/resource:' + (Join-Path $root 'assets\vbcable\VBCABLE_Driver_Pack45.zip') + ',Capri.VBCABLE.zip') }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\sprites') -Filter *.png  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Sprite.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\emotes') -Filter *.zip  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Emote.' + $_.Name }
$resources += Get-ChildItem -LiteralPath (Join-Path $root 'assets\voice') -Filter *.wav  | Sort-Object Name | ForEach-Object { '/resource:' + $_.FullName + ',Capri.Voice.' + $_.Name }
# The Planner's editor, StoryBoard (planner\, under its own licence), and WebView2's SDK (lib\webview2\,
# Microsoft's licence beside it), carried inside her and laid in her Planner folder.
$webview = Join-Path $root 'lib\webview2'
$webviewCore = Join-Path $webview 'Microsoft.Web.WebView2.Core.dll'
$webviewForms = Join-Path $webview 'Microsoft.Web.WebView2.WinForms.dll'
if ((Test-Path -LiteralPath $webviewCore) -and (Test-Path -LiteralPath $webviewForms)) {
    $shared += '/define:WEBVIEW2'
    $references += @(('/reference:' + $webviewCore), ('/reference:' + $webviewForms))
    $resources += @(('/resource:' + $webviewCore + ',Capri.WebView2.Core.dll'), ('/resource:' + $webviewForms + ',Capri.WebView2.WinForms.dll'))
    foreach ($bits in @('x64', 'x86')) {
        $loader = Join-Path $webview ('win-' + $bits + '\WebView2Loader.dll')
        if (Test-Path -LiteralPath $loader) { $resources += ('/resource:' + $loader + ',Capri.WebView2.Loader.' + $bits + '.dll') }
    }
}
$plannerSource = Join-Path $root 'planner'
if (Test-Path -LiteralPath (Join-Path $plannerSource 'app\index.html')) {
    # One zip of the editor (app\, its licence beside it) and its starter project (project\), in a fixed order.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $plannerZip = Join-Path $out 'planner.zip'
    if (Test-Path -LiteralPath $plannerZip) { Remove-Item -LiteralPath $plannerZip -Force }
    $to = [IO.Compression.ZipFile]::Open($plannerZip, 'Create')
    try {
        $files = @(Get-ChildItem -LiteralPath (Join-Path $plannerSource 'app') -Recurse -File) + @(Get-ChildItem -LiteralPath (Join-Path $plannerSource 'project') -Recurse -File) | Sort-Object FullName
        foreach ($file in $files) {
            $inside = $file.FullName.Substring($plannerSource.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($to, $file.FullName, $inside, [IO.Compression.CompressionLevel]::Optimal)
        }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($to, (Join-Path $plannerSource 'LICENSE'), 'app/LICENSE', [IO.Compression.CompressionLevel]::Optimal)
    }
    finally { $to.Dispose() }
    $resources += ('/resource:' + $plannerZip + ',Capri.Planner.zip')
}
# One file a line, sorted, so branches that add files touch different lines.
$sources = @(
    'ActChecks',
    'Acts',
    'AddonColours',
    'Agent',
    'AlertScreen',
    'Aloud',
    'Angles',
    'AreaShots',
    'Arrange',
    'ArtColors',
    'Asides',
    'AssemblyInfo',
    'Away',
    'Blends',
    'Bolts',
    'Bookmarks',
    'BoxSizes',
    'Brain',
    'CableSetup',
    'CallEars',
    'CallMode',
    'CallPanel',
    'Capri',
    'CapriCamera',
    'Care',
    'ChaosScreen',
    'Chatters',
    'Chess',
    'ChessBoard',
    'ChessTalk',
    'Chimes',
    'ChimeSettings',
    'ClearFrame',
    'ClipChecks',
    'Clips',
    'ClipSettings',
    'Clock',
    'Clones',
    'CodeMods',
    'Codex',
    'CodexSum',
    'ColourChecks',
    'Commands',
    'Commentary',
    'Computer',
    'Conversation',
    'ConvoBrain',
    'Dictation',
    'Doomscroll',
    'DoomscrollSettings',
    'Dynamics',
    'Ears',
    'EditorFonts',
    'Emotes',
    'Engine',
    'EngineTune',
    'ErrorChecks',
    'EverydayJobs',
    'Extended',
    'Favourites',
    'Fields',
    'FileChecks',
    'FileEditor',
    'FileFinder',
    'FileKeeper',
    'FileKeeperChecks',
    'Files',
    'FileSlots',
    'FirstRun',
    'FirstRunChecks',
    'FitChecks',
    'Fits',
    'FolderHold',
    'FollowUps',
    'Fonts',
    'Frames',
    'FreeKeyChecks',
    'FreshStarts',
    'FullScreenChecks',
    'Gallery',
    'GameClicks',
    'GameEars',
    'GameHud',
    'GameInput',
    'GameKeys',
    'GameNotes',
    'GameSense',
    'GameWatch',
    'GifChecks',
    'Gifs',
    'GithubShots',
    'Glow',
    'Glyphs',
    'Grammar',
    'Growth',
    'Gui',
    'Hands',
    'HearingStatus',
    'Hours',
    'Husk',
    'Icons',
    'IdleChecks',
    'IdleCpu',
    'Ink',
    'Interests',
    'KeyChecks',
    'KeyEvents',
    'Keyless',
    'LangCopies',
    'LanguagePair',
    'Layout',
    'LeakChecks',
    'Leaks',
    'Ledges',
    'Legacy',
    'Library',
    'LineFile',
    'Lint',
    'ListenThrough',
    'LogChecks',
    'Logs',
    'Lookout',
    'Lore',
    'LoreSum',
    'LoveLanguages',
    'MachineSeal',
    'MailBox',
    'MailChecks',
    'MailMime',
    'MailSmtp',
    'MailTools',
    'Mainframe',
    'MainframeColours',
    'MainframeColumns',
    'MainframeConflicts',
    'MainframeGifs',
    'MainframeGrowth',
    'MainframeKeys',
    'MainframeLog',
    'MainframeMail',
    'MainframeMemory',
    'MainframeModHelper',
    'MainframeMods',
    'MainframeModsWindow',
    'MainframeOwnTrees',
    'MainframePerf',
    'MainframePersonalities',
    'MainframePet',
    'MainframePlugins',
    'MainframeRemote',
    'MainframeRingStrip',
    'MainframeSearch',
    'MainframeSections',
    'MainframeSizes',
    'MainframeSlide',
    'MainframeStreaming',
    'MainframeSystem',
    'MainframeTips',
    'MainframeToggles',
    'MainframeTools',
    'Mcp',
    'McpOnline',
    'McpSignIn',
    'MediumChecks',
    'MenuLayout',
    'MenuPlacement',
    'MicMeter',
    'MicMix',
    'Minis',
    'Mixer',
    'ModActHooks',
    'ModApi',
    'ModCollections',
    'ModConflicts',
    'ModDirectory',
    'ModFixes',
    'ModFolders',
    'ModGuide',
    'ModHooks',
    'ModImportRun',
    'ModMusic',
    'ModPetHooks',
    'ModReadout',
    'Mods',
    'ModTemplates',
    'ModTryRun',
    'Moments',
    'Mood',
    'NameLines',
    'Neon',
    'Network',
    'NoForcedIntro',
    'Notes',
    'Notice',
    'Ocr',
    'Onboarding',
    'Opening',
    'OwnFiles',
    'OwnSet',
    'Packaged',
    'PanelFolds',
    'PanelRows',
    'Panels',
    'PanelSpot',
    'PanelsSide',
    'PanelTree',
    'PanelTreeBuild',
    'PanelTreeEntry',
    'PanelTreePieces',
    'PatTrace',
    'Personalities',
    'PersonalitySettings',
    'PersonalitySettingsList',
    'PetMode',
    'PetRates',
    'PetStats',
    'PetStatsChecks',
    'PlanBrain',
    'PlannerChecks',
    'PlannerHost',
    'PlannerWindow',
    'Play',
    'Player',
    'Playlist',
    'PluginFloor',
    'PluginJson',
    'PluginMods',
    'Plugins',
    'PluginSettings',
    'Presence',
    'Probes',
    'Proof',
    'QuietRow',
    'Reactions',
    'Reminders',
    'Remote',
    'RemoteBrainKeys',
    'RemoteBrains',
    'RemoteCall',
    'RemoteChat',
    'RemoteCrypto',
    'RemoteLink',
    'RemoteLive',
    'RemoteQr',
    'RemoteScreen',
    'RemoteVectors',
    'Renew',
    'RepeatSurvey',
    'ResourceChecks',
    'Ribbons',
    'Room',
    'RoomHost',
    'RouteTest',
    'Sayings',
    'ScanWave',
    'Scenes',
    'Screensaver',
    'ScrollForMe',
    'SelfKnow',
    'SettingsFile',
    'SettingsTransfer',
    'Setup',
    'SimTime',
    'SkillMods',
    'SkinBands',
    'SkinMods',
    'SlimBar',
    'Slots',
    'SmokeRun',
    'Smooth',
    'SoundsLike',
    'SpoutSender',
    'Stamps',
    'StarterPlugins',
    'Startup',
    'Streak',
    'StreamArea',
    'StreamSafe',
    'StreamWords',
    'SysLines',
    'SystemUse',
    'Talk',
    'Tamagotchi',
    'TarWriter',
    'Them',
    'Tilt',
    'TipChecks',
    'ToolChecks',
    'ToolSections',
    'Topics',
    'Touch',
    'TreeEyes',
    'TreeEyesChecks',
    'TreeMods',
    'TreeSpeed',
    'Tucked',
    'Twitch',
    'TwitchPace',
    'TwitchViewers',
    'UninstallSelf',
    'Usage',
    'Viewers',
    'VirtualCamera',
    'Voice',
    'VoicePack',
    'VoiceProfiles',
    'VoiceTree',
    'WatchAlong',
    'Weather',
    'WeatherSettings',
    'Websites',
    'WorkApps',
    'Working',
    'Workspace',
    'WorkspaceLook',
    'WorkspaceParts',
    'WorkspaceWindow',
    'WorkspaceWords',
    'Zones'
) | ForEach-Object { Join-Path $root ('app\' + $_ + '.cs') }
# Any *.local.cs in app\ is compiled in when present; none are named here. A build without them is the ordinary one.
$local = @(Get-ChildItem -LiteralPath (Join-Path $root 'app') -Filter '*.local.cs' -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $_.FullName })
# Silent when there is nothing to compile in: an ordinary build says nothing about what it does not have.
$stable = @()
if ($deterministic) { $stable = @('/deterministic', '/langversion:5', ('/pathmap:' + $root + '=.')) }
# A test build is the number after version.txt with TEST on the end, like 0.9.434TEST; at the 0.9.1 placeholder every
# release looked newer. build-capri.ps1 stamps first, so only an unstamped build gets this, on a copy.
$info = Join-Path $root 'app\AssemblyInfo.cs'
$counter = Join-Path (Split-Path -Parent $root) 'version.txt'
$infoText = [IO.File]::ReadAllText($info)
if ($infoText -match 'AssemblyFileVersion\("0\.9\.1"\)' -and (Test-Path -LiteralPath $counter)) {
    $last = [regex]::Match([IO.File]::ReadAllText($counter), '(\d+)\.(\d+)\.(\d+)')
    if ($last.Success) {
        $patch = [int]$last.Groups[3].Value + 1
        $testVersion = $last.Groups[1].Value + '.' + $last.Groups[2].Value + '.' + $patch.ToString('000') + 'TEST'
        $stampedInfo = Join-Path $out 'AssemblyInfo.test.cs'
        [IO.File]::WriteAllText($stampedInfo, ($infoText -replace 'AssemblyFileVersion\("0\.9\.1"\)', ('AssemblyFileVersion("' + $testVersion + '")')), (New-Object Text.UTF8Encoding $false))
        $sources = @($sources | ForEach-Object { if ($_ -eq $info) { $stampedInfo } else { $_ } })
        Write-Output "Version  $testVersion"
    }
}
# Handed over in a response file: the sources and resources alone come near Windows' 32,767 character limit for a
# command line, and a build from a longer folder went past it.
$arguments = @($shared + $stable + $references + $resources + @('/unsafe+', ('/out:' + $app)) + $sources + $local)
$response = Join-Path $out 'csc-args.rsp'
[IO.File]::WriteAllLines($response, [string[]]($arguments | ForEach-Object { '"' + $_ + '"' }), (New-Object Text.UTF8Encoding $false))
& $compiler ('@' + $response)
$failed = $LASTEXITCODE -ne 0
Remove-Item -LiteralPath $response -Force -ErrorAction SilentlyContinue
if ($failed) { throw 'Capri compilation failed.' }
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

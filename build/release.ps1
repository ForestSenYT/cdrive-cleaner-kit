<#
.SYNOPSIS
Builds the release files into artifacts\release.

.DESCRIPTION
  CDriveCleaner-<version>-setup.exe     installer; carries the .NET runtime, installs for the current user by default
  CDriveCleaner-<version>-portable.exe  one file that runs without installing; carries the runtime and the rule file
  CDriveCleaner-signing.cer             the public certificate the files are signed with (only when signing)
  SHA256SUMS.txt                        checksums of the files above

The version comes from Directory.Build.props. Runs the tests first and stops on any failure.
Needs the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).

.PARAMETER Thumbprint
A code-signing certificate in Cert:\CurrentUser\My. Without it nothing is signed.

.EXAMPLE
.\build\release.ps1 -Thumbprint 7EB8C6545406E0F7AEC1E35CB27A18EFC9CA937D
#>
param([string] $Thumbprint = '')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\CDriveCleaner.App\CDriveCleaner.App.csproj'
$work = Join-Path $root 'artifacts\work'
$out = Join-Path $root 'artifacts\release'
$sign = Join-Path $PSScriptRoot 'sign.ps1'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Directory.Build.props 里没有 Version。' }

function Invoke-Native([string] $What, [scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What 失败（退出码 $LASTEXITCODE）。" }
}

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw '找不到 Inno Setup 6 的 ISCC.exe。安装：winget install JRSoftware.InnoSetup' }

Write-Output "== C 盘清理 $version"
Remove-Item $work, $out -Recurse -Force -ErrorAction SilentlyContinue

Invoke-Native 'tests' { dotnet test $root -c Release }
Invoke-Native 'publish (installer)' { dotnet publish $project -c Release -o "$work\app" --self-contained true }
Invoke-Native 'publish (portable)' {
    dotnet publish $project -c Release -o "$work\portable" --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
}

# Only our own files are signed; the .NET runtime files already carry Microsoft's signatures.
if ($Thumbprint) {
    & $sign -Thumbprint $Thumbprint "$work\app\CDriveCleaner.exe" "$work\app\CDriveCleaner.dll" "$work\app\CDriveCleaner.Core.dll" "$work\portable\CDriveCleaner.exe"
}

$isccArgs = @("/DAppVersion=$version", "/DSourceDir=$work\app", "/O$out", '/Qp')
if ($Thumbprint) {
    # Inno Setup signs the installer and the uninstaller it writes; $q is a quote and $f the file to sign.
    $isccArgs += '/DSign'
    $isccArgs += '/Scdsign=powershell.exe -NoProfile -ExecutionPolicy Bypass -File $q' + $sign + '$q -Thumbprint ' + $Thumbprint + ' $f'
}
$isccArgs += Join-Path $root 'installer\CDriveCleaner.iss'
Invoke-Native 'installer' { & $iscc @isccArgs }

Copy-Item "$work\portable\CDriveCleaner.exe" "$out\CDriveCleaner-$version-portable.exe"
if ($Thumbprint) {
    Export-Certificate -Cert "Cert:\CurrentUser\My\$Thumbprint" -FilePath "$out\CDriveCleaner-signing.cer" | Out-Null
}

$sums = Get-ChildItem $out -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllText("$out\SHA256SUMS.txt", (($sums -join "`n") + "`n"))

Write-Output "== $out"
Get-ChildItem $out -File | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }

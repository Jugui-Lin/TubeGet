param(
    [Parameter(Mandatory = $true)][string]$ProgramDirectory,
    [Parameter(Mandatory = $true)][string]$Compiler,
    [string]$OutputDirectory = '.',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$program = (Resolve-Path -LiteralPath $ProgramDirectory).Path
$programFile = Get-Item -LiteralPath (Join-Path $program 'TubeGet.exe')
$installerCompiler = (Resolve-Path -LiteralPath $Compiler).Path
$version = $programFile.VersionInfo
if ($version.ProductName -ne 'TubeGet' -or $version.FileVersion -notmatch '^\d+(?:\.\d+){3}$' -or
    ([Reflection.Assembly]::LoadFile($programFile.FullName).GetManifestResourceNames() -match '^TubeGet\.Bundle\.')) {
    throw 'Pass the built Windows program directory, with its main executable and components.'
}
foreach ($component in @('yt-dlp/yt-dlp.exe','ffmpeg/ffmpeg.exe','ffmpeg/ffprobe.exe','aria2/aria2c.exe','quickjs/qjs.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $program "components/$component") -PathType Leaf)) {
        throw "Missing component: $component"
    }
}

$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($Verify) {
    $report = Join-Path $output ('verify-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $report | Out-Null
    foreach ($check in @('--self-test', '--ui-smoke')) {
        $destination = if ($check -eq '--self-test') { Join-Path $report 'self-test.log' } else { Join-Path $report 'screenshots' }
        $test = Start-Process -FilePath $programFile.FullName -ArgumentList @($check, ('"{0}"' -f $destination)) -WindowStyle Hidden -PassThru -Wait
        if ($test.ExitCode -ne 0) { throw "TubeGet $check failed ($($test.ExitCode)). See $report" }
    }
    Write-Host "Verification reports: $report"
}
$script = Join-Path $PSScriptRoot '打包/TubeGet.iss'
& $installerCompiler "/O$output" "/DAppVersion=$($version.FileVersion)" "/DProgramDirectory=$program" $script
if ($LASTEXITCODE -ne 0) { throw "TubeGet installer build failed ($LASTEXITCODE)" }
$installer = Get-Item -LiteralPath (Join-Path $output 'TubeGet-Setup.exe')
if ($Verify) {
    if ($installer.VersionInfo.FileVersion.Trim() -ne $version.FileVersion.Trim()) { throw 'Installer and program versions differ.' }
    $hash = Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $installer.Name |
        Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
}
$installer

param(
    [Parameter(Mandatory = $true)][string]$ProgramDirectory,
    [Parameter(Mandatory = $true)][string]$Compiler,
    [string]$OutputDirectory = '.'
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
$script = Join-Path $PSScriptRoot '打包/TubeGet.iss'
& $installerCompiler "/O$output" "/DAppVersion=$($version.FileVersion)" "/DProgramDirectory=$program" $script
if ($LASTEXITCODE -ne 0) { throw "TubeGet installer build failed ($LASTEXITCODE)" }
Get-Item -LiteralPath (Join-Path $output 'TubeGet-Setup.exe')

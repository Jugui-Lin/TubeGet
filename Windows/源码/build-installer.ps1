param(
    [Parameter(Mandatory = $true)][string]$Launcher,
    [Parameter(Mandatory = $true)][string]$Compiler,
    [string]$OutputDirectory = '.'
)

$ErrorActionPreference = 'Stop'
$launcherFile = Get-Item -LiteralPath $Launcher
$installerCompiler = (Resolve-Path -LiteralPath $Compiler).Path
$version = $launcherFile.VersionInfo
if ($version.ProductName -ne 'TubeGet' -or $version.FileVersion -notmatch '^\d+(?:\.\d+){3}$') {
    throw 'The launcher must be a built TubeGet standalone executable.'
}
$resources = [Reflection.Assembly]::LoadFile($launcherFile.FullName).GetManifestResourceNames()
if (!($resources -match '^TubeGet\.Bundle\.[0-9a-f]{64}$')) {
    throw 'Run build-single.ps1 first and pass the resulting executable as -Launcher.'
}

$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$script = Join-Path $PSScriptRoot '打包/TubeGet.iss'
& $installerCompiler "/O$output" "/DAppVersion=$($version.FileVersion)" "/DLauncherPath=$($launcherFile.FullName)" $script
if ($LASTEXITCODE -ne 0) { throw "TubeGet installer build failed ($LASTEXITCODE)" }
Get-Item -LiteralPath (Join-Path $output 'TubeGet-Setup.exe')

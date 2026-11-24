param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
if (!$OutputPath) { $OutputPath = Join-Path (Split-Path -Parent $root) 'TubeGet-Standalone.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$appIcon = Join-Path $PSScriptRoot 'TubeGet.ico'
$launcher = Join-Path $PSScriptRoot '打包/BundleLauncher.cs'
$updater = Join-Path $PSScriptRoot '打包/AppUpdateInstall.cs'
$activation = Join-Path $PSScriptRoot 'WindowActivation.cs'
$versionInfo = Join-Path $PSScriptRoot 'VersionInfo.cs'
$payload = [IO.Path]::GetTempFileName()
$launcherExe = [IO.Path]::ChangeExtension($payload, '.exe')
try {
    $stream = [IO.File]::Open($payload, [IO.FileMode]::Create)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true, [Text.Encoding]::UTF8)
        try {
            $rootFiles = @('TubeGet.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','使用说明.md')
            foreach ($name in $rootFiles) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $name), $name) | Out-Null
            }
            $componentRoot = Join-Path $root 'components'
            foreach ($file in (Get-ChildItem -LiteralPath $componentRoot -File -Recurse | Sort-Object FullName)) {
                $name = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name) | Out-Null
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }

    $digest = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$launcherExe" "/win32icon:$appIcon" "/win32manifest:$PSScriptRoot/TubeGet.manifest" "/resource:$payload,TubeGet.Bundle.$digest" /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $launcher $updater $activation $versionInfo
    if ($LASTEXITCODE -ne 0) { throw "Standalone build failed ($LASTEXITCODE)" }
    Copy-Item -LiteralPath $launcherExe -Destination $OutputPath -Force
    Write-Output "Bundle SHA-256 $digest"
    Get-Item -LiteralPath $OutputPath
}
finally {
    Remove-Item -LiteralPath $payload,$launcherExe -Force -ErrorAction SilentlyContinue
}

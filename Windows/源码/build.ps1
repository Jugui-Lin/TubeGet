$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'TubeGet.exe'
$appIcon = Join-Path $PSScriptRoot 'TubeGet.ico'
$webViewCore = Join-Path $root 'Microsoft.Web.WebView2.Core.dll'
$webViewForms = Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll'
$files = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$output" "/win32icon:$appIcon" "/win32manifest:$PSScriptRoot/TubeGet.manifest" "/resource:$appIcon,TubeGet.AppIcon.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$webViewCore" "/reference:$webViewForms" @files
if ($LASTEXITCODE -ne 0) { throw "C# build failed ($LASTEXITCODE)" }

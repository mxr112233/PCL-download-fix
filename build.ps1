[CmdletBinding()]
param(
    [switch]$TestHooks
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src\Pcl299DownloadFix.cs'
$ui = Join-Path $root 'src\InteractiveUi.cs'
$manifest = Join-Path $root 'src\app.manifest'
$dist = Join-Path $root 'dist'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "未找到 .NET Framework 4.x C# 编译器：$csc"
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$outputName = if ($TestHooks) { 'PCL-2.99-Download-Fix.test.exe' } else { 'PCL-2.99-Download-Fix.exe' }
$output = Join-Path $dist $outputName
$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    '/utf8output',
    "/win32manifest:$manifest",
    "/out:$output",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    $src,
    $ui
)
if ($TestHooks) {
    $compilerArgs = @('/define:PCL299_TEST_HOOKS') + $compilerArgs
}

& $csc $compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

$item = Get-Item -LiteralPath $output
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
[pscustomobject]@{
    Path = $item.FullName
    Length = $item.Length
    SHA256 = $hash
    TestHooks = [bool]$TestHooks
} | Format-List
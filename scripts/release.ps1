[CmdletBinding()]
param(
    [string]$Version = 'v1.0.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'PCL-2.99-Download-Fix.exe'
$releaseDir = Join-Path $root 'release'
$stage = Join-Path $releaseDir ('.stage-' + [Guid]::NewGuid().ToString('N'))
$zipName = "PCL-2.99-Download-Fix-$Version.zip"
$zipPath = Join-Path $releaseDir $zipName
$hashPath = $zipPath + '.sha256'

if (-not $SkipBuild) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码：$LASTEXITCODE" }
}

if (-not (Test-Path -LiteralPath $exe)) { throw "找不到 Release EXE：$exe" }
$releaseText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($exe))
if ($releaseText.Contains('PCL299_TEST_')) { throw 'Release EXE 包含测试钩子，拒绝打包。' }

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $files = @(
        'PCL-2.99-Download-Fix.exe',
        'README.md',
        'LICENSE',
        'THIRD_PARTY_NOTICES.md'
    )
    Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'PCL-2.99-Download-Fix.exe')
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $stage

    if (Test-Path -LiteralPath $zipPath) { [IO.File]::Delete($zipPath) }
    if (Test-Path -LiteralPath $hashPath) { [IO.File]::Delete($hashPath) }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $actual = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    }
    finally {
        $archive.Dispose()
    }
    $expected = @($files | Sort-Object)
    if (($actual -join '|') -ne ($expected -join '|')) {
        throw "ZIP 内容不符合发布白名单。实际：$($actual -join ', ')"
    }

    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($hashPath, $hash + '  ' + $zipName + "`n", [Text.Encoding]::ASCII)
    [pscustomobject]@{
        Zip = $zipPath
        Bytes = (Get-Item -LiteralPath $zipPath).Length
        SHA256 = $hash
        HashFile = $hashPath
        Files = $actual -join ', '
    } | Format-List
}
finally {
    if (Test-Path -LiteralPath $stage) {
        [IO.Directory]::Delete((Resolve-Path -LiteralPath $stage).Path, $true)
    }
}

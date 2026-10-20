[CmdletBinding()]
param(
    [string]$OriginalPcl = (Join-Path $PSScriptRoot '..\..\Plain Craft Launcher 2.exe'),
    [string]$Tool = (Join-Path $PSScriptRoot '..\dist\PCL-2.99-Download-Fix.exe'),
    [string]$HookTool = (Join-Path $PSScriptRoot '..\dist\PCL-2.99-Download-Fix.test.exe')
)

$ErrorActionPreference = 'Stop'
$originalHash = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$patchedHash = '34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runRoot = Join-Path $PSScriptRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$passed = 0
$success = $false

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "断言失败：$Message" }
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) {
        throw "断言失败：$Message；实际=$Actual，预期=$Expected"
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Quote-Argument {
    param([string]$Value)
    if ($Value -match '[\s"]') {
        return '"' + $Value.Replace('"', '\"') + '"'
    }
    return $Value
}

function Invoke-Tool {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [hashtable]$Environment = @{}
    )

    $logId = [Guid]::NewGuid().ToString('N')
    $stdout = Join-Path $runRoot ($logId + '.out.txt')
    $stderr = Join-Path $runRoot ($logId + '.err.txt')
    $argumentLine = (($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
    $previous = @{}

    foreach ($key in $Environment.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], 'Process')
    }

    try {
        $process = Start-Process -FilePath $Executable -ArgumentList $argumentLine -Wait -PassThru `
            -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $code = $process.ExitCode
    }
    finally {
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
        }
    }

    $output = ''
    if (Test-Path -LiteralPath $stdout) { $output += Get-Content -Raw -LiteralPath $stdout }
    if (Test-Path -LiteralPath $stderr) { $output += Get-Content -Raw -LiteralPath $stderr }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function New-OriginalCopy {
    param([string]$Name)
    $directory = Join-Path $runRoot $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $target = Join-Path $directory 'Plain Craft Launcher 2.exe'
    Copy-Item -LiteralPath $OriginalPcl -Destination $target
    return $target
}

if (-not (Test-Path -LiteralPath $OriginalPcl)) { throw "找不到原版 PCL：$OriginalPcl" }
if (-not (Test-Path -LiteralPath $Tool)) { throw "找不到 Release 工具：$Tool" }
if (-not (Test-Path -LiteralPath $HookTool)) { throw "找不到测试钩子工具，请先运行 build.ps1 -TestHooks：$HookTool" }
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 C# 编译器：$csc" }
if ((Get-Sha256 $OriginalPcl) -ne $originalHash) { throw '作为测试输入的原版 PCL 哈希不匹配。' }
if (Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Plain Craft Launcher 2.exe' }) {
    throw '检测到 PCL 正在运行。请完全退出 PCL 和游戏后再运行自动化测试。'
}

New-Item -ItemType Directory -Path $runRoot | Out-Null
try {
    Write-Host '[1/11] --help 返回 0'
    $result = Invoke-Tool $Tool @('--help')
    Assert-Equal $result.ExitCode 0 '--help 退出码'
    Assert-True ($result.Output -match 'PCL 2.99 原版下载修复工具') '--help 应输出版本标题'
    $passed++

    Write-Host '[2/11] Release EXE 不含测试钩子'
    $releaseText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($Tool))
    Assert-True (-not $releaseText.Contains('PCL299_TEST_')) 'Release EXE 不应包含 PCL299_TEST_'
    $passed++

    Write-Host '[3/11] 未知哈希返回 2 且不创建备份'
    $unsupportedDir = Join-Path $runRoot 'unsupported'
    New-Item -ItemType Directory -Path $unsupportedDir | Out-Null
    $unsupported = Join-Path $unsupportedDir 'not-pcl.exe'
    [IO.File]::WriteAllBytes($unsupported, [byte[]](1, 2, 3, 4, 5))
    $beforeUnsupported = Get-Sha256 $unsupported
    $result = Invoke-Tool $Tool @('--pcl', $unsupported, '--no-launch')
    Assert-Equal $result.ExitCode 2 '未知哈希退出码'
    Assert-Equal (Get-Sha256 $unsupported) $beforeUnsupported '未知文件不应被修改'
    Assert-True (-not (Test-Path -LiteralPath ($unsupported + '.pcl-download-fix.bak'))) '未知文件不得创建备份'
    $passed++

    Write-Host '[4/11] 非法已有备份被拒绝且不覆盖'
    $invalidBackupTarget = New-OriginalCopy 'invalid-backup'
    $invalidBackup = $invalidBackupTarget + '.pcl-download-fix.bak'
    [IO.File]::WriteAllBytes($invalidBackup, [byte[]](9, 8, 7, 6))
    $targetBefore = Get-Sha256 $invalidBackupTarget
    $backupBefore = Get-Sha256 $invalidBackup
    $result = Invoke-Tool $Tool @('--pcl', $invalidBackupTarget, '--no-launch')
    Assert-Equal $result.ExitCode 3 '非法备份退出码'
    Assert-Equal (Get-Sha256 $invalidBackupTarget) $targetBefore '非法备份时目标不得被修改'
    Assert-Equal (Get-Sha256 $invalidBackup) $backupBefore '非法备份不得被覆盖'
    $passed++

    Write-Host '[5/11] PCL 运行中拒绝修改'
    $runningTarget = New-OriginalCopy 'running-refusal'
    $helperDir = Join-Path $runRoot 'running-helper'
    New-Item -ItemType Directory -Path $helperDir | Out-Null
    $helperSource = Join-Path $helperDir 'SleepHelper.cs'
    $helperExe = Join-Path $helperDir 'Plain Craft Launcher 2.exe'
    $helperCode = @(
        'using System;',
        'using System.Threading;',
        'internal static class Program',
        '{',
        '    [STAThread]',
        '    private static void Main()',
        '    {',
        '        Thread.Sleep(30000);',
        '    }',
        '}'
    )
    Set-Content -LiteralPath $helperSource -Value $helperCode -Encoding utf8
    & $csc /nologo /target:winexe "/out:$helperExe" $helperSource | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '无法编译运行中检测辅助程序。' }
    $helper = Start-Process -FilePath $helperExe -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Milliseconds 500
        $targetBefore = Get-Sha256 $runningTarget
        $result = Invoke-Tool $Tool @('--pcl', $runningTarget, '--no-launch')
        Assert-Equal $result.ExitCode 3 'PCL 运行中退出码'
        Assert-Equal (Get-Sha256 $runningTarget) $targetBefore 'PCL 运行中目标不得被修改'
        Assert-True (-not (Test-Path -LiteralPath ($runningTarget + '.pcl-download-fix.bak'))) 'PCL 运行中不得创建备份'
    }
    finally {
        if (-not $helper.HasExited) {
            $helper.Kill()
            $helper.WaitForExit()
        }
        $helper.Dispose()
    }
    $passed++

    Write-Host '[6/11] 首次修复生成正确补丁并创建备份'
    $mutableTarget = New-OriginalCopy 'mutation'
    $mutableBackup = $mutableTarget + '.pcl-download-fix.bak'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '首次修复退出码'
    Assert-Equal (Get-Sha256 $mutableTarget) $patchedHash '首次修复后的补丁哈希'
    Assert-Equal (Get-Sha256 $mutableBackup) $originalHash '首次修复创建的备份哈希'
    $passed++

    Write-Host '[7/11] 补丁版重复运行只校验，不二次修改'
    $patchedBefore = Get-Sha256 $mutableTarget
    $backupBefore = Get-Sha256 $mutableBackup
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '重复运行退出码'
    Assert-Equal (Get-Sha256 $mutableTarget) $patchedBefore '重复运行不得修改补丁版'
    Assert-Equal (Get-Sha256 $mutableBackup) $backupBefore '重复运行不得覆盖备份'
    $passed++

    Write-Host '[8/11] 合法已有备份不覆盖'
    $preservedTarget = New-OriginalCopy 'preserved-backup'
    $preservedBackup = $preservedTarget + '.pcl-download-fix.bak'
    Copy-Item -LiteralPath $OriginalPcl -Destination $preservedBackup
    $oldTimestamp = [DateTime]::SpecifyKind([DateTime]'2000-01-01T00:00:00', [DateTimeKind]::Utc)
    [IO.File]::SetLastWriteTimeUtc($preservedBackup, $oldTimestamp)
    $result = Invoke-Tool $Tool @('--pcl', $preservedTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '合法已有备份退出码'
    Assert-Equal (Get-Sha256 $preservedTarget) $patchedHash '合法已有备份时仍应修复'
    Assert-Equal (Get-Sha256 $preservedBackup) $originalHash '合法已有备份哈希应保持'
    $timestampDelta = [Math]::Abs(([IO.File]::GetLastWriteTimeUtc($preservedBackup) - $oldTimestamp).TotalSeconds)
    Assert-True ($timestampDelta -lt 1) '合法已有备份不得被重新复制'
    $passed++

    Write-Host '[9/11] --verify 与 --restore'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--verify')
    Assert-Equal $result.ExitCode 0 '补丁版 --verify 退出码'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--restore')
    Assert-Equal $result.ExitCode 0 '--restore 退出码'
    Assert-Equal (Get-Sha256 $mutableTarget) $originalHash '恢复后的原版哈希'
    Assert-Equal (Get-Sha256 $mutableBackup) $originalHash '恢复后备份保持不变'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--verify')
    Assert-Equal $result.ExitCode 0 '恢复后 --verify 退出码'
    $passed++

    Write-Host '[10/11] 启动失败自动回滚并返回 4'
    $rollbackTarget = New-OriginalCopy 'startup-rollback'
    $rollbackBackup = $rollbackTarget + '.pcl-download-fix.bak'
    $result = Invoke-Tool $HookTool @('--pcl', $rollbackTarget) @{ PCL299_TEST_STARTUP_FAILURE = '1' }
    Assert-Equal $result.ExitCode 4 '启动失败退出码'
    Assert-Equal (Get-Sha256 $rollbackTarget) $originalHash '启动失败后应恢复原版'
    Assert-Equal (Get-Sha256 $rollbackBackup) $originalHash '启动失败后备份应保持原版'
    $passed++

    Write-Host '[11/11] 恢复失败返回 5 且不损坏备份'
    $result = Invoke-Tool $Tool @('--pcl', $rollbackTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '恢复失败测试前修复退出码'
    Assert-Equal (Get-Sha256 $rollbackTarget) $patchedHash '恢复失败测试前补丁哈希'
    $lock = [IO.File]::Open($rollbackTarget, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $result = Invoke-Tool $Tool @('--pcl', $rollbackTarget, '--restore')
        Assert-Equal $result.ExitCode 5 '锁定文件恢复失败退出码'
    }
    finally {
        $lock.Dispose()
    }
    Assert-Equal (Get-Sha256 $rollbackTarget) $patchedHash '恢复失败后目标应保持补丁版'
    Assert-Equal (Get-Sha256 $rollbackBackup) $originalHash '恢复失败后备份不得损坏'
    $passed++

    $success = $true
    Write-Host "全部通过：$passed 项测试。"
}
finally {
    if ($success -and (Test-Path -LiteralPath $runRoot)) {
        [IO.Directory]::Delete((Resolve-Path -LiteralPath $runRoot).Path, $true)
        Write-Host "已清理测试目录：$runRoot"
    }
    elseif (Test-Path -LiteralPath $runRoot) {
        Write-Warning "测试未全部通过，临时目录保留用于诊断：$runRoot"
    }
}

if (-not $success) { exit 1 }
exit 0


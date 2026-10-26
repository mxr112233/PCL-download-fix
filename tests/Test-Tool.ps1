[CmdletBinding()]
param(
    [string]$OriginalPcl = (Join-Path $PSScriptRoot '..\..\Plain Craft Launcher 2.exe'),
    [string]$Tool = (Join-Path $PSScriptRoot '..\dist\PCL-2.99-Download-Fix.exe'),
    [string]$HookTool = (Join-Path $PSScriptRoot '..\dist\PCL-2.99-Download-Fix.test.exe')
)

$ErrorActionPreference = 'Stop'
$originalHash = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$downloadPatchedHash = '34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7'
$fullyPatchedV110Hash = 'AFC49BCD281D365D01D17C349C559DDB344DB00D9E403FFE088A47F83D88B9BB'
$fullyPatchedHash = '34F774B3A5936905DC7C033B502B980685B9EAE74D2E0D705DB459E376FC7DCB'
$modpackBody495Hex = '13300A00820000000C000011720F3F03701614178D7001001B2516198D5B0100012516726A480070A2251772F35C0170A2251872E55C0170A272FB5C017073B106000AA47001001B28B206000A73DF02000A28E002000A287200002BFE0E0000FE0C0000287E00000A3A1F00000073ED12000625FE0C00007D33080004FE06EE120006730F01000A28471100062A'
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

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Pcl299WindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static string[] GetTitles(int processId)
    {
        List<string> titles = new List<string>();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                StringBuilder text = new StringBuilder(512);
                GetWindowText(hWnd, text, text.Capacity);
                if (text.Length > 0) titles.Add(text.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }
}
'@

function Invoke-GuiTool {
    param(
        [string]$Executable,
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 20
    )

    $previous = @{}
    foreach ($key in $Environment.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], 'Process')
    }

    $titles = New-Object 'System.Collections.Generic.HashSet[string]'
    $process = $null
    try {
        $process = Start-Process -FilePath $Executable -WorkingDirectory $WorkingDirectory -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
            foreach ($title in [Pcl299WindowProbe]::GetTitles($process.Id)) {
                [void]$titles.Add($title)
            }
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        }
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
            throw "GUI 工具在 $TimeoutSeconds 秒内未退出。"
        }
        foreach ($title in [Pcl299WindowProbe]::GetTitles($process.Id)) {
            [void]$titles.Add($title)
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Titles = @($titles) }
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
        }
    }
}

function New-OriginalCopy {
    param([string]$Name)
    $directory = Join-Path $runRoot $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $target = Join-Path $directory 'Plain Craft Launcher 2.exe'
    Copy-Item -LiteralPath $OriginalPcl -Destination $target
    return $target
}
function New-DownloadPatchedCopy {
    param([string]$Name)
    $target = New-OriginalCopy $Name
    $bytes = [IO.File]::ReadAllBytes($target)
    $hex = '02281205000602026F07060006026F05060006026F51050006147E0F04000402FE061C060006738D05000A141728CA00002B2A'
    $patch = New-Object byte[] ($hex.Length / 2)
    for ($i = 0; $i -lt $patch.Length; $i++) {
        $patch[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16)
    }
    [Array]::Copy([BitConverter]::GetBytes([int]51), 0, $bytes, 0x2C0B8, 4)
    [Array]::Copy($patch, 0, $bytes, 0x2C0C0, $patch.Length)
    for ($i = 0x2C0C0 + $patch.Length; $i -lt 0x2C0C0 + 94; $i++) {
        $bytes[$i] = 0
    }
    [IO.File]::WriteAllBytes($target, $bytes)
    if ((Get-Sha256 $target) -ne $downloadPatchedHash) {
        throw '构造 v1.0 下载补丁版失败。'
    }
    return $target
}

function Get-PeLayout {
    param([byte[]]$Bytes)
    $peOffset = [BitConverter]::ToInt32($Bytes, 0x3C)
    $optionalOffset = $peOffset + 24
    $optionalSize = [BitConverter]::ToUInt16($Bytes, $peOffset + 20)
    return [pscustomobject]@{
        PeOffset = $peOffset
        OptionalOffset = $optionalOffset
        SectionTable = $optionalOffset + $optionalSize
        SectionCount = [BitConverter]::ToUInt16($Bytes, $peOffset + 6)
        SectionAlignment = [BitConverter]::ToInt32($Bytes, $optionalOffset + 32)
        FileAlignment = [BitConverter]::ToInt32($Bytes, $optionalOffset + 36)
    }
}

function Get-FileOffsetFromRva {
    param([byte[]]$Bytes, [uint32]$Rva)
    $layout = Get-PeLayout $Bytes
    for ($i = 0; $i -lt $layout.SectionCount; $i++) {
        $header = $layout.SectionTable + $i * 40
        $virtualSize = [BitConverter]::ToUInt32($Bytes, $header + 8)
        $virtualAddress = [BitConverter]::ToUInt32($Bytes, $header + 12)
        $rawSize = [BitConverter]::ToUInt32($Bytes, $header + 16)
        $rawPointer = [BitConverter]::ToUInt32($Bytes, $header + 20)
        $mappedSize = if ($virtualSize -eq 0) { $rawSize } else { $virtualSize }
        if ($Rva -ge $virtualAddress -and $Rva -lt ($virtualAddress + $mappedSize)) {
            return [int]($rawPointer + ($Rva - $virtualAddress))
        }
    }
    throw "无法把 RVA $Rva 映射为文件偏移。"
}

function Get-MethodRva {
    param([byte[]]$Bytes, [int]$Rid, [bool]$ExpandedHeader)
    $headerAdjustment = if ($ExpandedHeader) { 0x200 } else { 0 }
    $row = 0x0AFD46 + $headerAdjustment + ($Rid - 495) * 0x12
    return [BitConverter]::ToUInt32($Bytes, $row)
}

function Get-FolderScanOpcodeOffset {
    param([byte[]]$Bytes, [bool]$ExpandedHeader)
    $methodRva = Get-MethodRva $Bytes 5887 $ExpandedHeader
    $methodOffset = Get-FileOffsetFromRva $Bytes $methodRva
    if (($Bytes[$methodOffset] -band 0x03) -ne 0x03) {
        throw '版本文件夹扫描方法不是 Fat 方法头。'
    }
    $headerSize = ($Bytes[$methodOffset + 1] -shr 4) * 4
    return $methodOffset + $headerSize + 0x6A
}

function Write-UInt32 {
    param([byte[]]$Bytes, [int]$Offset, [uint32]$Value)
    [Array]::Copy([BitConverter]::GetBytes($Value), 0, $Bytes, $Offset, 4)
}

function Assert-FullPatchLayout {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $layout = Get-PeLayout $bytes
    Assert-Equal $layout.SectionCount 4 '完整补丁版应包含 4 个 PE 节'
    $fixHeader = $layout.SectionTable + 3 * 40
    $fixRva = [BitConverter]::ToUInt32($bytes, $fixHeader + 12)
    $fixVirtualSize = [BitConverter]::ToUInt32($bytes, $fixHeader + 8)
    Assert-Equal $fixVirtualSize 0x4E0 'v1.2 .pclfix 虚拟大小'
    Assert-Equal (Get-MethodRva $bytes 496 $true) $fixRva '拖入整合包入口 RID 496 RVA'
    Assert-Equal (Get-MethodRva $bytes 495 $true) ($fixRva + 0x450) '点击导入整合包入口 RID 495 RVA'
    $bodyOffset = Get-FileOffsetFromRva $bytes (Get-MethodRva $bytes 495 $true)
    $bodyBytes = New-Object byte[] ($modpackBody495Hex.Length / 2)
    for ($i = 0; $i -lt $bodyBytes.Length; $i++) {
        $bodyBytes[$i] = [Convert]::ToByte($modpackBody495Hex.Substring($i * 2, 2), 16)
    }
    for ($i = 0; $i -lt $bodyBytes.Length; $i++) {
        Assert-Equal $bytes[$bodyOffset + $i] $bodyBytes[$i] "RID 495 方法体字节 $i"
    }
    Assert-Equal $bytes[(Get-FolderScanOpcodeOffset $bytes $true)] 0x16 '版本文件夹扫描参数应为 false'
}

function New-FullyPatchedV110Copy {
    param([string]$Name)
    $target = New-OriginalCopy $Name
    $result = Invoke-Tool $Tool @('--pcl', $target, '--no-launch')
    if ($result.ExitCode -ne 0) {
        throw "构造 v1.1 测试输入时修复 v1.2 失败，退出码 $($result.ExitCode)：$($result.Output)"
    }
    if ((Get-Sha256 $target) -ne $fullyPatchedHash) {
        throw '构造 v1.1 测试输入时 v1.2 文件哈希不正确。'
    }

    $bytes = [IO.File]::ReadAllBytes($target)
    $layout = Get-PeLayout $bytes
    $fixHeader = $layout.SectionTable + 3 * 40
    $fixRva = [BitConverter]::ToUInt32($bytes, $fixHeader + 12)
    $fixRawPointer = [BitConverter]::ToInt32($bytes, $fixHeader + 20)
    [Array]::Clear($bytes, $fixRawPointer + 0x44A, 0x4E0 - 0x44A)
    Write-UInt32 $bytes ($fixHeader + 8) 0x44A
    Write-UInt32 $bytes (0x0AFD46 + 0x200) ($fixRva + 0x3D4)
    $bytes[(Get-FolderScanOpcodeOffset $bytes $true)] = 0x17
    [IO.File]::WriteAllBytes($target, $bytes)
    if ((Get-Sha256 $target) -ne $fullyPatchedV110Hash) {
        throw '构造 v1.1 测试输入失败，哈希不正确。'
    }
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
    Write-Host '[1/19] --help 返回 0'
    $result = Invoke-Tool $Tool @('--help')
    Assert-Equal $result.ExitCode 0 '--help 退出码'
    Assert-True ($result.Output -match 'PCL 2.99 下载与整合包修复工具') '--help 应输出版本标题'
    $passed++

    Write-Host '[2/19] Release EXE 不含测试钩子'
    $releaseText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($Tool))
    Assert-True (-not $releaseText.Contains('PCL299_TEST_')) 'Release EXE 不应包含 PCL299_TEST_'
    $passed++

    Write-Host '[3/19] 未知哈希返回 2 且不创建备份'
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

    Write-Host '[4/19] 非法已有备份被拒绝且不覆盖'
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

    Write-Host '[5/19] PCL 运行中拒绝修改'
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

    Write-Host '[6/19] 原版首次修复生成完整补丁并创建备份'
    $mutableTarget = New-OriginalCopy 'mutation'
    $mutableBackup = $mutableTarget + '.pcl-download-fix.bak'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '首次修复退出码'
    Assert-Equal (Get-Sha256 $mutableTarget) $fullyPatchedHash '首次修复后的完整补丁哈希'
    Assert-Equal (Get-Sha256 $mutableBackup) $originalHash '首次修复创建的备份哈希'
    Assert-FullPatchLayout $mutableTarget
    $passed++

    Write-Host '[7/19] v1.2 完整补丁版重复运行只校验，不二次修改'
    $patchedBefore = Get-Sha256 $mutableTarget
    $backupBefore = Get-Sha256 $mutableBackup
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '重复运行退出码'
    Assert-Equal (Get-Sha256 $mutableTarget) $patchedBefore '重复运行不得修改完整补丁版'
    Assert-Equal (Get-Sha256 $mutableBackup) $backupBefore '重复运行不得覆盖备份'
    $passed++

    Write-Host '[8/19] v1.0 下载补丁版可升级为 v1.2 且不伪造备份'
    $downloadTarget = New-DownloadPatchedCopy 'download-mutation'
    $result = Invoke-Tool $Tool @('--pcl', $downloadTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 'v1.0 迁移退出码'
    Assert-Equal (Get-Sha256 $downloadTarget) $fullyPatchedHash 'v1.0 迁移后的完整补丁哈希'
    Assert-True (-not (Test-Path -LiteralPath ($downloadTarget + '.pcl-download-fix.bak'))) '无原版文件时不得伪造备份'
    $passed++

    Write-Host '[9/19] v1.0 迁移时合法已有备份不覆盖'
    $preservedTarget = New-DownloadPatchedCopy 'preserved-backup'
    $preservedBackup = $preservedTarget + '.pcl-download-fix.bak'
    Copy-Item -LiteralPath $OriginalPcl -Destination $preservedBackup
    $oldTimestamp = [DateTime]::SpecifyKind([DateTime]'2000-01-01T00:00:00', [DateTimeKind]::Utc)
    [IO.File]::SetLastWriteTimeUtc($preservedBackup, $oldTimestamp)
    $result = Invoke-Tool $Tool @('--pcl', $preservedTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '合法已有备份退出码'
    Assert-Equal (Get-Sha256 $preservedTarget) $fullyPatchedHash '合法已有备份时仍应完成迁移'
    Assert-Equal (Get-Sha256 $preservedBackup) $originalHash '合法已有备份哈希应保持'
    $timestampDelta = [Math]::Abs(([IO.File]::GetLastWriteTimeUtc($preservedBackup) - $oldTimestamp).TotalSeconds)
    Assert-True ($timestampDelta -lt 1) '合法已有备份不得被重新复制'
    $passed++

    Write-Host '[10/19] --verify 正确识别原版、v1.0、v1.1 和 v1.2'
    $verifyOriginal = New-OriginalCopy 'verify-original'
    $result = Invoke-Tool $Tool @('--pcl', $verifyOriginal, '--verify')
    Assert-Equal $result.ExitCode 0 '原版 --verify 退出码'
    Assert-True ($result.Output -match '原版 PCL 2\.99\.0\.0') '--verify 应识别原版'

    $verifyDownload = New-DownloadPatchedCopy 'verify-download'
    $result = Invoke-Tool $Tool @('--pcl', $verifyDownload, '--verify')
    Assert-Equal $result.ExitCode 0 'v1.0 --verify 退出码'
    Assert-True ($result.Output -match 'v1\.0 下载入口修复版') '--verify 应识别 v1.0 下载补丁版'

    $verifyV110 = New-FullyPatchedV110Copy 'verify-v110'
    $result = Invoke-Tool $Tool @('--pcl', $verifyV110, '--verify')
    Assert-Equal $result.ExitCode 0 'v1.1 --verify 退出码'
    Assert-True ($result.Output -match 'v1\.1 整合包拖入修复版') '--verify 应识别 v1.1 修复版'

    $verifyFullDir = Join-Path $runRoot 'verify-full'
    New-Item -ItemType Directory -Path $verifyFullDir | Out-Null
    $verifyFull = Join-Path $verifyFullDir 'Plain Craft Launcher 2.exe'
    Copy-Item -LiteralPath $mutableTarget -Destination $verifyFull
    $result = Invoke-Tool $Tool @('--pcl', $verifyFull, '--verify')
    Assert-Equal $result.ExitCode 0 'v1.2 --verify 退出码'
    Assert-True ($result.Output -match 'v1\.2 下载、整合包与版本文件夹完整修复版') '--verify 应识别 v1.2 修复版'
    $passed++

    Write-Host '[11/19] --restore 从完整修复版恢复原版并再次校验'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--restore')
    Assert-Equal $result.ExitCode 0 '--restore 退出码'
    Assert-Equal (Get-Sha256 $mutableTarget) $originalHash '恢复后的原版哈希'
    Assert-Equal (Get-Sha256 $mutableBackup) $originalHash '恢复后备份保持不变'
    $result = Invoke-Tool $Tool @('--pcl', $mutableTarget, '--verify')
    Assert-Equal $result.ExitCode 0 '恢复后 --verify 退出码'
    Assert-True ($result.Output -match '原版 PCL 2\.99\.0\.0') '恢复后 --verify 应识别原版'
    $passed++

    Write-Host '[12/19] 原版启动失败自动回滚并返回 4'
    $rollbackTarget = New-OriginalCopy 'startup-rollback'
    $rollbackBackup = $rollbackTarget + '.pcl-download-fix.bak'
    $result = Invoke-Tool $HookTool @('--pcl', $rollbackTarget) @{ PCL299_TEST_STARTUP_FAILURE = '1' }
    Assert-Equal $result.ExitCode 4 '原版启动失败退出码'
    Assert-Equal (Get-Sha256 $rollbackTarget) $originalHash '原版启动失败后应恢复原版'
    Assert-Equal (Get-Sha256 $rollbackBackup) $originalHash '原版启动失败后备份应保持原版'
    $passed++

    Write-Host '[13/19] v1.0 启动失败回滚到 v1.0 且返回 4'
    $downloadRollbackTarget = New-DownloadPatchedCopy 'download-startup-rollback'
    $result = Invoke-Tool $HookTool @('--pcl', $downloadRollbackTarget) @{ PCL299_TEST_STARTUP_FAILURE = '1' }
    Assert-Equal $result.ExitCode 4 'v1.0 启动失败退出码'
    Assert-Equal (Get-Sha256 $downloadRollbackTarget) $downloadPatchedHash 'v1.0 启动失败后应恢复原状态'
    Assert-True (-not (Test-Path -LiteralPath ($downloadRollbackTarget + '.pcl-download-fix.bak'))) 'v1.0 回滚不得创建伪造备份'
    $passed++

    Write-Host '[14/19] v1.1 原地升级为 v1.2 并保留原版备份'
    $v110Target = New-FullyPatchedV110Copy 'v110-mutation'
    $v110Backup = $v110Target + '.pcl-download-fix.bak'
    $result = Invoke-Tool $Tool @('--pcl', $v110Target, '--no-launch')
    Assert-Equal $result.ExitCode 0 'v1.1 原地升级退出码'
    Assert-Equal (Get-Sha256 $v110Target) $fullyPatchedHash 'v1.1 原地升级后的 v1.2 哈希'
    Assert-Equal (Get-Sha256 $v110Backup) $originalHash 'v1.1 升级时原版备份应保持'
    Assert-True ($result.Output -match '原地升级到 v1\.2') '日志应说明执行了 v1.1 原地升级'
    Assert-FullPatchLayout $v110Target
    $passed++

    Write-Host '[15/19] v1.1 启动失败回滚到 v1.1 且返回 4'
    $v110RollbackTarget = New-FullyPatchedV110Copy 'v110-startup-rollback'
    $v110RollbackBackup = $v110RollbackTarget + '.pcl-download-fix.bak'
    $result = Invoke-Tool $HookTool @('--pcl', $v110RollbackTarget) @{ PCL299_TEST_STARTUP_FAILURE = '1' }
    Assert-Equal $result.ExitCode 4 'v1.1 启动失败退出码'
    Assert-Equal (Get-Sha256 $v110RollbackTarget) $fullyPatchedV110Hash 'v1.1 启动失败后应恢复 v1.1'
    Assert-Equal (Get-Sha256 $v110RollbackBackup) $originalHash 'v1.1 回滚时原版备份应保持'
    $passed++

    Write-Host '[16/19] 新日志中的原版下载列表记录可通过非零退出码启动检查'
    $evidenceTarget = New-OriginalCopy 'startup-evidence'
    $evidenceBackup = $evidenceTarget + '.pcl-download-fix.bak'
    $result = Invoke-Tool $HookTool @('--pcl', $evidenceTarget) @{
        PCL299_TEST_LAUNCH_EVIDENCE = 'loader'
        PCL299_TEST_LAUNCH_EXIT_CODE = '-1'
    }
    Assert-Equal $result.ExitCode 0 '有启动证据时不应误判退出码 -1'
    Assert-Equal (Get-Sha256 $evidenceTarget) $fullyPatchedHash '有启动证据时应保留完整修复版'
    Assert-Equal (Get-Sha256 $evidenceBackup) $originalHash '有启动证据时备份应保持原版'
    $passed++

    Write-Host '[17/19] GUI 自动定位、完整修复并显示成功弹窗'
    $guiSuccessDir = Join-Path $runRoot 'gui-success'
    New-Item -ItemType Directory -Path $guiSuccessDir | Out-Null
    $guiTarget = Join-Path $guiSuccessDir 'Plain Craft Launcher 2.exe'
    Copy-Item -LiteralPath $OriginalPcl -Destination $guiTarget
    $guiResult = Invoke-GuiTool $HookTool $guiSuccessDir @{
        PCL299_TEST_GUI_NO_LAUNCH = '1'
        PCL299_TEST_GUI_AUTOCLOSE_MS = '3000'
    }
    Assert-Equal $guiResult.ExitCode 0 'GUI 成功流程退出码'
    Assert-True ($guiResult.Titles -contains '修复完成') 'GUI 成功后应显示“修复完成”弹窗'
    Assert-Equal (Get-Sha256 $guiTarget) $fullyPatchedHash 'GUI 自动修复后的完整补丁哈希'
    Assert-Equal (Get-Sha256 ($guiTarget + '.pcl-download-fix.bak')) $originalHash 'GUI 自动修复创建的备份哈希'
    $passed++

    Write-Host '[18/19] GUI 修复失败弹窗、返回码和诊断日志'
    $guiFailureDir = Join-Path $runRoot 'gui-failure'
    New-Item -ItemType Directory -Path $guiFailureDir | Out-Null
    $guiFailureTarget = Join-Path $guiFailureDir 'Plain Craft Launcher 2.exe'
    [IO.File]::WriteAllBytes($guiFailureTarget, [byte[]](1, 2, 3, 4, 5))
    $failureBefore = Get-Sha256 $guiFailureTarget
    $logPath = Join-Path ([IO.Path]::GetTempPath()) 'PCL-2.99-Download-Fix-last.log'
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
    $guiResult = Invoke-GuiTool $HookTool $guiFailureDir @{
        PCL299_TEST_GUI_PCL_PATH = $guiFailureTarget
        PCL299_TEST_GUI_AUTOCLOSE_MS = '3000'
    }
    Assert-Equal $guiResult.ExitCode 2 'GUI 失败流程退出码'
    Assert-True ($guiResult.Titles -contains '修复失败') 'GUI 失败后应显示“修复失败”弹窗'
    Assert-Equal (Get-Sha256 $guiFailureTarget) $failureBefore 'GUI 失败时不得修改文件'
    Assert-True (Test-Path -LiteralPath $logPath) 'GUI 失败时应写入诊断日志'
    $logText = [IO.File]::ReadAllText($logPath, [Text.Encoding]::UTF8)
    Assert-True ($logText -match '返回码：2') '诊断日志应包含返回码 2'
    Assert-True ($logText -match ('实际 SHA256：' + $failureBefore)) '诊断日志应包含实际 SHA256'
    $passed++

    Write-Host '[19/19] 恢复失败返回 5 且不损坏备份'
    $result = Invoke-Tool $Tool @('--pcl', $rollbackTarget, '--no-launch')
    Assert-Equal $result.ExitCode 0 '恢复失败测试前修复退出码'
    Assert-Equal (Get-Sha256 $rollbackTarget) $fullyPatchedHash '恢复失败测试前完整补丁哈希'
    $lock = [IO.File]::Open($rollbackTarget, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $result = Invoke-Tool $Tool @('--pcl', $rollbackTarget, '--restore')
        Assert-Equal $result.ExitCode 5 '锁定文件恢复失败退出码'
    }
    finally {
        $lock.Dispose()
    }
    Assert-Equal (Get-Sha256 $rollbackTarget) $fullyPatchedHash '恢复失败后目标应保持完整补丁版'
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


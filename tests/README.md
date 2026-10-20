# 自动化测试

## 前提

- Windows 10/11。
- PCL 完全退出，且没有同名的 `Plain Craft Launcher 2.exe` 进程。
- 已安装 .NET Framework 4.x C# 编译器（`csc.exe`）。
- 测试输入为受支持的 PCL 原版，哈希必须为：

```text
6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

## 运行

从仓库根目录执行：

```powershell
.\build.ps1
.\build.ps1 -TestHooks
.\tests\Test-Tool.ps1
```

测试会复制原版 PCL 到 `tests/run-<随机ID>`，只修改测试副本。全部通过后会自动删除临时目录；失败时保留目录用于诊断。

## 覆盖范围

1. `--help` 返回 0。
2. Release EXE 不包含 `PCL299_TEST_*` 测试钩子。
3. 未知哈希返回 2，不修改文件且不创建备份。
4. 非法已有备份返回 3，目标与备份都不被改动。
5. PCL 运行中返回 3，拒绝修改。
6. 原版首次修复生成的补丁哈希和备份哈希准确。
7. 补丁版重复运行只校验，不二次修改。
8. 合法已有备份不覆盖。
9. `--verify` 与 `--restore` 正常工作。
10. 使用测试钩子模拟启动失败时返回 4，并自动恢复原版。
11. 目标文件被锁定时 `--restore` 返回 5，备份不损坏。

真实下载与启动验收记录见 [../analysis/evidence-summary.md](../analysis/evidence-summary.md)。

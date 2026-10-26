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

如果原版只保留在其他位置，可显式指定：

```powershell
.\tests\Test-Tool.ps1 -OriginalPcl ".\work\full\official.exe"
```

测试会复制原版 PCL 到 `tests/run-<随机ID>`，只修改测试副本。全部通过后会自动删除临时目录；失败时保留目录用于诊断。

## 覆盖范围

1. `--help` 返回 0 并显示工具名称。
2. Release EXE 不包含 `PCL299_TEST_*` 测试钩子。
3. 未知哈希返回 2，不修改文件且不创建备份。
4. 非法已有备份返回 3，目标与备份都不被改动。
5. PCL 运行中返回 3，拒绝修改。
6. 原版首次修复生成 v1.2 完整修复版，哈希和备份哈希准确。
7. v1.2 完整修复版重复运行只校验，不二次修改。
8. v1.0 下载补丁版可升级为 v1.2，且不伪造原版备份。
9. v1.0 迁移时合法已有备份不覆盖。
10. `--verify` 正确识别原版、v1.0、v1.1 和 v1.2。
11. `--restore` 从完整修复版恢复原版并再次校验。
12. 原版启动失败时返回 4，并自动恢复原版。
13. v1.0 启动失败时返回 4，并自动恢复 v1.0。
14. v1.1 原地升级为 v1.2，并保留原版备份。
15. v1.1 启动失败时返回 4，并自动恢复 v1.1。
16. 新日志中出现原版下载列表加载记录时，即使 PCL 退出码为 -1 也不误回滚。
17. GUI 自动定位、完整修复并显示“修复完成”弹窗。
18. GUI 失败时显示“修复失败”弹窗，返回码为 2，诊断日志包含实际 SHA256。
19. 目标文件被锁定时 `--restore` 返回 5，备份不损坏。

布局测试还会确认：

- `.pclfix` 的 VirtualSize 为 `0x4E0`；
- RID 495 指向 `.pclfix + 0x450`，方法体可解析；
- RID 496 仍指向 `.pclfix` 节起始；
- RID 5887 的目录扫描参数已修正为 `0`。

真实功能验收记录见 [../analysis/evidence-summary.md](../analysis/evidence-summary.md)。

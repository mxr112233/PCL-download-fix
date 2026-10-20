# 原版游戏下载恢复工具（第一版）

> **非官方第三方修复。** 本项目与 Plain Craft Launcher、Mojang、Microsoft 均无隶属或授权关系。
>
> **开发方式声明：本项目完全由 Vibecoding 完成。**
>
> 协作署名：DeepSeek。

## 说明

本工具用于恢复 Plain Craft Launcher 2 `2.99.0.0 (413)` 中被禁用的原版 Minecraft 下载入口。仓库和 Release 不包含 PCL 本体、Minecraft 游戏文件、账户信息或存档。

## 本次版本

- 双击后自动定位 PCL、自动校验、自动备份、自动修复并启动。
- 蓝白简洁界面，接近 PCL 的视觉风格。
- 修复过程显示步骤、百分比和详细日志。
- 成功后弹出“修复完成”窗口。
- 失败后弹出“修复失败”窗口，显示返回码、错误原因、实际版本与 SHA256。
- 失败窗口支持复制信息、打开诊断日志和重新尝试。
- 启动判定会检查 PCL 新日志中的原版下载列表加载记录，正常关闭返回 -1 不再误触发回滚。
- 保留 `--verify`、`--restore`、`--no-launch`、`--pcl` 和 `--help`。

## 使用

1. 完全退出 PCL 和正在运行的游戏。
2. 解压发布包，双击 `PCL-2.99-Download-Fix.exe`。
3. 等待自动处理和成功弹窗。
4. 在 PCL 中进入“下载 → 原版游戏”。

首次修复会创建固定备份：

```text
Plain Craft Launcher 2.exe.pcl-download-fix.bak
```

恢复命令：

```powershell
.\PCL-2.99-Download-Fix.exe --restore
```

## 失败反馈

失败弹窗会显示返回码和可复制的诊断信息。最近一次日志保存在：

```text
%TEMP%\PCL-2.99-Download-Fix-last.log
```

返回码：`0` 成功；`2` 不支持的文件；`3` 文件或备份错误；`4` 启动失败并已回滚；`5` 恢复失败。

## 适用范围

- PCL `2.99.0.0 (413)`
- 原版 SHA256：`6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386`
- 修复版 SHA256：`34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7`

未知版本或哈希会被拒绝。

## 验证

已完成静态复核、隔离环境功能检查及工具异常与恢复流程测试。14 项自动化测试全部通过。

发布包仅包含 EXE、README、LICENSE 和第三方声明。ZIP SHA256：

`0C527E62006A6D15DA462D89E54C57820B99E5D430B0DE973B508C5145B69020`

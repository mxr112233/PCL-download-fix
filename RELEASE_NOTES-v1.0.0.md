# PCL 2.99 原版下载修复 v1.0.0

> **非官方第三方修复。** 本项目与 Plain Craft Launcher、Mojang、Microsoft 均无隶属或授权关系。
>
> **开发方式声明：本项目完全由 Vibecoding 完成。**
>
> 协作署名：DeepSeek。

## 说明

本工具用于恢复 Plain Craft Launcher 2 `2.99.0.0 (413)` 中被禁用的原版 Minecraft 下载入口。仓库和 Release 不包含 PCL 本体、Minecraft 游戏文件、账户信息或存档。

## 适用范围

- PCL `2.99.0.0 (413)`
- 原版 SHA256：`6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386`
- 修复版 SHA256：`34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7`

未知版本或哈希会被拒绝。

## 使用

双击 `PCL-2.99-Download-Fix.exe` 即可校验、备份、修复并启动 PCL。首次修复会在 PCL 同目录创建：

`Plain Craft Launcher 2.exe.pcl-download-fix.bak`

已有合法备份不会被覆盖。恢复命令：

```powershell
.\PCL-2.99-Download-Fix.exe --restore
```

## 退出码

- `0`：成功
- `2`：不支持的文件
- `3`：文件或备份错误
- `4`：启动失败并已回滚
- `5`：恢复失败

## 验证

已对目标方法完成静态复核，并在隔离环境验证原版下载、游戏启动、PCL 下载界面及工具异常路径和恢复流程。验证过程未改动现有游戏目录、账户或存档。

发布包仅包含 EXE、README、LICENSE 和第三方声明。ZIP SHA256：

`A0710923CD400B3BB2289184BE5C9942BBD97E8E0989AD3F024159A701DA6147`

详细说明、源码、构建和测试步骤见仓库 README 与 `analysis/evidence-summary.md`。

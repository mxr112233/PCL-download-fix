# PCL 2.99 原版下载修复

> **非官方第三方修复。** 本项目与 Plain Craft Launcher、Mojang、Microsoft 均无隶属或授权关系。

本项目用于恢复 **Plain Craft Launcher 2 `2.99.0.0 (413)`** 中被禁用的原版 Minecraft 下载入口。修复通过静态分析定位 `PCL.PageDownloadInstall.LoaderInit`，只恢复该方法的正常控件初始化逻辑，不修改其他下载模块、PCL 元数据或用户游戏数据。

## 支持范围

工具只接受以下两个完整文件之一，其他版本或哈希一律拒绝：

| 状态 | 文件版本 | 文件大小 | SHA256 |
| --- | --- | ---: | --- |
| 原版 | `2.99.0.0` | 5,980,160 字节 | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` |
| 本工具补丁版 | `2.99.0.0` | 5,980,160 字节 | `34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7` |

未知哈希、未知版本、非法备份或无法识别的文件不会被修改。

## 一键使用

1. 完全退出 PCL 和正在运行的游戏。
2. 将 `PCL-2.99-Download-Fix.exe` 放到 PCL 主程序同目录，或使用 `--pcl` 指定路径。
3. 双击运行。工具会自动校验原版、创建备份、写入补丁并启动 PCL。
4. 在 PCL 中进入“下载 → 原版游戏”，确认可以正常选择版本并下载。

默认目标文件名为 `Plain Craft Launcher 2.exe`。首次修复会在同目录创建固定备份：

```text
Plain Craft Launcher 2.exe.pcl-download-fix.bak
```

- 备份只从已验证的原版创建。
- 已存在合法备份时不会覆盖。
- 已存在非法备份时工具停止并返回错误。
- 补丁版重复运行只做校验并启动，不会二次修改。
- 启动阶段失败时会自动从备份恢复。

### 系统要求

- Windows 10/11 x64。
- .NET Framework 4.8（Windows 10/11 通常已内置）。
- 不需要管理员权限。

## 命令行

```powershell
# 修复默认同目录 PCL，成功后启动
.\PCL-2.99-Download-Fix.exe

# 指定 PCL 路径，只修复不启动
.\PCL-2.99-Download-Fix.exe --pcl "D:\pcl启动器\Plain Craft Launcher 2.exe" --no-launch

# 只校验当前文件是原版还是补丁版
.\PCL-2.99-Download-Fix.exe --verify

# 从固定备份恢复原版
.\PCL-2.99-Download-Fix.exe --restore

# 显示帮助
.\PCL-2.99-Download-Fix.exe --help
```

### 返回码

| 返回码 | 含义 |
| ---: | --- |
| `0` | 成功 |
| `2` | 文件版本或哈希不受支持，或参数不受支持 |
| `3` | 文件、备份、运行中检测或其他文件系统错误 |
| `4` | 启动失败，并已自动从备份回滚 |
| `5` | 恢复失败，备份未被删除或覆盖 |

## 修复内容

静态分析确认目标方法为：

- MethodDef：`0x06000513`
- 方法：`PCL.PageDownloadInstall.LoaderInit`
- 方法头文件偏移：`0x2C0B4`
- CIL 文件偏移：`0x2C0C0`
- `code_size`：`94 → 51`

补丁版恢复为直接调用 `ControlInit()`，并调用 `PageLoaderInit(..., DlClientListLoader, AddressOf LoadMinecraft_OnFinish)`；不再引用只显示“关闭下载”提示的异常闭包。全文件与原版仅有 59 个字节不同，全部位于目标方法头和方法体。

工具不会下载或内置 PCL。它只对用户本地已持有的精确版本文件做字节级修复。

## 验收记录

2026-10-20 已完成以下测试：

- REA 6.7.0 静态检查，确认方法引用和字段访问已恢复。
- 隔离目录真实下载 Mojang 正式版 `26.3`：JSON、客户端 JAR、74 个 Libraries、5,147 个 Assets 对象。
- 使用该版本要求的 Java 25 成功启动 Minecraft，窗口稳定运行超过 60 秒。
- 真实 PCL 修复后界面显示“最新版本 / 正式版 / 原版游戏 / 开始下载”，不再显示“关闭下载”。
- 工具自动化测试 11 项全部通过，包括未知哈希、运行中拒绝、备份保护、重复运行、恢复、启动失败回滚和恢复失败返回码。

脱敏证据见 [analysis/evidence-summary.md](analysis/evidence-summary.md)。

## 构建与测试

```powershell
# 编译 Release 和测试钩子版本
.\build.ps1
.\build.ps1 -TestHooks

# 运行 11 项自动化测试
.\tests\Test-Tool.ps1

# 生成发布 ZIP 和 SHA256
.\scripts\release.ps1
```

完整测试说明见 [tests/README.md](tests/README.md)。

## 隐私与分发

本仓库和 Release **不包含**：

- PCL 可执行文件
- `.minecraft`、账户令牌、UUID、存档或日志
- Mojang 游戏文件
- REA 原始分析 JSON
- 测试钩子 EXE

Release ZIP 只包含工具 EXE、README、MIT 许可证和第三方声明。

## 许可证

本工具源码使用 [MIT License](LICENSE)。Plain Craft Launcher、REA、Minecraft 及其他第三方组件的权利归各自权利人所有，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

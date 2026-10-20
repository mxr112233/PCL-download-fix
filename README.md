# 原版游戏下载恢复工具

> **非官方第三方修复。** 本项目与 Plain Craft Launcher、Mojang、Microsoft 均无隶属或授权关系。
>
> **开发方式声明：本项目完全由 Vibecoding 完成。**
>
> 协作署名：DeepSeek。

本项目用于恢复 **Plain Craft Launcher 2 `2.99.0.0 (413)`** 中被禁用的原版 Minecraft 下载入口。修复只针对 `PCL.PageDownloadInstall.LoaderInit`，不会修改其他下载模块、PCL 元数据、账户、存档或游戏文件。

## 直接使用

1. 完全退出 PCL 和正在运行的游戏。
2. 解压发布包，双击 `PCL-2.99-Download-Fix.exe`。
3. 等待工具自动定位、校验、备份、修复并启动 PCL。
4. 看到“修复完成”弹窗后，在 PCL 中进入“下载 → 原版游戏”。

不需要命令行，不需要管理员权限。工具会优先在自身目录、当前目录、正在运行的 PCL 进程、桌面、文档、下载目录和固定磁盘中自动查找 PCL；找不到时会打开文件选择框。

建议把工具放在 PCL 主程序同目录，这样定位最快。工具也支持任意位置运行。

## 界面与反馈

工具使用蓝白风格的简洁窗口，启动后自动执行，不需要点击“开始”。

- 修复过程中显示当前步骤、百分比和详细日志。
- 成功后弹出“修复完成”窗口，显示 PCL 路径和原版备份路径。
- 失败后弹出“修复失败”窗口，显示返回码、错误原因、实际文件版本和 SHA256。
- 失败窗口支持“复制信息”“打开日志”和“重新尝试”。
- 最近一次失败的完整诊断保存在：

```text
%TEMP%\PCL-2.99-Download-Fix-last.log
```

如果 PCL 正在运行且尚未修复，工具会拒绝直接修改。完全退出 PCL 后，在失败窗口点击“重新尝试”即可。

启动检查会读取 PCL 新日志中的原版下载列表加载记录。即使 PCL 在正常关闭时返回 -1，只要确认下载列表已加载，工具也不会误判失败并回滚。

## 支持范围

只接受以下两个完整文件之一，其他版本或哈希一律拒绝：

| 状态 | 文件版本 | 文件大小 | SHA256 |
| --- | --- | ---: | --- |
| 原版 | `2.99.0.0` | 5,980,160 字节 | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` |
| 本工具补丁版 | `2.99.0.0` | 5,980,160 字节 | `34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7` |

未知哈希、未知版本、非法备份或无法识别的文件不会被修改。

## 备份与恢复

默认目标文件名为 `Plain Craft Launcher 2.exe`。首次修复会在同目录创建固定备份：

```text
Plain Craft Launcher 2.exe.pcl-download-fix.bak
```

- 备份只从已验证的原版创建。
- 已有合法备份时不会覆盖。
- 已有非法备份时工具停止，不会继续修改。
- 补丁版重复运行只校验并启动，不会二次修改。
- 启动阶段失败时会自动从备份恢复原版。

如需手动恢复：

```powershell
.\PCL-2.99-Download-Fix.exe --restore
```

## 命令行

图形界面适合日常使用；以下参数保留给高级用户和自动化测试：

```powershell
# 自动定位、修复并启动
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
| `5` | 恢复或回滚失败，备份未被删除或覆盖 |

## 系统要求

- Windows 10/11 x64。
- .NET Framework 4.8（Windows 10/11 通常已内置）。
- 不需要管理员权限。

## 修复内容

静态分析确认目标方法为：

- MethodDef：`0x06000513`
- 方法：`PCL.PageDownloadInstall.LoaderInit`
- 方法头文件偏移：`0x2C0B4`
- CIL 文件偏移：`0x2C0C0`
- `code_size`：`94 → 51`

补丁恢复为直接调用 `ControlInit()`，并调用 `PageLoaderInit(..., DlClientListLoader, AddressOf LoadMinecraft_OnFinish)`；不再引用只显示“关闭下载”提示的异常闭包。全文件与原版仅有 59 个字节不同，全部位于目标方法头和方法体。

工具不会下载或内置 PCL，只对用户本地已持有的精确版本文件做字节级修复。

## 验收情况

已完成静态复核、隔离环境功能检查和工具异常与恢复流程测试。14 项自动化测试全部通过；现有 `.minecraft`、账户和存档未被改动。

脱敏说明见 [analysis/evidence-summary.md](analysis/evidence-summary.md)。

## 构建与测试

```powershell
# 编译 Release 和测试钩子版本
.\build.ps1
.\build.ps1 -TestHooks

# 运行 14 项自动化测试
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

<div align="center">

# PCL-download-fix

**为 Plain Craft Launcher 2 恢复原版下载、整合包导入与版本文件夹选取**

[![Latest Release](https://img.shields.io/github/v/release/mxr112233/PCL-download-fix?style=flat-square&label=release&color=2f81f7)](https://github.com/mxr112233/PCL-download-fix/releases/latest)
[![PCL](https://img.shields.io/badge/PCL-2.99.0.0%20%28413%29-2f81f7?style=flat-square)](https://github.com/mxr112233/PCL-download-fix)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4?style=flat-square&logo=windows)](https://github.com/mxr112233/PCL-download-fix)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4?style=flat-square&logo=dotnet)](https://github.com/mxr112233/PCL-download-fix)
[![License](https://img.shields.io/badge/license-MIT-3da639?style=flat-square)](LICENSE)

[立即下载](https://github.com/mxr112233/PCL-download-fix/releases/latest) · [快速开始](#快速开始) · [命令行](#命令行) · [修复内容](#修复内容) · [问题反馈](https://github.com/mxr112233/PCL-download-fix/issues)

</div>

> [!WARNING]
> **非官方第三方修复。** 本项目与 Plain Craft Launcher、Mojang、Microsoft 均无隶属或授权关系。请仅对你自己合法持有的 PCL 文件使用本工具。

> [!IMPORTANT]
> **开发方式声明：本项目完全由 Vibecoding 完成。** 协作署名：DeepSeek。

## 这是什么

`PCL-download-fix` 是一个针对 **Plain Craft Launcher 2 `2.99.0.0 (413)`** 的本地修补工具，用于恢复以下功能：

- 原版 Minecraft 下载入口；
- 点击“导入整合包”选择压缩包；
- 将整合包压缩包拖入 PCL 窗口导入；
- 导入整合包时正常选取 `.minecraft/versions` 下的版本文件夹。

工具会修复 `PCL.PageDownloadInstall.LoaderInit`，通过新增 `.pclfix` 节重定向 `PCL.ModModpack::ModpackInstall` 的两个入口，并修正版本文件夹扫描参数。它不会改动 PCL 元数据、账户、存档或其他下载模块。

## 快速开始

1. 从 [Releases](https://github.com/mxr112233/PCL-download-fix/releases/latest) 下载最新 ZIP。
2. 完全退出 PCL 和正在运行的游戏。
3. 解压后双击 `PCL-2.99-Download-Fix.exe`。
4. 等待工具自动定位、校验、备份、修复并启动 PCL。
5. 看到“修复完成”弹窗后，可使用原版下载、拖入整合包、点击“导入整合包”或选择版本文件夹。

> 建议把工具放在 PCL 主程序同目录，定位会更快。工具也支持从其他位置自动搜索 PCL；找不到时会要求手动选择。

无需安装、无需管理员权限，也不需要命令行。

## 一键工具会做什么

| 步骤 | 行为 |
| --- | --- |
| 定位 | 自动查找 PCL 主程序，也支持手动选择 |
| 校验 | 只接受指定版本和 SHA256 的原版或补丁版文件 |
| 备份 | 首次从原版修复时创建固定原版备份，不覆盖已有合法备份 |
| 修复 | 写入下载入口、完整整合包导入和版本文件夹选取补丁 |
| 启动 | 修复后启动 PCL；启动异常时自动尝试回滚 |
| 反馈 | 显示进度、步骤日志和成功/失败弹窗 |

v1.0 下载补丁版和 v1.1 拖入修复版都会自动原地升级为 v1.2。已经是 v1.2 时，重复运行不会再次修改文件，只会校验并启动。

## 界面与诊断

工具采用蓝白风格的简洁界面，启动后自动执行，不需要点击“开始”。

- 修复过程中显示当前步骤、百分比和操作日志。
- 成功弹窗显示 PCL 路径和备份路径。
- 失败弹窗显示返回码、错误原因、实际文件版本和 SHA256。
- 失败窗口支持复制诊断信息、打开日志和重新尝试。
- 最近一次诊断日志保存在：

```text
%TEMP%\PCL-2.99-Download-Fix-last.log
```

如果 PCL 正在运行，工具会拒绝直接修改。请完全退出 PCL 后重新尝试。

## 支持范围

只接受以下四个完整文件之一，其他版本或哈希一律拒绝：

| 状态 | 版本 | 文件大小 | SHA256 |
| --- | --- | ---: | --- |
| 原版 | `2.99.0.0 (413)` | 5,980,160 字节 | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` |
| v1.0 下载入口修复版 | `2.99.0.0 (413)` | 5,980,160 字节 | `34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7` |
| v1.1 整合包拖入修复版 | `2.99.0.0 (413)` | 5,982,208 字节 | `AFC49BCD281D365D01D17C349C559DDB344DB00D9E403FFE088A47F83D88B9BB` |
| v1.2 完整修复版 | `2.99.0.0 (413)` | 5,982,208 字节 | `34F774B3A5936905DC7C033B502B980685B9EAE74D2E0D705DB459E376FC7DCB` |

未知版本、未知哈希、非法备份或无法识别的文件不会被修改。

## 备份与恢复

默认目标文件名为 `Plain Craft Launcher 2.exe`。首次从原版修复会在同目录创建：

```text
Plain Craft Launcher 2.exe.pcl-download-fix.bak
```

- 备份只从已校验的原版创建。
- 已有合法备份时不会覆盖。
- 已有非法备份时工具停止，不会继续修改。
- 启动阶段失败时会自动尝试恢复修改前状态。
- 如果备份被删除，后续将无法通过 `--restore` 恢复原版；建议自行保留一份合法原版副本。

手动恢复：

```powershell
.\PCL-2.99-Download-Fix.exe --restore
```

## 命令行

图形界面适合日常使用；以下参数保留给高级用户和自动化场景：

| 命令 | 用途 |
| --- | --- |
| `.\PCL-2.99-Download-Fix.exe` | 自动定位、修复并启动 |
| `.\PCL-2.99-Download-Fix.exe --pcl "路径" --no-launch` | 指定 PCL 路径，只修复不启动 |
| `.\PCL-2.99-Download-Fix.exe --verify` | 校验当前文件是原版、v1.0、v1.1 还是 v1.2 |
| `.\PCL-2.99-Download-Fix.exe --restore` | 从固定备份恢复原版 |
| `.\PCL-2.99-Download-Fix.exe --help` | 显示帮助 |

### 返回码

| 返回码 | 含义 |
| ---: | --- |
| `0` | 成功 |
| `2` | 文件版本、哈希或参数不受支持 |
| `3` | 文件、备份、运行状态检测或文件系统错误 |
| `4` | 启动失败，已尝试自动回滚 |
| `5` | 恢复或回滚失败 |

## 修复内容

静态分析确认本工具包含三组目标修复：

**原版下载入口**

- MethodDef：`0x06000513`
- 方法：`PCL.PageDownloadInstall.LoaderInit`
- 原版方法头文件偏移：`0x2C0B4`
- 原版 CIL 文件偏移：`0x2C0C0`
- `code_size`：`94 → 51`

下载补丁恢复对 `ControlInit()` 和 `PageLoaderInit(..., DlClientListLoader, AddressOf LoadMinecraft_OnFinish)` 的直接调用，不再引用只显示“关闭下载”提示的异常闭包。

**整合包点击与拖入导入**

- `PCL.ModModpack::ModpackInstall()`：MethodDef RID `495`
- `PCL.ModModpack::ModpackInstall(String, String, String)`：MethodDef RID `496`
- RID `495` 重定向到 `.pclfix + 0x450` 的完整方法体，恢复点击“导入整合包”后的文件选择与安装流程。
- RID `496` 保持在 `.pclfix` 节起始，继续负责拖入压缩包导入。
- `.pclfix`：VirtualSize `0x4E0`，RawSize `0x600`
- 选择框过滤器：`rar, zip, mrpack`

这样点击导入或拖入整合包时，不再进入“遵守相关法规，暂不提供下载功能”的异常提示路径。

**版本文件夹选取**

- MethodDef RID `5887`
- 将版本文件夹选择流程中的目录扫描参数从 `1` 修正为 `0`，避免扫描整个游戏目录导致无法正常选取版本。
- 修复后可正常选择 `.minecraft/versions` 下的实例目录，并继续整合包安装。

完整修复版会在原文件基础上追加新节并同步修正 PE 节表、镜像大小和 RVA；最终文件大小为 `5,982,208` 字节。

工具不会下载或内置 PCL，只对用户本地已持有的精确版本文件做字节级修复。

## 系统要求

- Windows 10/11 x64
- .NET Framework 4.8（Windows 10/11 通常已内置）
- 不需要管理员权限

## 构建与测试

```powershell
# 编译 Release 和测试钩子版本
.\build.ps1
.\build.ps1 -TestHooks

# 运行自动化测试
.\tests\Test-Tool.ps1

# 生成发布 ZIP 和 SHA256
.\scripts\release.ps1
```

测试和构建细节见 [tests/README.md](tests/README.md)。

## 项目结构

```text
PCL-download-fix/
├─ src/                     # 工具源码与 WinForms 界面
├─ tests/                   # 自动化测试
├─ scripts/                 # 发布脚本
├─ analysis/                # 脱敏后的分析摘要
├─ build.ps1                # 编译脚本
├─ LICENSE                  # MIT License
└─ THIRD_PARTY_NOTICES.md   # 第三方声明
```

## 隐私与分发

本仓库和 Release **不包含**：

- PCL 可执行文件
- `.minecraft`、账户令牌、UUID、存档或日志
- Mojang 游戏文件
- REA 原始分析 JSON
- 测试钩子 EXE

Release ZIP 只包含工具 EXE、README、MIT 许可证和第三方声明。

## 第三方与许可证

本项目源码使用 [MIT License](LICENSE)。Plain Craft Launcher、REA、Minecraft 及其他第三方组件的权利归各自权利人所有，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

<div align="center">

**如果这个工具帮你恢复了下载功能，欢迎给项目点一个 Star。**

</div>

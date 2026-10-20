# PCL 2.99 原版下载修复：验收证据摘要

> 本文件只保留脱敏后的结论、哈希和日志摘要。REA 原始 JSON、PCL 日志、账户信息、令牌与下载缓存均不发布。

## 1. 受支持目标

| 项目 | 值 |
| --- | --- |
| 产品版本 | 2.99.0.0 (413) |
| 文件大小 | 5,980,160 字节 |
| 原版 SHA256 | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` |
| 补丁版 SHA256 | `34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7` |
| 工具接受策略 | 版本和完整文件 SHA256 必须同时精确匹配；否则拒绝处理 |

## 2. REA 6.7.0 静态分析

- 使用 `inspect-managed-artifact` 检查原版与补丁版的 PE/CLR 元数据。
- 使用 `inspect-managed-members` 检查 `PCL.PageDownloadInstall.LoaderInit` 的 CIL、调用边和字段访问。
- 与 PCL 2.13.1.1 的 `LoaderInit` CIL 进行结构对照，用于确认原版下载入口的控件初始化顺序。
- 分析方法：`rea-dotnet-static`，仅做静态读取，不加载或执行目标程序集。

### 目标方法

| 项目 | 原版 | 补丁版 |
| --- | --- | --- |
| MethodDef | `0x06000513` | `0x06000513` |
| 方法名 | `PCL.PageDownloadInstall.LoaderInit` | 同左 |
| RVA | `188084` (`0x2DEB4`) | 同左 |
| 方法头文件偏移 | `0x2C0B4` | 同左 |
| CIL 文件偏移 | `0x2C0C0` | 同左 |
| `code_size` | 94 | 51 |
| CIL 字节数 | 94 | 51 |
| 指令数 | 30 | 18 |

补丁只改动 `LoaderInit`：方法头 `code_size` 和一个方法体内的字节。全文件对比共有 59 个不同字节，全部位于 `0x2C0B8` 与 `0x2C0DA..0x2C11D`；其余元数据、方法、资源和其他模块未改动。

### CIL 结论

- 原版方法尾部通过闭包 `_Lambda$__R3-3` 构造下载关闭提示路径，不再直接使用正常下载加载器。
- 补丁版先调用 `ControlInit()`，再直接读取 `PCL.ModDownload.DlClientListLoader` 字段 `0x0400040f`。
- 补丁版把 `LoadMinecraft_OnFinish` 方法 `0x0600061c` 绑定为 `Action<LoaderTask>`，调用 `PageLoaderInit(...)`。
- 补丁版调用边中不再存在 `_Lambda$__R3-3`，字段访问中不再存在 `$IR3-3` 闭包字段。

写入的 51 字节 CIL：

```text
02-28-12-05-00-06-02-02-6F-07-06-00-06-02-6F-05-06-00-06
02-6F-51-05-00-06-14-7E-0F-04-00-04-02-FE-06-1C-06-00-06
73-8D-05-00-0A-14-17-28-CA-00-00-2B-2A
```

## 3. 隔离环境真实下载与启动

- 验收日期：2026-10-20（Asia/Shanghai）。
- 测试根目录：`C:\Temp\pcl299-e2e-20261020-01`。
- PCL 游戏目录明确为该根目录下的 `.minecraft`，未使用用户主游戏目录。
- PCL 下载页恢复后显示当前正式版 `26.3`；未再显示“关闭下载”。
- 下载完成：JSON、客户端 JAR、74 个 Libraries、5,147 个 Assets 对象（484,829,952 字节）。
- 下载日志摘要：`26.3 安装成功！`。
- 启动日志摘要：`已出现日志输出`、`26.3 启动成功！`。
- 运行进程窗口：`Minecraft 26.3`，响应正常，持续稳定运行超过 60 秒。
- 版本 JSON 要求 Java 25；PCL 自动使用 `java-runtime-epsilon` 并成功启动。该运行时由本次测试新建，验收后已清理。

| 下载产物 | 大小 | SHA256 |
| --- | ---: | --- |
| `26.3.json` | 63,976 | `357CD6BC1321F259C24CA9D5754AB2DC1ACFCA97654BC05A68894224B686D6E2` |
| `26.3.jar` | 41,483,720 | `4508D006323F24FA02876310C192D739AF56516EB259000AC50F0909A68C9A2D` |

## 4. 隔离与清理

- 测试期间曾出现一个用户目录下的嵌套误建目录；确认其路径边界和内容后已删除，父实例与原 `.minecraft` 保留。
- 测试结束后再次核对用户 `.minecraft`：`launcher_profiles.json`、`options.txt` 哈希保持不变，未发现 账户文件，存档文件数量和总大小未变化，也未发现测试时间段内的新写入。
- 测试 PCL 与 Minecraft 均通过窗口关闭消息正常退出。
- 测试目录、下载缓存和本次新建的 Java 25 运行时在证据固定后清理。

## 5. 工具验收

最终工具测试覆盖：

- 原版修复与补丁版字节/哈希验证。
- 重复运行不二次修改、只启动。
- `--verify`、`--restore`、`--no-launch`、`--pcl`、`--help`。
- 未知哈希拒绝且不创建备份。
- PCL 运行中拒绝修改。
- 无效备份拒绝且不覆盖。
- 有效备份不覆盖。
- 启动失败自动从备份回滚（退出码 4）。
- 恢复失败返回 5 且不损坏备份。
- Release EXE 不包含测试钩子。

原始 REA JSON 和完整日志仅保存在本地分析/测试现场，不进入 Git 仓库或 Release。
## 6. 真实 PCL 修复与界面冒烟

- 真实目标：`D:\pcl启动器\Plain Craft Launcher 2.exe`。
- 修复前 SHA256：`6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386`。
- 备份：`Plain Craft Launcher 2.exe.pcl-download-fix.bak`，SHA256 为原版哈希。
- 修复后 SHA256：`34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7`。
- 一键工具以 `--no-launch` 完成原子替换，随后通过正常启动流程启动 PCL。
- 界面自动化读取到“最新版本”“正式版”“原版游戏”“开始下载”等控件；下载页不再出现“关闭下载”。
- 工具自动化测试 11 项全部通过；Release EXE 不含测试钩子。

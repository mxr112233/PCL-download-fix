# v1.2.0 - 整合包点击导入与版本文件夹选取修复

发布日期：2026-10-26

## 更新内容

- 修复点击“导入整合包”后抛出 `InvalidProgramException`、无法正常选择整合包的问题。
- 保留拖入整合包压缩包导入功能，`.pclfix` 节起始的 RID 496 入口不变。
- 在 `.pclfix + 0x450` 写入合法方法体，并将 RID 495 重定向到该入口。
- 整合包文件选择过滤器恢复为 `rar, zip, mrpack`。
- 修复 RID 5887 的版本文件夹目录扫描参数，解决无法正常选取 `.minecraft/versions` 下实例目录的问题。
- `.pclfix` VirtualSize 由 `0x44A` 扩展为 `0x4E0`，同步更新 PE 镜像大小和方法 RVA。
- 支持原版、v1.0、v1.1 原地升级到 v1.2；v1.1 升级失败时自动回滚到 v1.1。
- 更新界面成功提示、状态文案、帮助信息和自动化测试。

## 验证结果

- 19 项自动化测试全部通过。
- 原版 SHA256 `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` 可精确修复为 v1.2。
- v1.0 SHA256 `34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7` 可精确升级为 v1.2。
- v1.1 SHA256 `AFC49BCD281D365D01D17C349C559DDB344DB00D9E403FFE088A47F83D88B9BB` 可精确原地升级为 v1.2。
- RID 495 方法体通过 Cecil 结构检查；RID 496 仍指向 `.pclfix` 节起始。
- 完整修复版大小：`5,982,208` 字节。
- 完整修复版 SHA256：`34F774B3A5936905DC7C033B502B980685B9EAE74D2E0D705DB459E376FC7DCB`。

## 发布文件

- `PCL-2.99-Download-Fix-v1.2.0.zip`
- `PCL-2.99-Download-Fix-v1.2.0.zip.sha256`
- 发布包 SHA256：`25ECEDE721507EEF750415B01F89B8BD9E7BA28C24EF23C9D38967265BB19330`

> 本项目为非官方第三方修复工具，仅用于修复你自己合法持有的 PCL 2.99.0.0 (413) 文件。

# v1.1.0 - 整合包导入修复

发布日期：2026-10-25

## 更新内容

- 修复把整合包 ZIP 拖入 PCL 窗口时仍提示“遵守相关法规，暂不提供下载功能”的问题。
- 将 `PCL.ModModpack::ModpackInstall()` 和 `ModpackInstall(String, String, String)` 重定向到新增的 `.pclfix` 节，恢复整合包导入入口。
- 保留 v1.0 的原版游戏下载入口修复。
- 支持把 v1.0 下载补丁版自动升级为 v1.1 完整修复版，无需手动还原原版。
- 启动失败时支持按原状态自动回滚：原版回滚到原版，v1.0 回滚到 v1.0。
- 更新工具界面、诊断文案、返回码说明和自动化测试。

## 验证结果

- 17 项自动化测试全部通过。
- 完整修复版的整合包相关方法通过 ILVerify，无 IL 校验错误。
- 原版 SHA256 `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` 可精确修复为完整修复版。
- v1.0 SHA256 `34DE99E6F4ECFF3DE6B7DFFFAD14B501ABEAA5BB61047CCA541BC067A6B9CBE7` 可精确升级为完整修复版。
- 完整修复版大小：`5,982,208` 字节。
- 完整修复版 SHA256：`AFC49BCD281D365D01D17C349C559DDB344DB00D9E403FFE088A47F83D88B9BB`。

## 发布文件

- `PCL-2.99-Download-Fix-v1.1.0.zip`
- `PCL-2.99-Download-Fix-v1.1.0.zip.sha256`

发布包 SHA256：`1531DAD8CF7005EF0684A106D0AAC2BFD53F2219FB88CB84F2F7C6E2B45FBC65`

> 本项目为非官方第三方修复工具，仅用于修复你自己合法持有的 PCL 2.99.0.0 (413) 文件。
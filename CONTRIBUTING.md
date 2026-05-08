# 参与贡献

感谢你对 KeyNest for Windows 的关注。以下为建议流程与约定。

## 行为准则

本仓库遵循 [Contributor Covenant](CODE_OF_CONDUCT.md)。参与即表示你同意遵守。

## 如何贡献

1. **Issue 优先**：新功能、较大重构或行为变更，建议先开 Issue 简述场景，避免与维护者方向不一致。
2. **分支与提交**：从默认分支拉出功能分支；提交信息建议使用 [Conventional Commits](https://www.conventionalcommits.org/) 风格（如 `feat:`、`fix:`、`docs:`）。
3. **合并请求（PR）**：描述动机、主要改动点；若涉及 UI/加密/浏览器扩展协议，请说明测试方式。

## 开发环境

- Windows 10 / 11 **x64**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 构建桌面客户端：见根目录 `README.md` 中 `dotnet build` / `dotnet publish` 说明。
- Chrome 扩展：修改 `browser/chrome-extension/` 后，在 `chrome://extensions` 中「重新加载」以验证。

## 代码与安全相关约定（摘要）

- **保管库与密码学**：对 `VaultCryptography`、`VaultService`、桥接 API 的修改须谨慎，保持与文档中协议、macOS 版格式的兼容性说明一致（见 `README.md`）。
- **本机桥**仅监听 `127.0.0.1`，勿在默认配置下扩大对外监听范围。
- **不要在 Issue/PR/日志中粘贴**真实主密码、恢复密钥或完整 `vault.keynest` 内容。

## 许可

贡献的代码将按仓库根目录 [LICENSE](LICENSE)（MIT）授权，除非你与维护者另有书面约定。

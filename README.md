# KeyNest for Windows

面向 **Windows 10 / 11（x64）** 的 KeyNest 桌面客户端（**.NET 8 + WPF**），与 macOS 版共用同一保管库文件格式（`vault.keynest`），便于复制文件后在两台机器上使用同一数据库。

> 当前版本：**0.5.2**（与 macOS 版功能对齐）

维护者：**heureuxl** · [lq_17395@163.com](mailto:lq_17395@163.com)

## 功能概览

- **本地加密保管库**：`%AppData%\KeyNest\vault.keynest`（若检测到旧版 `%AppData%\TwoPassword\vault.twopw` 且新文件不存在，会尝试复制迁移）。
- **主密码**解锁 / 新建；**恢复密钥**重置主密码（v2 格式，与 macOS 一致）；解锁后可**更换恢复密钥**。
- **条目管理**：模糊搜索（标题、用户名、网站、备注、自定义字段）；筛选（全部 / 收藏 / 最近使用 / 空密码 / 弱密码）；列表或**按域名分组**；详情面板显示/复制密码、弱密码提示。
- **自定义字段**：银行卡、API Key、密保问答等；**收藏**置顶。
- **整理**：合并重复（同站点同用户名仅保留最新一条）。
- **本机 HTTP 桥** `127.0.0.1:17373`：`GET /api/credentials?url=`、`POST /api/save`（密码未变返回 `unchanged`）；仅解锁且开启桥接时监听。
- **浏览器扩展**：`browser/chrome-extension`（Chrome）、`browser/edge-extension`（Edge）；自动填充、提交保存、明文密码记忆（避免站点加密串入库）、密码不一致提示同会话仅一次。
- **系统托盘** + **单实例**：关闭窗口后驻留托盘；重复启动会激活已有窗口。
- **站点规则**：同一主机（域名或 IP，不含路径与端口）默认可保存 **3 个不同用户名**（可在设置中改为 1–99）；相同主机 + 用户名则覆盖；子域与根域可按包含关系匹配。

## 系统要求

- Windows **10 / 11**，x64
- 运行发布包**无需**单独安装 .NET 运行时（自包含单文件）
- 从源码构建需 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 与 **.NET 桌面开发**工作负载

## 从源码构建

```bat
dotnet restore KeyNestForWin.sln
dotnet build KeyNestForWin.sln -c Release
```

输出：`src\KeyNestForWin\bin\Release\net8.0-windows\KeyNestForWin.exe`

## 打包与发版

**必须在 Windows x64 上执行**（WPF 目标）。

```bat
scripts\publish-release.bat
```

或 PowerShell：

```powershell
.\scripts\publish-release.ps1
```

产物目录 `dist\`：

| 路径 | 说明 |
|------|------|
| `KeyNest-Windows-Release\KeyNestForWin.exe` | 自包含单文件主程序 |
| `KeyNest-Chrome.zip` / `KeyNest-Edge.zip` | 浏览器扩展（解压后「加载已解压的扩展程序」） |
| `Chrome扩展安装说明.txt` / `Edge扩展安装说明.txt` | 中文安装说明（UTF-8 带 BOM，源码见 `scripts/extension-install-notes/`） |
| `KeyNest-Windows-<版本>-win-x64.zip` | 上述内容的一键分发包 |

## 浏览器扩展安装

1. 解压 `KeyNest-Chrome.zip` 或 `KeyNest-Edge.zip`，得到 **KeyNest** 文件夹。
2. Chrome：`chrome://extensions` → 开发者模式 → **加载已解压的扩展程序** → 选中该文件夹。  
   Edge：`edge://extensions` → 开发人员模式 → **加载解压缩的扩展**。
3. 打开 KeyNest 桌面端并解锁，勾选底部「允许浏览器扩展连接本机端口 17373」。

## HttpListener 与端口

若启动桥接时提示**拒绝访问**，可在**管理员**命令提示符执行（仅需一次）：

```bat
netsh http add urlacl url=http://127.0.0.1:17373/ user=%USERNAME%
```

## 与 macOS 的差异

- 无 Safari / Firefox 扩展打包脚本（Windows 仓库提供 Chrome + Edge）；Firefox 可尝试加载 Chrome 包，未单独测试。
- UI 为 WPF 实现，交互与 macOS SwiftUI 版等价但布局略有不同。

## 规范与许可

| 文档 | 说明 |
|------|------|
| [LICENSE](LICENSE) | **MIT License** |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 贡献指南 |
| [SECURITY.md](SECURITY.md) | 安全报告 |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | 行为准则 |

**版权**：Copyright (c) 2026 [heureuxl](mailto:lq_17395@163.com)

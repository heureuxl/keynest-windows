# KeyNest for Windows

面向 **Windows 10 / 11** 的 KeyNest 桌面客户端（**.NET 8 + WPF**），与 macOS 版共用同一保管库文件格式（`vault.keynest`），便于复制文件后在两台机器上使用同一数据库。

## 功能（首版）

- 主密码解锁 / 新建保管库；**恢复密钥**重置主密码（与 macOS v2 保管库一致）。
- **PBKDF2-SHA256（310000 轮）+ AES-GCM**，与 Swift 版 `VaultCrypto` 对齐。
- 条目列表（标题 / 用户名 / URL）；添加、编辑、删除。
- **Chrome 扩展源码**：与本仓库根目录 `browser/chrome-extension` 内 macOS 版保持一致（可从 Windows 仓库打包 zip/crx 安装）；自动填充、提交保存时的去重与密码变更提示等行为与 macOS 同源脚本一致。
- **本机 HTTP 桥** `127.0.0.1:17373`：`GET /api/credentials?url=`、`POST /api/save`，与现有 Chrome 扩展协议一致。
- **系统托盘**：关闭窗口后驻留托盘；托盘可打开窗口、锁定、退出。
- 保管库路径：`%AppData%\KeyNest\vault.keynest`；若检测到旧路径 `%AppData%\TwoPassword\vault.twopw` 且新文件不存在，会尝试复制迁移。

## 构建要求

- [**.NET 8 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0)（Windows x64）
- Visual Studio 2022（工作负载：**.NET 桌面开发**）或 Visual Studio Build Tools + Windows SDK

在仓库根目录执行：

```bat
dotnet restore KeyNestForWin.sln
dotnet build KeyNestForWin.sln -c Release
```

输出程序：`src\KeyNestForWin\bin\Release\net8.0-windows\KeyNestForWin.exe`

### 打包 Windows 分发版（绿色 / 单 exe）

**必须在已安装 .NET 8 SDK 的 Windows（x64）上执行**；`net8.0-windows` + WPF 在 macOS/Linux 上通常无法可靠发布 Windows 目标。

在仓库根目录任选其一：

```bat
scripts\publish-release.bat
```

```powershell
.\scripts\publish-release.ps1
```

产物目录：`dist\KeyNest-Windows-Release\`，主程序为 **`KeyNestForWin.exe`**（自包含单文件，可将整个文件夹打成 zip 分给其他 Win10/11 x64 电脑）。

等价命令（脚本内部即为此参数）：

```bat
dotnet publish src\KeyNestForWin\KeyNestForWin.csproj -c Release -o dist\KeyNest-Windows-Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
```

## HttpListener 与端口

若启动桥接时提示 **拒绝访问** 或无法监听 `127.0.0.1:17373`，可在**管理员**命令提示符执行（仅需一次）：

```bat
netsh http add urlacl url=http://127.0.0.1:17373/ user=%USERNAME%
```

## 与 macOS 的差异

- 无原生菜单栏图标（Windows 使用托盘）。
- **应用与托盘图标**：见工程 `Assets/app.ico` 与 `ApplicationIcon` 配置。
- UI 为简化版；后续可继续对齐 macOS 详情布局与版本号策略。

## 规范与许可

| 文档 | 说明 |
|------|------|
| [LICENSE](LICENSE) | **MIT License** — 使用与再发布条件见文件全文。 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 参与贡献的流程与约定。 |
| [SECURITY.md](SECURITY.md) | 安全漏洞报告方式与范围。 |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | 社区行为准则（Contributor Covenant 2.1 改编）。 |

**版权**：Copyright (c) 2026 [heureuxl](mailto:lq_17395@163.com)

若为主仓库 **KeyNest** 的 monorepo，仍以该仓库根目录 **LICENSE** 为准；本文件为 Windows 客户端仓库内同步说明。

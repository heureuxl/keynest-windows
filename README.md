# KeyNest for Windows

面向 **Windows 10 / 11** 的 KeyNest 桌面客户端（**.NET 8 + WPF**），与 macOS 版共用同一保管库文件格式（`vault.keynest`），便于复制文件后在两台机器上使用同一数据库。

## 功能（首版）

- 主密码解锁 / 新建保管库；**恢复密钥**重置主密码（与 macOS v2 保管库一致）。
- **PBKDF2-SHA256（310000 轮）+ AES-GCM**，与 Swift 版 `VaultCrypto` 对齐。
- 条目列表（标题 / 用户名 / URL）；添加、编辑、删除。
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
- 图标暂用系统默认；可将 `AppIcon.ico` 放入工程并写入 `csproj` 的 `ApplicationIcon`。
- UI 为简化版；后续可继续对齐 macOS 详情布局与版本号策略。

## 许可

与主项目一致（MIT）；仓库位于 `KeyNest` / `KeyNestForWin` 时请以根目录 `LICENSE` 为准。

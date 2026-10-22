# Desktop Preview 分支

基于 1Remote 1.3.1，增加远程桌面历史预览和紧凑的设备卡片界面。

## 功能

- RDP 桌面可见时定期保存最后一张有效画面，断开连接或重启应用后仍可查看。
- 卡片默认每行 4 台，顶部 4 / 5 / 6 按钮切换列数；图片随卡片宽度等比例缩放。
- 单击画面连接；卡片提供文件共享、大图、连接设置和远程电源操作。
- 浅色顶部与紧凑编辑页，备注支持宽屏并排、窄屏换行，脚本设置默认折叠。

预览保存在本机 `%LOCALAPPDATA%\1Remote\DesktopPreviews`，每个连接保留一张。更多菜单可清除或停用预览。历史画面不是实时在线画面。

文件功能使用 Windows SMB；远程电源操作需要当前 Windows 账户的相应权限。电源操作确认目标后执行，不强制关闭应用。远端连接及电源操作需要在自己的测试环境中验证。

## 构建

需要 Windows、.NET 10 SDK，以及初始化后的 Git 子模块：

```powershell
git submodule update --init --recursive
dotnet publish Ui/Ui.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o artifacts/publish
```

没有本机构建兼容值时，源码保留上游占位值，不能保证读取其他发行版创建的数据库。需要兼容已有安装时，提供自己原版程序的位置：

```powershell
./Build-Preview.ps1 -OriginalExe 'C:\Apps\1Remote\1Remote.exe'
# SDK 不在 PATH 时，另传 -Dotnet 'C:\Tools\dotnet\dotnet.exe'
```

此脚本仅在本地提取构建兼容值，写入被 Git 忽略的 `Ui/CompatibilitySalt.private`，然后嵌入本地 EXE。兼容值、数据库、用户配置、真实截图以及带兼容值的构建产物均不属于本分支的公开交付内容。公开源码不会读取或上传用户数据库。

## 验证

```powershell
dotnet test Tests/Tests.csproj -c Release --filter 'FullyQualifiedName~DesktopPreview'
```

测试覆盖截图保存、空白画面保护、缓存隔离和禁用，以及混合高度卡片在 4 / 5 / 6 列之间切换时的布局。

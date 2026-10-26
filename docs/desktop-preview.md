# Desktop Preview 分支

基于 1Remote 1.3.1，增加远程桌面历史预览和紧凑的设备卡片界面。

## 功能

- RDP 桌面可见时定期保存最后一张有效画面，断开连接或重启应用后仍可查看。
- 卡片默认每行 4 台，顶部 4 / 5 / 6 按钮切换列数；画面统一为 16:9，等比例填满预览区域，完整截图可通过“大图”查看。
- 底部左侧显示最后连接时间，文件 / 大图 / 更多三个按钮在右侧同排对齐；窄卡片显示短日期和紧凑按钮，悬停日期查看完整时间。
- 单击画面连接；卡片提供文件共享、大图、连接设置和远程电源操作。
- 勾选卡片或 Ctrl / Shift 单击可多选，选中时显示蓝色发光边框；按回车打开所选可见设备，已连接的设备仍复用现有窗口。
- 没有历史预览时使用当前 Windows 安装的原生默认壁纸作为占位，不写入截图缓存，也不显示“尚无预览”页脚。
- “更多 → 复制副本”打开预填当前连接配置的编辑页，保存后新增连接，原连接保持不变。
- 浅色顶部与紧凑编辑页，备注支持宽屏并排、窄屏换行，脚本设置默认折叠。
- 已连接的设备再次点击时激活现有窗口和标签；连接中的会话不会因再次点击而重新连接。
- 设备卡片支持独立备注名，在“更多 → 编辑备注名”修改；隐私模式隐藏备注，设备名简化为 Server-amz01 / Server-admin 等，保留外网后缀，不改写原始连接名称。
- 增强模式独立控制卡片 IP / 主机名及端口的显示。隐私与增强开关分别记忆，均不遮挡远程桌面截图。
- 顶部“账号表”集中编辑账号信息，支持多行复制新增和新增 RDP；保存后自动同步到主页卡片，单击卡片才连接。
- 账号表的撤销 / 重做及 Ctrl+Z / Ctrl+Y 覆盖单元格编辑、新增、复制、Excel 导入和移除草稿。保存落盘后清空本轮历史，不回滚已保存的数据库内容。
- 账号表支持导入 / 导出 `.xlsx`。导入先进入草稿，检查后保存，不覆盖原有设备；导出当前筛选结果，密码默认不包含，只有手动勾选“导出含密码”才写入密码。隐私模式导出不包含原设备名称和备注。

备注名设置仅保存在本机 `%LOCALAPPDATA%\1Remote\DeviceAliases`，按安装目录隔离，不写入远程连接数据库或公开源码。私有姓名对应表不随源码、公开程序包发布。

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

## 公开发布包

```powershell
./Build-PublicPreview.ps1
```

公开构建禁用本地兼容值资源和本地密钥注入步骤，并移除调试符号与 XAML 调试信息。发布时只打包 EXE、许可证、便携模式标记及说明，不包含数据库、配置或截图。

公开版应解压到新的独立目录。它不会兼容官方版或本地定制版已有的加密数据库；请重新添加连接，或在本地自行构建兼容版本。不要用公开版覆盖现有安装目录。`v*-preview.*` 标签不触发上游正式版发布流程。

## 任务栏最近连接

右键任务栏中的 1Remote 图标，可在“最近连接”中找到最近使用的已保存设备（最多 5 项）。悬停设备后点击图钉可放入 Windows 管理的“已固定”分组；再次点击图钉可取消固定。点击条目会连接设备，已有会话则激活原窗口。

列表沿用本地连接历史，不收录临时连接。条目只保存设备名及不含凭据的连接标识，不写入备注姓名；设备改名不会改变入口标识。设备删除或数据源不可用时会提示检查。Windows 的“在开始菜单、跳转列表和文件资源管理器中显示最近打开的项目”需保持开启。

## 验证

```powershell
dotnet test Tests/Tests.csproj -c Release -p:PublicPreviewRelease=true `
  --filter 'FullyQualifiedName~DesktopPreview|FullyQualifiedName~DesktopCardSelectionTests|FullyQualifiedName~DesktopDisplayModeTests|FullyQualifiedName~DesktopSessionPrivacyTests|FullyQualifiedName~TaskbarJumpListTests|FullyQualifiedName~AccountTable'
```

测试覆盖截图保存、空白画面保护、缓存隔离、多选和回车、显示模式持久化、统一卡片布局、最近连接列表、账号复制隔离与临时 SQLite 保存往返、Excel 导入导出，以及撤销重做与保存边界。账号测试使用虚构数据和隔离的临时数据库，不读取本地账号库。

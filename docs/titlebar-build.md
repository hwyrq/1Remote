# 会话窗口标题栏与标题后缀开关

本改动基于 `1.3.1` 标签（`d9dd7fa98cb914d5ee112acc860bf557b66975e9`），
维护分支为 `feature/session-titlebar-toggle`。

主窗口搜索框右侧、菜单左侧的窗口图标用于显示或隐藏所有会话窗口的标题栏。
带斜线表示已隐藏。设置会自动保存，新会话继承当前状态，没有新增快捷键。
旧配置缺少该设置时保持标题栏可见；完整便携包的初始配置设为隐藏。

旁边带 `T` 的窗口图标控制会话窗口标题中自动追加的 ` - 1Remote` 后缀。
关闭后，`code - 1Remote` 变为 `code`，连接名称始终保留；开启后恢复后缀。
全屏窗口原本没有自动追加的应用名后缀，其原有标题保持不变。
这两个开关独立生效，状态自动保存，新建窗口也会继承当前状态；没有新增快捷键。
旧配置和完整便携包默认显示后缀，点击新按钮即可隐藏。
此前已关闭标题文字的配置可以直接沿用，更新后会保留连接名称，无需重置配置。

## 构建

在 Windows x64 上安装 .NET 10 SDK，初始化标签指定的子模块后运行：

```powershell
git submodule update --init --recursive
.\scripts\Build-Titlebar.ps1 -Verify
```

也可通过 `-DotnetPath` 指定已下载的 `dotnet.exe`，通过 `-PackageSource` 指定已下载的
NuGet 包缓存目录。缓存作为只读包源使用，构建写入 `artifacts` 及各项目的 `bin`、`obj`。
脚本会恢复本次修改的进程环境变量，并覆盖上游依赖维护者私有文件的构建钩子；
构建使用仓库原有的占位参数，与此前交付的自编译版本保持一致。

产物位于 `artifacts/1Remote`，包含 .NET 10 运行时。`-Verify` 会使用独立测试配置运行
WPF 交互检查，输出到 `artifacts/qa`，包括检查日志及显示、隐藏状态的按钮渲染。
检查覆盖实际编译的主窗口、会话视图、双向绑定、多窗口同步、状态保存及内容布局，
并通过 Windows 原生窗口接口验证 `code - 1Remote` 与 `code` 的切换、全屏标题保留、
新窗口、改名和切换标签后的行为，以及连接名自身含 ` - 1Remote` 时仍完整保留。
测试中设置页内容为空白占位，不连接远程主机。

## 打包和更新

安装 Python 3 后运行：

```powershell
python .\scripts\Package-Titlebar.py
```

完整便携包、源码差异、构建信息及 SHA-256 清单输出到 `artifacts/dist`。
可用 `--output` 指定新的产物目录，`--prefix` 指定新的文件名前缀，保留已发布的旧包。
构建信息记录源码提交、工作区是否存在未提交改动、补丁校验值和本次通过的检查数量。
如需生成前一个自编译版本的增量更新包，同时指定其发布目录与源码目录：

```powershell
python .\scripts\Package-Titlebar.py --previous-publish '<之前的发布目录>' --previous-source '<之前的源码目录>'
```

打包脚本校验两次构建的加密参数源码一致，只把变化的程序文件写入更新包。
更新包不包含 `1Remote.json` 或数据库；从托盘完全退出前一个修改版后，将更新包解压到
其 `1Remote.exe` 所在目录并覆盖同名文件，再重新启动，即可沿用该目录的配置和连接数据。
更新包仅用于此前提供的自编译版本。

完整便携包请解压到新目录后运行 `1Remote.exe`，首次运行使用独立配置，默认隐藏会话标题栏。
从官方版迁移时，在官方版导出连接，再在此版本导入。

## 文件范围

功能修改涉及以下 8 个文件，子模块指针保持 `1.3.1` 标签中的版本：

- `Ui/Resources/Languages/en-us.xaml`
- `Ui/Resources/Languages/zh-cn.xaml`
- `Ui/Service/ConfigurationService.cs`
- `Ui/View/Host/TabWindowView.xaml`
- `Ui/View/Host/TabWindowView.xaml.cs`
- `Ui/View/Host/TabWindowViewModel.cs`
- `Ui/View/MainWindowView.xaml`
- `Ui/View/Settings/General/GeneralSettingViewModel.cs`

另外增加本说明、本地构建与打包脚本、构建钩子以及复用的 WPF 检查程序。
Git 补丁相对 `1.3.1` 标签生成，包含以上文件及 README 中的版本和功能说明。

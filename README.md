# CaptureDesk

Windows 截图与标注工具，WPF + .NET 10。

## 下载与安装

在 [GitHub Releases](https://github.com/9600bits/CaptureDesk/releases) 下载 `CaptureDesk-0.4-win-x64-setup.exe`。
支持 Windows 10 22H2 / Windows 11 x64，自带 .NET 运行环境，安装和使用无需 GitHub 连接。
默认安装到当前用户的应用目录，无需管理员权限；桌面快捷方式可选，默认不开机启动。
卸载保留个人配置及录制缓存。文字、表格识别依赖 Windows 已安装的 OCR 语言包。
安装程序暂未进行代码签名。

## 当前可用

- 区域截图：拖动选区、移动选区、Enter / 双击确认、Esc / 右键取消。
- 截图浮动工具栏直接在原选区编辑，不新开编辑窗口；撤销、重做、文字和标注随复制、保存、贴图一起输出。Enter / 完成直接复制；取消保存对话框保留编辑状态。开始标注后固定裁切区域，重新选区会清除标注。
- 截图前冻结桌面，裁切原图；物理像素转换支持负坐标与 DPI 缩放。
- 矩形、椭圆、带箭头端点的箭头、画笔、文字、实色遮挡。
- 更多标注菜单：直线、荧光笔、自动递增序号、真实像素马赛克；颜色和线宽就地调整，不需要进入设置。马赛克强度随线宽增大，导出为像素块，实色遮挡仍独立保留。Shift 绘制正方形 / 正圆或固定 45° 倍数方向的直线 / 箭头。
- 选择工具双击文字可再次修改，支持撤销 / 重做；历史编辑支持 Ctrl+Z / Ctrl+Y、Delete，文字输入框内保留文本快捷键。
- 选择和移动标注、Delete 删除、撤销 / 重做、复制、PNG / JPEG 保存、贴到桌面。
- 剪贴板图像直接贴图；贴图可置顶、调透明度、滚轮缩放、关闭。
- 贴图右键菜单：锁定位置与大小、恢复尺寸、保存 PNG / JPEG、复制；锁定后禁用拖动和滚轮缩放，仍可右键解锁或关闭。
- 本次会话的截图历史，可重新编辑或贴图。
- 设置分类导航、浅色 / 深色、实际功能偏好、配置导入 / 导出和默认设置恢复。
- 全局 Ctrl + Shift + A、托盘入口、关闭到托盘、可选开机启动。
- 长截图：框选纯滚动内容区域后手动缓慢滚动，自动检测上下位移并拼接；反向滚动不重复累加。匹配失败保留已有内容，回滚后可以继续。完成后预览、复制、保存 PNG、加入历史。请避开固定标题栏，每次保留至少一半重叠。
- GIF 录制：5 / 10 / 15 fps、鼠标指针、暂停 / 继续、停止、按秒截取连续片段、流式导出、真实帧进度和取消。控制窗口排除在采集之外。当前采集后端为 GDI，不是 Windows Graphics Capture；不含音频。
- 录制帧按时间记录到磁盘，意外退出后在主界面的“恢复录制”选择日期重新导出，也可将缓存移到回收站。单次限制为 5 分钟或 512 MB，导出成功也保留缓存供重新剪辑。取消或失败不会覆盖原目标文件。
- 文字识别：选择截图区域，使用 Windows 已安装的 OCR 语言包，支持语言选择、原图对照、结果编辑、复制及 UTF-8 文本导出。缺少语言包时提供安装说明；支持取消与 20 秒超时。无需捆绑大型模型。
- 历史图片直接在历史窗口内编辑，不再新开编辑窗口。
- **截图后工具栏**直接提供长截图、屏幕录制、文字识别、表格识别，复用当前选区。主界面不再重复这些入口。工具栏根据当前显示器工作区换行，不缩小按钮。长截图与录制使用实时屏幕区域，不录制截图上的标注；两种识别使用当前导出图像（含标注）。
- **表格识别**：Windows OCR + 网格线 / 空白间距推断行列，补识别空白单元格。原图对照、单元格编辑、增加 / 删除行、复制 TSV、导出 XLSX / CSV。XLSX 将内容保存为文字，不执行识别出的公式。CSV 转义公式前缀。最多 500 行、100 列，每次最多补识别 100 个空白单元格。不支持合并单元格、斜表头或复杂嵌套；低清文字及短数字可能遗漏，需核对原图。

界面内嵌 MiSans Regular / Semibold 完整字库；无需在 Windows 中安装字体。字重采用原始字体的 330 / 520，避免模拟加粗。导航、主窗口和工具栏统一使用 Iconify 的 Lucide 图标，离线渲染，不引入整个图标库或网页运行时。图标原始数据和许可证位于 Assets/Icons。

## 尚未完成

尚未接入：Windows Graphics Capture、MP4 / WebP 录制、硬件编码、音频和摄像头、键鼠操作叠加、预览时间轴；条码、翻译；高级捕获模式和高级标注、贴图分组与更多贴图类型、PDF / SVG 导出。
长截图目前是手动滚动、自动拼接；不支持自动滚轮控制、横向拼接、固定区域消除及跨进程恢复。长图限制为 6400 万像素。
历史仅在当前会话中保存；标注支持当前编辑会话内移动、删除和文字修改，导出或重新打开历史图像后标注合并到图片，不保留矢量编辑记录。
多显示器坐标和 100% / 150% / 200% 缩放有几何测试，实际混合 DPI 多屏仍需设备验证。

## 构建和测试

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build CaptureDesk.slnx -c Release
& 'C:\Program Files\dotnet\dotnet.exe' test CaptureDesk.slnx -c Release
& '.\src\CaptureDesk.App\bin\Release\net10.0-windows10.0.19041.0\CaptureDesk.App.exe' --verify-ui "$PWD\artifacts\ui-verification.txt"
```

UI 回归在独立 STA 进程中运行，检查截图输入捕获、原图裁切、标注工具与导出、贴图操作、设置导航及草稿隔离。媒体检查验证双向拼接的像素一致性、GIF 解码与时间、真实桌面录制与暂停 / 恢复 / 剪辑，以及 Windows OCR 的成功、缺失语言与取消。诊断启动不会读取或改写用户配置、注册全局快捷键或改写剪贴板。

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish src\CaptureDesk.App\CaptureDesk.App.csproj -p:PublishProfile=Lightweight
& 'C:\Program Files\dotnet\dotnet.exe' publish src\CaptureDesk.App\CaptureDesk.App.csproj -p:PublishProfile=Portable
```

设置位于 `%AppData%\CaptureDesk\settings.json`。

构建安装包需要 Windows、.NET 10 SDK 和 NSIS 3 Unicode 编译器：

```powershell
.\tools\build-installer.ps1 -MakeNsis 'C:\Tools\NSIS\makensis.exe'
```

输出位于 `dist/0.4/release`，包含安装程序及 SHA-256 校验文件。

- `dist/0.4/lightweight`：约 37.3 MiB，依赖已安装的 .NET 10 Desktop Runtime x64。新增 Windows OCR 所需的 WinRT 投影程序集使轻量版增大，未附带 OCR 模型。
- `dist/0.4/portable`：约 90.8 MiB，包含压缩的运行库，无需预装 .NET；首次启动会把所需组件释放到 .NET 缓存，磁盘实际占用大于 EXE 体积。
- `dist/win-x64`：旧版运行目录；本次新构建位于上述两个目录，退出旧版后运行新版。
- 0.4 已移除公式识别及其独立模型、Python 运行环境；文字和表格识别继续使用 Windows OCR，换电脑时需安装所需的 OCR 语言包。

原版约 174 MB，主要体积来自自包含桌面运行库。发布不使用 WPF 不支持的激进裁剪，保证 XAML 和反射功能完整。

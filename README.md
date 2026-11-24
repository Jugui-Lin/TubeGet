# TubeGet

TubeGet 用于解析视频链接、管理下载队列和转换媒体文件。

| 平台 | 源码与使用说明 |
| --- | --- |
| Windows | [WinForms 源码](Windows/源码) · [使用说明](Windows/使用说明.md) |
| macOS | [Electron 源码](macOS) |
| iOS | [SwiftUI 工程与安装说明](iOS/README.md) · [电脑解析服务](iOS/Bridge) |

## 构建

- Windows 使用 `Windows/源码/build.ps1` 编译，随后可用 `build-single.ps1` 打包。先按[第三方组件清单](Windows/components/第三方组件.md)准备各组件，并把 x64 WebView2 SDK 的 Core、WinForms 和 Loader DLL 放到 `Windows/` 目录。
- macOS 应用入口为 `macOS/main.js`。打包时需要准备 Electron，以及应用所需的 yt-dlp、FFmpeg、ffprobe 和 QuickJS。
- iOS 用 Xcode 打开 `iOS/TubeGet.xcodeproj`，选择自己的签名团队。站点解析可连接 [电脑解析服务](iOS/README.md#启动电脑解析服务)。

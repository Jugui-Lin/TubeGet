# TubeGet

TubeGet 是一款 Windows 应用，用于解析视频链接、管理下载队列和转换媒体文件。

[WinForms 源码](Windows/源码) · [使用说明](Windows/使用说明.md)

## 构建

按[第三方组件清单](Windows/components/第三方组件.md)准备各组件，将 x64 WebView2 SDK 的 Core、WinForms 和 Loader DLL 放到 `Windows/` 目录。运行 `Windows/源码/build.ps1` 编译，再运行 `Windows/源码/build-single.ps1` 打包。

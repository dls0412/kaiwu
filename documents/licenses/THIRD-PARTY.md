# 第三方组件与来源

开物文档模块使用以下第三方组件，完整项目来源见仓库根目录 THIRD-PARTY.md。

- ZetaOffice / LibreOffice WebAssembly：来自 https://cdn.zetaoffice.net/zetaoffice_latest/ 。
  原始响应的版本时间、ETag、压缩方式及 SHA256 见 `app/engine/manifest.json`。
  LibreOffice 使用 MPL 2.0 / LGPL 等许可，许可正文及第三方声明随本目录保留。
  对应实现源代码：https://git.libreoffice.org/core ；ZetaOffice 来源：https://zetaoffice.net/ 。
- ZetaJS：MIT，https://github.com/allotropia/zetajs 。
- PDF.js 5.6.205：Apache 2.0，https://github.com/mozilla/pdf.js 。
- pdf-lib 1.17.1：MIT，https://github.com/Hopding/pdf-lib 。
- @pdf-lib/fontkit：MIT，https://github.com/Hopding/fontkit 。
- Microsoft WebView2 SDK 1.0.4314.35：Microsoft SDK 许可，原许可见同目录。
  运行时复用系统已有 Edge WebView2，未分发浏览器运行时。

Windows 微软雅黑和黑体只在用户本机读取使用，没有复制或打包这些字体。



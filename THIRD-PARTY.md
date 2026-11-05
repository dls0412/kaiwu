# Third-party components

Kaiwu application code is distributed under GPL-3.0-or-later. Third-party components retain their own licenses. Dependency binaries are redistributed without modification; implementation sources and build provenance are listed below.

| Component | License / notices | Source |
| --- | --- | --- |
| libmpv / FFmpeg build | GPL family and enabled dependency licenses; see upstream Copyright and build configuration | https://github.com/mpv-player/mpv/tree/3138fc8eea and https://github.com/shinchiro/mpv-winbuild-cmake/tree/bda10a2 |
| FFmpeg | Upstream license and dependency notices | https://ffmpeg.org/legal.html |
| LibreOffice / ZetaOffice WASM | MPL-2.0 / LGPL and bundled notices in documents/licenses | https://git.libreoffice.org/core and https://zetaoffice.net/ |
| ZetaJS | MIT | https://github.com/allotropia/zetajs |
| PDF.js 5.6.205 | Apache-2.0 plus bundled font/codec notices in documents/app/pdf | https://github.com/mozilla/pdf.js |
| pdf-lib 1.17.1 | MIT | https://github.com/Hopding/pdf-lib |
| pdf-lib fontkit | MIT | https://github.com/Hopding/fontkit |
| WebView2 SDK 1.0.4314.35 | Redistributable notices in documents/vendor/LICENSE-WebView2.txt | https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35 |
| Lux 0.24.1 | MIT, documents/licenses/Lux-MIT.txt | https://github.com/iawia002/lux/tree/v0.24.1 |
| whisper.cpp 1.9.2 | MIT, documents/licenses/LICENSE-whisper.cpp.txt | https://github.com/ggml-org/whisper.cpp/tree/v1.9.2 |
| Optional multilingual Whisper small q5_1 model | MIT, documents/licenses/LICENSE-Whisper-model.txt | https://huggingface.co/ggerganov/whisper.cpp and https://github.com/openai/whisper |

Exact upstream media and download release records: tools/engine-source.json and tools/download-engine.json. Speech engine and model hashes: tools/speech-engine.json. The document-engine response dates, URLs and SHA256 values are in documents/app/engine/manifest.json. The complete downloadable dependency bundle is pinned by tools/dependencies.json.

System Windows fonts are read locally at runtime and are not copied into this repository or release. WebView2 uses the separately installed Microsoft runtime; the browser runtime is not bundled. OCR uses Windows APIs and installed recognition languages. No Inno Setup installer is included.

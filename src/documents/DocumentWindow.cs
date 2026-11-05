using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class DocumentWindow : Form
{
    private readonly WebView2 web = new WebView2();
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 192 * 1024 * 1024 };
    private readonly Dictionary<string, string> selectedFiles = new Dictionary<string, string>();
    private readonly HashSet<string> extensions = new HashSet<string>("doc docx odt rtf txt xls xlsx ods csv ppt pptx odp pdf odg tsv".Split(' '));
#if TEST_BUILD
    private readonly string dataFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tests", "document-test-data"));
#else
    private readonly string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlintPlayer", "Documents");
#endif
    private readonly string startupFile;
    private StaticServer server;
    private CoreWebView2Environment webEnvironment;
    private bool dirty, busy, closingApproved, closingAfterSave, handlingFile;
    private List<RecentFile> recents = new List<RecentFile>();
    private Label bootLabel;
    readonly Glint.ToolServices toolServices=new Glint.ToolServices();
    internal string ToolStartupFile;
    internal string ToolStartupMode,ToolStartupLink;internal bool ToolAutoDownload;
    internal Glint.ToolServices ToolsBackend {get{return toolServices;}}
    internal bool Ready { get; private set; }
    internal bool WaitingCloseSave { get { return closingAfterSave; } }
    internal bool HasUnsaved { get { return dirty; } }
    internal Func<DialogResult> CloseChoice { get; set; }
    internal Func<string,string> ConversionDestination { get; set; }
    internal Action<PrintWindow> PreviewCreated { get; set; }
    internal Action<string> OpenMedia,FileOpened,ToolModeChanged;internal Action SwitchTools,SwitchDocument,SwitchHome;
    private string pendingExternal;
    internal Task<string> EvaluateAsync(string script) { return web.CoreWebView2.ExecuteScriptAsync(script); }
    internal void OpenExternal(string path) {
        if (!Ready) { pendingExternal = path; return; }
        if (busy || handlingFile || closingAfterSave) { MessageBox.Show(this,"请先完成当前文档操作。","开物"); return; }
        try { web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new { @event="startup-file", file=OpenFile(path) })); }
        catch(Exception ex) { MessageBox.Show(this,"无法打开文档："+ex.Message,"开物"); }
    }

    internal DocumentWindow(string initialFile)
    {
        startupFile = initialFile;
        Text = "开物 · 文档";
        Size = new Size(1180, 820);
        MinimumSize = new Size(620, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 245, 241);
        try { Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kaiwu.ico")); } catch { }
        web.Dock = DockStyle.Fill;
        Controls.Add(web);
        bootLabel = new Label { Text = initialFile=="--tools"?"正在打开工具…":"正在打开文档…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 11), BackColor = BackColor,ForeColor=Color.FromArgb(89,110,98) };
        Controls.Add(bootLabel);
        toolServices.OpenOutput=path=>{if(OpenMedia!=null)OpenMedia(path);};
        toolServices.Progress=(phase,done,total)=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(new Action(()=>{if(!IsDisposed&&web.CoreWebView2!=null)web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{@event="tool-progress",phase=phase,done=done,total=total}));}));};
        Shown += async delegate { await Initialize(); };
        FormClosing += OnClosing;
        FormClosed += delegate { if (server != null) server.Dispose(); web.Dispose(); };
    }

    private async Task Initialize()
    {
        try
        {
            Directory.CreateDirectory(dataFolder);
            LoadRecents();
            string appFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
            if (!File.Exists(Path.Combine(appFolder, "index.html"))) throw new IOException("应用文件不完整，请将整个文件夹一起解压。");
            server = new StaticServer(appFolder);
            server.Start();
            var options = new CoreWebView2EnvironmentOptions();
#if TEST_BUILD
            string debugPort = Environment.GetEnvironmentVariable("QING_DOCUMENTS_DEBUG_PORT");
            int parsedPort;
            if (Int32.TryParse(debugPort, out parsedPort) && parsedPort > 1024 && parsedPort < 65536)
                options.AdditionalBrowserArguments = "--remote-debugging-port=" + parsedPort;
#endif
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataFolder, "WebView"), options);
            webEnvironment = environment;
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.IsZoomControlEnabled = false;
            web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (e.Uri != server.Origin + "/" && !e.Uri.StartsWith(server.Origin + "/", StringComparison.Ordinal)) e.Cancel = true;
            };
            web.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            web.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
            {
                if (e.PermissionKind != CoreWebView2PermissionKind.ClipboardRead) e.State = CoreWebView2PermissionState.Deny;
            };
            web.CoreWebView2.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
            web.CoreWebView2.WebMessageReceived += OnWebMessage;
            web.CoreWebView2.DocumentTitleChanged += delegate { Text = web.CoreWebView2.DocumentTitle; };
            web.CoreWebView2.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e)
            {
                MessageBox.Show(this, "文档窗口意外停止。请保留原文件，关闭并重新打开开物。", "开物", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            web.CoreWebView2.NavigationCompleted += delegate { bootLabel.Visible = false; };
            web.CoreWebView2.Navigate(server.Origin + (startupFile=="--tools"?"/tools.html":"/"));
        }
        catch (WebView2RuntimeNotFoundException)
        {
            bootLabel.Text = "需要系统 Microsoft Edge WebView2 运行时。\n请在 Windows 中安装或修复 Microsoft Edge WebView2 后重新打开。";
        }
        catch (Exception error) { bootLabel.Text = "无法打开文档\n" + error.Message; }
    }

    private string GetString(Dictionary<string, object> input, string key)
    {
        object value; return input.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
    }
    private bool GetBool(Dictionary<string, object> input, string key)
    {
        object value; return input.TryGetValue(key, out value) && value is bool && (bool)value;
    }
    private void Reply(object id, object result, string error)
    {
        if (IsDisposed || web.CoreWebView2 == null) return;
        web.CoreWebView2.PostWebMessageAsJson(json.Serialize(new { requestId = id, result = result, error = error }));
    }

    private async void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(server.Origin + "/", StringComparison.Ordinal)) return;
        Dictionary<string, object> input;
        try { input = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); }
        catch { return; }
        object requestId = input.ContainsKey("requestId") ? input["requestId"] : null;
        bool ownsFileOperation = false;
        try
        {
            string command = GetString(input, "command");
            if(command=="switchHome"){if(SwitchHome!=null)SwitchHome();Reply(requestId,true,null);return;}
            if(command=="switchMedia"){if(OpenMedia!=null)OpenMedia(null);Reply(requestId,true,null);return;}
            if(command=="switchTools"){if(SwitchTools!=null)SwitchTools();Reply(requestId,true,null);return;}
            if(command=="switchDocument"){if(SwitchDocument!=null)SwitchDocument();Reply(requestId,true,null);return;}
            if(command=="toolMode"){if(ToolModeChanged!=null)ToolModeChanged(GetString(input,"mode"));Reply(requestId,true,null);return;}
            if(command.StartsWith("tool",StringComparison.Ordinal)){Reply(requestId,await toolServices.Handle(this,input),null);return;}
            if (command == "state") { dirty = GetBool(input, "dirty"); busy = GetBool(input, "busy"); return; }
            if (command == "clipboardRead") { Reply(requestId, Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : "", null); return; }
            if (command == "clipboardWrite")
            {
                string text = GetString(input, "text");
                if (!String.IsNullOrEmpty(text)) Clipboard.SetText(text, TextDataFormat.UnicodeText);
                Reply(requestId, true, null); return;
            }
            if (command == "closeResult")
            {
                closingAfterSave = false;
                if (GetBool(input, "ok") && !dirty) { closingApproved = true; Close(); }
                Reply(requestId, true, null); return;
            }
            if (command == "ready")
            {
                if(startupFile=="--tools"){Ready=true;Reply(requestId,new {file=ToolStartupFile==null?null:ToolStartupMode=="transcribe"?(object)new{token=toolServices.Add(ToolStartupFile),name=Path.GetFileName(ToolStartupFile)}:toolServices.Packet(ToolStartupFile),mode=ToolStartupMode,link=ToolStartupLink,start=ToolAutoDownload},null);return;}
                object packet = null;
                string first = pendingExternal ?? startupFile; pendingExternal = null;
                if (!String.IsNullOrEmpty(first) && File.Exists(first)) packet = OpenFile(first);
                Ready = true;
                Reply(requestId, new { file = packet }, null); return;
            }
            if (command == "recents")
            {
                Reply(requestId, recents.Where(r => File.Exists(r.path)).Take(16).Select(r => new
                {
                    id = r.id, name = Path.GetFileName(r.path), folder = Path.GetDirectoryName(r.path),
                    date = r.opened.ToLocalTime().ToString("MM-dd HH:mm")
                }).ToArray(), null); return;
            }
            if (command == "clearRecents") { recents.Clear(); SaveRecents(); Reply(requestId, true, null); return; }
            if (handlingFile) throw new InvalidOperationException("请先完成当前文件操作。");
            if (command == "open")
            {
                handlingFile = true; ownsFileOperation = true;
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "打开文件";
                    dialog.Filter = Glint.Player.OpenFileFilter;
                    dialog.Multiselect = false;
                    if (dialog.ShowDialog(this) != DialogResult.OK) Reply(requestId, new { canceled = true }, null);
                    else if (Glint.Player.IsDocument(dialog.FileName)) Reply(requestId, OpenFile(dialog.FileName), null);
                    else { Reply(requestId,new { canceled=true },null); if(OpenMedia!=null)OpenMedia(dialog.FileName); }
                }
            }
            else if (command == "openRecent")
            {
                string id = GetString(input, "id");
                var record = recents.FirstOrDefault(r => r.id == id);
                if (record == null) throw new IOException("这条文件记录已不存在，请重新选择文件。");
                Reply(requestId, OpenFile(record.path), null);
            }
            else if (command == "print")
            {
                handlingFile = true; ownsFileOperation = true;
                string encoded = GetString(input, "base64");
                if (encoded.Length > 176 * 1024 * 1024) throw new IOException("打印文件过大，请减少页数后重试。");
                byte[] pdf = Convert.FromBase64String(encoded);
                if (pdf.Length < 5 || Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-") throw new IOException("无法生成有效的打印文件。");
                string resource = server.AddPrintDocument(pdf);
                try
                {
                    var completed = new TaskCompletionSource<bool>();
                    string printName = GetString(input, "name");
                    // Start the modal loop after the WebView2 message callback has returned.
                    BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            using (var preview = new PrintWindow(webEnvironment, server.Origin + resource, printName)) {
                                if(PreviewCreated!=null)PreviewCreated(preview);
                                preview.ShowDialog(TopLevelControl as Form ?? this);
                            }
                            completed.SetResult(true);
                        }
                        catch (Exception error) { completed.SetException(error); }
                    }));
                    await completed.Task;
                    Reply(requestId, new { closed = true }, null);
                }
                finally { server.RemovePrintDocument(resource); }
            }
            else if (command == "save")
            {
                handlingFile = true; ownsFileOperation = true;
                string ext = GetString(input, "extension").ToLowerInvariant();
                if (!extensions.Contains(ext)) throw new IOException("不支持这个保存格式。");
                string path = null;
                string token = GetString(input, "token");
                if (!GetBool(input, "saveAs") && selectedFiles.ContainsKey(token)) path = selectedFiles[token];
                if (path != null && Path.GetExtension(path).ToLowerInvariant() != "." + ext) path = null;
                if (path == null && ConversionDestination != null) path=ConversionDestination(ext);
                if (path == null)
                {
                    using (var dialog = new SaveFileDialog())
                    {
                        dialog.Title = GetBool(input, "exportOnly") ? "导出 PDF" : "保存文档";
                        dialog.Filter = ext.ToUpperInvariant() + " 文件|*." + ext;
                        dialog.DefaultExt = ext;
                        dialog.AddExtension = true;
                        dialog.OverwritePrompt = true;
                        dialog.FileName = Path.GetFileName(GetString(input, "name"));
                        if (dialog.ShowDialog(this) != DialogResult.OK) { Reply(requestId, new { canceled = true }, null); return; }
                        path = dialog.FileName;
                    }
                }
                if (Path.GetExtension(path).ToLowerInvariant() != "." + ext) throw new IOException("保存文件的扩展名需要是 ." + ext);
                string encoded = GetString(input, "base64");
                if (encoded.Length > 96 * 1024 * 1024) throw new IOException("保存文件过大。");
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length == 0) throw new IOException("文档导出为空，已取消保存。");
                string destination = path;
                await Task.Run(delegate { AtomicWrite(destination, bytes); });
                token = Guid.NewGuid().ToString("N");
                selectedFiles[token] = path;
                AddRecent(path);if(FileOpened!=null)FileOpened(path);
                Reply(requestId, new { name = Path.GetFileName(path), token = token }, null);
            }
            else throw new InvalidOperationException("不支持的操作。");
        }
        catch (Exception error) { Reply(requestId, null, error.Message); }
        finally { if (ownsFileOperation) handlingFile = false; }
    }

    private object OpenFile(string path)
    {
        path = Path.GetFullPath(path);
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!extensions.Contains(ext)) throw new IOException("暂不支持这个文件格式。");
        var info = new FileInfo(path);
        if (info.Length > 64 * 1024 * 1024) throw new IOException("这份文件超过 64 MB，请先使用较小的文件。");
        byte[] bytes = File.ReadAllBytes(path);
        string token = Guid.NewGuid().ToString("N");
        selectedFiles[token] = path;
        AddRecent(path);if(FileOpened!=null)FileOpened(path);
        return new { token = token, name = Path.GetFileName(path), base64 = Convert.ToBase64String(bytes) };
    }

    private static void AtomicWrite(string target, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(target), ".qing-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(target)) File.Replace(temp, target, null);
            else File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (closingApproved) return;
        if (busy || handlingFile || closingAfterSave)
        { e.Cancel = true; MessageBox.Show(this, "请等当前文件操作完成后再关闭。", "开物"); return; }
        if (!dirty) return;
        DialogResult result = CloseChoice != null ? CloseChoice() : MessageBox.Show(this, "文件有未保存的修改，保存后再关闭吗？", "开物", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.No) return;
        e.Cancel = true;
        if (result == DialogResult.Yes)
        {
            closingAfterSave = true;
            web.CoreWebView2.PostWebMessageAsJson("{\"event\":\"save-before-close\"}");
        }
    }

    private void LoadRecents()
    {
        try
        {
            string path = Path.Combine(dataFolder, "recent-files.json");
            if (File.Exists(path)) recents = json.Deserialize<List<RecentFile>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<RecentFile>();
        }
        catch { recents = new List<RecentFile>(); }
    }
    private void AddRecent(string path)
    {
        recents.RemoveAll(r => String.Equals(r.path, path, StringComparison.OrdinalIgnoreCase));
        recents.Insert(0, new RecentFile { id = Guid.NewGuid().ToString("N"), path = path, opened = DateTime.UtcNow });
        recents = recents.Take(20).ToList();
        SaveRecents();
    }
    private void SaveRecents()
    {
        try { AtomicWrite(Path.Combine(dataFolder, "recent-files.json"), Encoding.UTF8.GetBytes(json.Serialize(recents))); }
        catch { /* A recent-file history failure never turns a successful document save into a failure. */ }
    }
    public sealed class RecentFile { public string id { get; set; } public string path { get; set; } public DateTime opened { get; set; } }
}

internal sealed class StaticServer : IDisposable
{
    private readonly string root;
    private readonly TcpListener listener;
    private volatile bool running;
    private readonly ConcurrentDictionary<string, byte[]> printDocuments = new ConcurrentDictionary<string, byte[]>();
    public string Origin { get; private set; }
    public string AddPrintDocument(byte[] bytes)
    {
        string key = "/print/" + Guid.NewGuid().ToString("N") + ".pdf";
        printDocuments[key] = bytes;
        return key;
    }
    public void RemovePrintDocument(string key) { byte[] ignored; printDocuments.TryRemove(key, out ignored); }

    public StaticServer(string folder)
    {
        root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        listener = new TcpListener(IPAddress.Loopback, 0);
    }
    public void Start()
    {
        listener.Start();
        Origin = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        running = true;
        Task.Run(async delegate
        {
            while (running)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
                }
                catch { if (!running) break; }
            }
        });
    }
    private void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 15000; client.SendTimeout = 60000;
                using (NetworkStream stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                    string line = reader.ReadLine();
                    if (line == null || line.Length > 8192) return;
                    string[] request = line.Split(' ');
                    if (request.Length != 3 || (request[0] != "GET" && request[0] != "HEAD")) { Respond(stream, 405, "text/plain", Encoding.UTF8.GetBytes("Method not allowed"), null, false); return; }
                    string host = "", fetchSite = "", acceptEncoding = "";
                    for (int n = 0; n < 80; n++)
                    {
                        string header = reader.ReadLine(); if (String.IsNullOrEmpty(header)) break;
                        int colon = header.IndexOf(':'); if (colon < 0) continue;
                        string key = header.Substring(0, colon).Trim().ToLowerInvariant();
                        string value = header.Substring(colon + 1).Trim();
                        if (key == "host") host = value;
                        else if (key == "sec-fetch-site") fetchSite = value;
                        else if (key == "accept-encoding") acceptEncoding = value;
                    }
                    if ("http://" + host != Origin || fetchSite == "cross-site") { Respond(stream, 403, "text/plain", Encoding.UTF8.GetBytes("Forbidden"), null, false); return; }
                    string urlPath = Uri.UnescapeDataString(request[1].Split('?')[0]);
                    if (urlPath.IndexOf('\0') >= 0 || urlPath.Contains("\\")) throw new IOException();
                    if (urlPath.StartsWith("/print/", StringComparison.Ordinal))
                    {
                        byte[] pdf;
                        if (printDocuments.TryGetValue(urlPath, out pdf)) Respond(stream, 200, "application/pdf", pdf, null, request[0] == "HEAD");
                        else Respond(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"), null, request[0] == "HEAD");
                        return;
                    }
                    string path;
                    if (urlPath == "/system-font.ttf") path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
                    else if (urlPath == "/pdf-font.ttf") path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "simhei.ttf");
                    else
                    {
                        if (urlPath == "/") urlPath = "/index.html";
                        path = Path.GetFullPath(Path.Combine(root, urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException();
                    }
                    string encoding = null;
                    if (acceptEncoding.Contains("br") && File.Exists(path + ".br")) { path += ".br"; encoding = "br"; }
                    if (!File.Exists(path)) { Respond(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"), null, false); return; }
                    string mimePath = encoding == "br" ? path.Substring(0, path.Length - 3) : path;
                    string mime = Mime(mimePath);
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        WriteHeaders(stream, 200, mime, file.Length, encoding);
                        if (request[0] != "HEAD") file.CopyTo(stream, 131072);
                    }
                }
            }
            catch { /* Client disconnects and malformed requests do not stop the local server. */ }
        }
    }
    private static string Mime(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".html": return "text/html; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".js": case ".mjs": return "application/javascript; charset=utf-8";
            case ".wasm": return "application/wasm";
            case ".json": return "application/json; charset=utf-8";
            case ".svg": return "image/svg+xml";
            case ".ttc": case ".ttf": return "font/collection";
            default: return "application/octet-stream";
        }
    }
    private static void Respond(Stream stream, int status, string type, byte[] bytes, string encoding, bool head)
    { WriteHeaders(stream, status, type, bytes.Length, encoding); if (!head) stream.Write(bytes, 0, bytes.Length); }
    private static void WriteHeaders(Stream stream, int status, string type, long length, string encoding)
    {
        string headers = "HTTP/1.1 " + status + (status == 200 ? " OK" : " Error") + "\r\nContent-Type: " + type +
            "\r\nContent-Length: " + length + "\r\nConnection: close\r\nCache-Control: no-cache" +
            "\r\nCross-Origin-Opener-Policy: same-origin\r\nCross-Origin-Embedder-Policy: require-corp" +
            "\r\nCross-Origin-Resource-Policy: same-origin\r\nX-Content-Type-Options: nosniff" +
            (encoding == null ? "" : "\r\nContent-Encoding: " + encoding) + "\r\n\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(headers); stream.Write(bytes, 0, bytes.Length);
    }
    public void Dispose() { running = false; listener.Stop(); }
}

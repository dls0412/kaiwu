using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class PrintWindow : Form
{
    private readonly WebView2 preview = new WebView2();
    private readonly Button print = new Button();
    private readonly Label status = new Label();
    private readonly CoreWebView2Environment environment;
    private readonly string documentUrl;

    internal PrintWindow(CoreWebView2Environment webEnvironment, string url, string name)
    {
        environment = webEnvironment; documentUrl = url;
        Text = "打印预览 · " + name;
        Size = new Size(1000, 800); MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.White;
        ShowInTaskbar = false;
        try { Icon = new Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kaiwu.ico")); } catch { }
        preview.Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(16, 9, 16, 9), BackColor = Color.White };
        print.Text = "打印…  Ctrl+P"; print.Enabled = false; print.Dock = DockStyle.Right; print.Width = 130;
        print.BackColor = Color.FromArgb(48, 91, 78); print.ForeColor = Color.White; print.FlatStyle = FlatStyle.Flat;
        print.FlatAppearance.BorderSize = 0;
        status.Text = "正在准备分页预览…"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Color.FromArgb(97, 106, 100);
        bar.Controls.Add(status); bar.Controls.Add(print);
        Controls.Add(preview); Controls.Add(bar);
        print.Click += delegate { ShowSystemPrint(); };
        Shown += async delegate { await Initialize(); };
        FormClosed += delegate { preview.Dispose(); };
    }

    private async Task Initialize()
    {
        try
        {
            await preview.EnsureCoreWebView2Async(environment);
            preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            preview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            preview.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            preview.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            preview.CoreWebView2.Settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Print | CoreWebView2PdfToolbarItems.Save | CoreWebView2PdfToolbarItems.SaveAs;
            preview.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
            { if (e.Uri != documentUrl) e.Cancel = true; };
            preview.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            preview.CoreWebView2.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
            preview.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
            preview.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess) { status.Text = "打印预览未能打开，请关闭后重试。"; return; }
                status.Text = "选择打印机、页码、份数；更多选项见打印机首选项。";
                print.Enabled = true;
            };
            preview.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.P)
                { e.Handled = true; e.SuppressKeyPress = true; ShowSystemPrint(); }
            };
            preview.CoreWebView2.Navigate(documentUrl);
        }
        catch (Exception error) { status.Text = "无法准备打印：" + error.Message; }
    }

    private void ShowSystemPrint()
    {
        if (!print.Enabled || preview.CoreWebView2 == null) return;
        try { preview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.System); }
        catch (Exception error) { MessageBox.Show(this, "无法打开打印窗口：\n" + error.Message, "轻文档", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

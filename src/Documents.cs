using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace Glint {
partial class Player {
    public static readonly string[] DocumentExtensions=".doc .docx .odt .rtf .txt .xls .xlsx .ods .csv .ppt .pptx .odp .pdf .odg".Split(' ');
    public static bool IsDocument(string path){return DocumentExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());}
    public const string OpenFileFilter="支持的文件|*.doc;*.docx;*.odt;*.rtf;*.txt;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp;*.pdf;*.odg;*.mp4;*.mkv;*.mov;*.avi;*.webm;*.flv;*.ts;*.m2ts;*.mts;*.m4v;*.wmv;*.mpg;*.mpeg;*.vob;*.3gp;*.ogv;*.rm;*.rmvb;*.asf;*.mxf;*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif;*.ico;*.zip;*.md;*.json;*.log;*.srt;*.vtt|文档|*.doc;*.docx;*.odt;*.rtf;*.txt;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp;*.pdf;*.odg|图片|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif;*.ico|所有文件|*.*";
    DocumentWindow document;
    Panel documentHost=new Panel();
    FlatButton documentButton=new FlatButton("文档");
    bool documentMode,pendingShellClose;
    Rectangle beforeDocumentBounds;Size beforeDocumentMinimum;
    void InitializeDocuments(){
        documentHost.Dock=DockStyle.Fill;documentHost.Visible=false;Controls.Add(documentHost);
        footer.Controls.Add(documentButton);documentButton.Size=new Size(72,34);documentButton.Location=new Point(124,30);documentButton.Click+=(s,e)=>EnterDocuments(null);
        var bar=new Panel{Dock=DockStyle.Top,Height=38,BackColor=BackColor};
        var openFile=new FlatButton("打开文件…"){Location=new Point(10,2),Size=new Size(94,34)};
        var media=new FlatButton("图片 / 视频"){Location=new Point(120,2),Size=new Size(100,34)};
        var label=new Label{Text="文档",Location=new Point(240,2),Size=new Size(120,34),TextAlign=ContentAlignment.MiddleLeft,ForeColor=ForeColor};
        bar.Controls.AddRange(new Control[]{openFile,media,label});documentHost.Controls.Add(bar);bar.Visible=false;
        openFile.Click+=(s,e)=>Open();media.Click+=(s,e)=>LeaveDocuments();
        var tools=new FlatButton("文件工具"){Location=new Point(375,2),Size=new Size(95,34)};bar.Controls.Add(tools);tools.Click+=(s,e)=>EnterTools();
    }
    void PrepareWorkspace(){
        HideHome();SetWindowChrome(false);
        if(fullscreen)ToggleFullscreen();
        if(!documentMode){beforeDocumentBounds=Bounds;beforeDocumentMinimum=MinimumSize;StopGif();engine.Set("pause","yes");documentMode=true;
            MinimumSize=new Size(680,480);}
        video.Visible=images.Visible=footer.Visible=false;documentHost.Visible=true;documentHost.BringToFront();
    }
    void EnterDocuments(string path){
        if(toolsWindow!=null&&!toolsWindow.IsDisposed)toolsWindow.Hide();
        PrepareWorkspace();
        if(document==null||document.IsDisposed){
            document=new DocumentWindow(path);document.TopLevel=false;document.FormBorderStyle=FormBorderStyle.None;document.MinimumSize=Size.Empty;document.Dock=DockStyle.Fill;
            document.OpenMedia=file=>{if(file==null)LeaveDocuments();else LoadFiles(new[]{file});};document.FileOpened=RecentStore.Record;document.SwitchHome=ShowHome;document.SwitchTools=EnterTools;document.SwitchDocument=()=>EnterDocuments(null);
            document.TextChanged+=(s,e)=>{if(documentMode&&document!=null&&document.Visible){Text=document.Text.Replace(" · 轻文档"," · 开物");if(shellBar!=null){shellBar.Active="document";KeepShell();}}};
            document.FormClosed+=(s,e)=>{if(pendingShellClose&&!closing&&!IsDisposed)BeginInvoke((Action)(()=>{if(!IsDisposed)Close();}));};
            documentHost.Controls.Add(document);document.BringToFront();document.Show();
        }else {document.Show();document.BringToFront();if(path!=null)document.OpenExternal(path);}
        Text=document.Text.Replace(" · 轻文档"," · 开物");if(shellBar!=null){shellBar.Active="document";KeepShell();}
    }
    void LeaveDocuments(){
        if(!documentMode)return;documentMode=false;documentHost.Visible=false;MinimumSize=beforeDocumentMinimum;if(WindowState==FormWindowState.Normal)Bounds=beforeDocumentBounds;
        footer.Visible=true;images.Visible=imageMode&&images.HasImage;video.Visible=!images.Visible;Text=loaded?Path.GetFileName(current)+" · 开物":"开物 · 视频、图片与文档";Touch();
    }
    bool CloseDocuments(){
        if(toolsWindow!=null&&!toolsWindow.IsDisposed){if(toolsWindow.HasUnsaved&&!toolsWindow.Visible)EnterTools();pendingShellClose=true;toolsWindow.Close();if(!toolsWindow.IsDisposed){pendingShellClose=toolsWindow.WaitingCloseSave;return false;}pendingShellClose=false;}
        if(document==null||document.IsDisposed)return true;
        if(document.HasUnsaved&&!documentMode)EnterDocuments(null);
        pendingShellClose=true;document.Close();
        if(!document.IsDisposed){pendingShellClose=document.WaitingCloseSave;return false;}pendingShellClose=false;return true;
    }
}
}

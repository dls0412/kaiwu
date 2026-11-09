using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Diagnostics;

namespace Glint {
static class Native {
    const string Dll = "libmpv-2.dll";
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr mpv_create();
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern int mpv_initialize(IntPtr h);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern int mpv_set_option_string(IntPtr h, byte[] n, byte[] v);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern int mpv_set_property_string(IntPtr h, byte[] n, byte[] v);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr mpv_get_property_string(IntPtr h, byte[] n);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern int mpv_command(IntPtr h, IntPtr[] args);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern void mpv_free(IntPtr p);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] public static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    public static byte[] U(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }
    public static string Read(IntPtr p) { if(p==IntPtr.Zero) return ""; int n=0; while(Marshal.ReadByte(p,n)!=0) n++; byte[] b=new byte[n]; Marshal.Copy(p,b,0,n); return Encoding.UTF8.GetString(b); }
}
sealed class Engine : IDisposable {
    public IntPtr Handle;
    public Engine(IDictionary<string,string> options){Handle=Native.mpv_create();if(Handle==IntPtr.Zero)throw new Exception("无法创建转换器");try{foreach(var pair in options)Option(pair.Key,pair.Value);if(Native.mpv_initialize(Handle)<0)throw new Exception("转换器初始化失败");}catch{Dispose();throw;}}
    public Engine(IntPtr wid) {
        Handle=Native.mpv_create(); if(Handle==IntPtr.Zero) throw new Exception("无法创建解码器");
        Option("config","no"); Option("terminal","no"); Option("input-default-bindings","no");
        Option("input-vo-keyboard","yes"); Option("input-cursor","yes"); Option("osc","no");
        Option("input-builtin-drag-and-drop","no");
        Option("idle","yes"); Option("keep-open","yes"); Option("hwdec","auto-safe");
        Option("vo","gpu"); Option("gpu-api","d3d11"); Option("wid",wid.ToInt64().ToString());
        Option("volume","70"); Option("sub-auto","fuzzy");
        if(Native.mpv_initialize(Handle)<0) { Dispose(); throw new Exception("解码器初始化失败"); }
    }
    void Option(string k,string v) { int r=Native.mpv_set_option_string(Handle,Native.U(k),Native.U(v)); if(r<0) throw new Exception("解码选项失败："+k); }
    public string Get(string k) { IntPtr p=Native.mpv_get_property_string(Handle,Native.U(k)); try{return Native.Read(p);} finally{if(p!=IntPtr.Zero)Native.mpv_free(p);} }
    public double Number(string k) { double n; return double.TryParse(Get(k),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out n)?n:0; }
    public void Set(string k,string v) { Native.mpv_set_property_string(Handle,Native.U(k),Native.U(v)); }
    public int Command(params string[] args) {
        IntPtr[] a=new IntPtr[args.Length+1];
        try { for(int i=0;i<args.Length;i++){byte[] b=Native.U(args[i]); a[i]=Marshal.AllocHGlobal(b.Length); Marshal.Copy(b,0,a[i],b.Length);} return Native.mpv_command(Handle,a); }
        finally {foreach(IntPtr p in a)if(p!=IntPtr.Zero)Marshal.FreeHGlobal(p);}
    }
    public void Dispose() {if(Handle!=IntPtr.Zero){Native.mpv_terminate_destroy(Handle);Handle=IntPtr.Zero;}}
}
class Surface : Panel {
    public Action MouseActivity;
    public Action ClickVideo;
    public Action Fullscreen;
    public Action<string[]> DropFiles;
    public string Message="";
    public bool Loaded;
    public bool Audio;public string AudioTitle="";
    public Surface() {
        BackColor=Color.FromArgb(15,17,21); AllowDrop=true;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        MouseMove+=(s,e)=>{if(MouseActivity!=null)MouseActivity();};
        MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left && ClickVideo!=null)ClickVideo();};
        MouseDoubleClick+=(s,e)=>{if(Fullscreen!=null)Fullscreen();};
        DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
        DragDrop+=(s,e)=>{if(DropFiles!=null)DropFiles((string[])e.Data.GetData(DataFormats.FileDrop));};
    }
    protected override void OnPaintBackground(PaintEventArgs e) {if(!Loaded)base.OnPaintBackground(e);}
    protected override void OnPaint(PaintEventArgs e) {
        if(Loaded)return;
        if(Audio){var graphics=e.Graphics;Center(graphics,"♫",Height/2-95,52,Color.FromArgb(174,204,194));Center(graphics,AudioTitle,Height/2,14,Color.FromArgb(225,228,232));Center(graphics,"音频播放 · 空格暂停 · 方向键快进 / 音量",Height/2+40,9,Color.Gray);return;}
        var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        float x=Width/2f,y=Height/2f-32;
        using(var p=new Pen(Color.FromArgb(62,69,80),1.4f))g.DrawEllipse(p,x-32,y-32,64,64);
        using(var b=new SolidBrush(Color.FromArgb(174,204,194)))g.FillPolygon(b,new[]{new PointF(x-6,y-11),new PointF(x-6,y+11),new PointF(x+12,y)});
        Center(g,"让画面，简单发生",y+57,14,Color.FromArgb(225,228,232));
        Center(g,Message.Length>0?Message:"拖入视频、图片或文档，或点击下方打开文件",y+90,9,Color.FromArgb(123,131,145));
    }
    void Center(Graphics g,string s,float y,float size,Color color) {using(var f=new Font("Microsoft YaHei UI",size))using(var b=new SolidBrush(color)){var z=g.MeasureString(s,f);g.DrawString(s,f,b,(Width-z.Width)/2,y);}}
}
class FlatButton : Control {
    public bool Hover;
    public FlatButton(string text) {Text=text; Size=new Size(44,34); Cursor=Cursors.Hand; ForeColor=Color.FromArgb(210,215,223); SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); MouseEnter+=(s,e)=>{Hover=true;Invalidate();}; MouseLeave+=(s,e)=>{Hover=false;Invalidate();};}
    protected override void OnPaint(PaintEventArgs e) {if(Hover)e.Graphics.Clear(Color.FromArgb(43,47,56)); using(var font=new Font("Segoe UI",10))TextRenderer.DrawText(e.Graphics,Text,font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
}
class SeekBar : Control {
    public double Fraction;
    public bool Dragging;
    public Action<double> Seek;
    public SeekBar() {Height=22; Cursor=Cursors.Hand; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;Dragging=true;Capture=true;MoveTo(e.X);}; MouseMove+=(s,e)=>{if(Dragging)MoveTo(e.X);}; MouseUp+=(s,e)=>{if(!Dragging)return;MoveTo(e.X);Dragging=false;Capture=false;if(Seek!=null)Seek(Fraction);};}
    void MoveTo(int x){Fraction=Math.Max(0,Math.Min(1,x/(double)Math.Max(1,Width)));Invalidate();}
    protected override void OnPaint(PaintEventArgs e) {var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=new Pen(Color.FromArgb(52,57,66),3))g.DrawLine(p,0,Height/2,Width,Height/2);using(var p=new Pen(Color.FromArgb(170,211,193),3))g.DrawLine(p,0,Height/2,(int)(Fraction*Width),Height/2);using(var b=new SolidBrush(Color.FromArgb(207,231,221)))g.FillEllipse(b,(float)(Fraction*Width)-4,Height/2-4,8,8);}
}
partial class Player : Form {
    Engine engine;
    ImageCanvas images=new ImageCanvas();bool imageMode,imageDecode,imageReady;DateTime imageDeadline;
    Rectangle videoBounds;Size fullscreenFrame;
    FlatButton previousImage=new FlatButton("‹"),nextImage=new FlatButton("›");
    Surface video=new Surface(); Panel footer=new Panel(); SeekBar seek=new SeekBar();
    FlatButton play=new FlatButton("▶"),open=new FlatButton("打开"),speed=new FlatButton("1×"),sound=new FlatButton("70%"),more=new FlatButton("···"),full=new FlatButton("⛶");
    Label time=new Label(); Timer timer=new Timer(); ContextMenuStrip menu=new ContextMenuStrip();
    List<string> playlist=new List<string>(); int index=-1;
    bool audioMode;
    bool loaded,fullscreen,closing; string current=""; DateTime activity=DateTime.Now; Point lastCursor;
    Rectangle savedBounds; FormWindowState savedState; string[] initial;
#if TEST_BUILD
    readonly string settingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-settings.txt");
#else
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlintPlayer","settings.txt");
#endif
    public string CaptureFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"开物");
    GifRecorder recording,exporting; double recordSeconds,lastRecordTick,lastFrameTime; string notice="";DateTime noticeUntil;
    public Player(string[] args) {
        initial=args; Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); Text="开物"; BackColor=Color.FromArgb(22,25,31); ForeColor=Color.White;
        Font=new Font("Microsoft YaHei UI",9); ClientSize=new Size(1100,730); MinimumSize=new Size(760,520); StartPosition=FormStartPosition.CenterScreen; KeyPreview=true;
        video.Dock=DockStyle.Fill; footer.Dock=DockStyle.Bottom; footer.Height=78;footer.BackColor=BackColor;
        Controls.Add(video);Controls.Add(images);Controls.Add(footer);images.Visible=false;footer.Controls.AddRange(new Control[]{seek,play,open,speed,sound,more,full,time,previousImage,nextImage});previousImage.Visible=nextImage.Visible=false;
        images.Activity=Touch;images.Fullscreen=ToggleFullscreen;images.DropFiles=LoadFiles;images.ViewChanged=UpdateImageInfo;images.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)ShowMenu(images,e.Location);};previousImage.Click+=(s,e)=>Next(-1);nextImage.Click+=(s,e)=>Next(1);
        time.ForeColor=Color.FromArgb(139,147,161);time.AutoSize=false;time.TextAlign=ContentAlignment.MiddleLeft;
        time.Text="00:00 / 00:00";
        footer.Resize+=(s,e)=>LayoutFooter();
        video.MouseActivity=Touch; video.ClickVideo=()=>{if(loaded)Toggle();else Open();}; video.Fullscreen=ToggleFullscreen;video.DropFiles=LoadFiles;
        play.Click+=(s,e)=>{if(loaded)Toggle();else Open();}; open.Click+=(s,e)=>Open();full.Click+=(s,e)=>ToggleFullscreen();
        sound.Click+=(s,e)=>{if(imageMode)RotateImage();else engine.Command("cycle","mute");Touch();};speed.Click+=(s,e)=>{if(imageMode){if(images.IsFit)images.ActualSize();else images.Fit();}else ShowSpeed(speed);};
        more.Click+=(s,e)=>ShowMenu(more,new Point(0,-menu.Height));
        video.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)ShowMenu(video,e.Location);};
        footer.MouseMove+=(s,e)=>Touch(); foreach(Control c in footer.Controls)c.MouseMove+=(s,e)=>Touch();
        seek.Seek=f=>{if(loaded)engine.Command("seek",(f*100).ToString("F4",System.Globalization.CultureInfo.InvariantCulture),"absolute-percent+exact");Touch();};
        MouseWheel+=(s,e)=>Volume(e.Delta>0?5:-5);video.MouseWheel+=(s,e)=>Volume(e.Delta>0?5:-5);
        Shown+=(s,e)=>StartEngine();
        timer.Interval=100;timer.Tick+=(s,e)=>TickPlayer();
        FormClosing+=(s,e)=>{if(!CloseDocuments()){e.Cancel=true;return;}closing=true;timer.Stop();StopGif();if(exporting!=null)exporting.Work.Wait();images.Clear();SaveSettings();if(engine!=null)engine.Dispose();};
        InitializeDocuments();InitializeTools();InitializeHome();InitializeShell();
        LayoutFooter();
    }
    void LayoutFooter() {int w=footer.Width,y=imageMode?11:30;open.Visible=time.Visible=!imageMode||w>=530;seek.SetBounds(22,0,w-44,22);play.Location=new Point(16,y);previousImage.Location=new Point(10,y);nextImage.Location=new Point(54,y);open.SetBounds(imageMode?101:65,y,52,34);time.SetBounds(imageMode?166:132,y,imageMode?Math.Max(60,w-378):190,34);speed.Location=new Point(w-202,y);sound.SetBounds(w-152,y,56,34);more.Location=new Point(w-94,y);full.Location=new Point(w-48,y);}
    void ImageMode(bool enabled){bool wasImage=imageMode;if(enabled&&!wasImage)videoBounds=fullscreen?savedBounds:WindowState==FormWindowState.Normal?Bounds:RestoreBounds;imageMode=enabled;MinimumSize=enabled?new Size(320,260):new Size(540,340);seek.Visible=play.Visible=!enabled;previousImage.Visible=nextImage.Visible=enabled;footer.Height=enabled?56:78;if(!enabled){images.Clear();images.Visible=false;video.Visible=true;speed.Text="1×";sound.Text="70%";if(wasImage&&!videoBounds.IsEmpty){if(fullscreen){savedBounds=videoBounds;savedState=FormWindowState.Normal;}else if(WindowState==FormWindowState.Normal)Bounds=videoBounds;}}LayoutFooter();}
    void AdjustImageWindow(){
        if(!images.HasImage||(!fullscreen&&WindowState!=FormWindowState.Normal))return;
        Rectangle area=Screen.FromControl(this).WorkingArea,previous=fullscreen?savedBounds:Bounds;
        Size frame=fullscreen?fullscreenFrame:SizeFromClientSize(Size.Empty),source=images.ImageSize;
        int maxWidth=Math.Max(1,(int)(area.Width*0.85)-frame.Width),maxHeight=Math.Max(1,(int)(area.Height*0.85)-frame.Height-footer.Height-(shellBar==null?0:shellBar.Height));
        double scale=Math.Min(1,Math.Min(maxWidth/(double)source.Width,maxHeight/(double)source.Height));
        Size outer=new Size(Math.Max(320-frame.Width,(int)Math.Round(source.Width*scale))+frame.Width,Math.Max(260-frame.Height-footer.Height-(shellBar==null?0:shellBar.Height),(int)Math.Round(source.Height*scale))+footer.Height+frame.Height+(shellBar==null?0:shellBar.Height));
        outer.Width=Math.Min(outer.Width,area.Width);outer.Height=Math.Min(outer.Height,area.Height);
        int x=Math.Max(area.Left,Math.Min(area.Right-outer.Width,previous.Left+(previous.Width-outer.Width)/2)),y=Math.Max(area.Top,Math.Min(area.Bottom-outer.Height,previous.Top+(previous.Height-outer.Height)/2));
        Rectangle target=new Rectangle(new Point(x,y),outer);if(fullscreen){savedBounds=target;savedState=FormWindowState.Normal;}else Bounds=target;
        images.Fit();
    }
    void RotateImage(){images.Rotate();AdjustImageWindow();}
    void UpdateImageInfo(){if(!imageMode||!images.HasImage)return;time.Text=images.ImageSize.Width+" × "+images.ImageSize.Height+" · "+(index+1)+"/"+playlist.Count;speed.Text=images.IsFit?"适应":Math.Round(images.Zoom*100)+"%";sound.Text="旋转";speed.Invalidate();sound.Invalidate();}
    void StartEngine() {
        SetWindowChrome(false);float uiScale=KaiwuUi.Scale(this);if(uiScale>1&&WindowState==FormWindowState.Normal){ClientSize=new Size((int)(1100*uiScale),(int)(730*uiScale));CenterToScreen();}
        try {engine=new Engine(video.Handle);BindInput();ReadSettings();timer.Start();if(initial.Length==1&&initial[0]=="--documents")EnterDocuments(null);else if(initial.Length>0)LoadFiles(initial);}
        catch(Exception ex){MessageBox.Show(this,"启动失败："+ex.Message+"\n请将开物.exe 与 libmpv-2.dll 放在同一目录。","开物");Close();}
    }
    void Open() {Touch();using(var d=new OpenFileDialog()){d.Title="打开文件";d.Multiselect=true;d.Filter=OpenFileFilter;if(d.ShowDialog(this)==DialogResult.OK)LoadFiles(d.FileNames);}}
    public static readonly string[] ImageExtensions=".jpg .jpeg .jfif .png .bmp .gif .tif .tiff .webp .avif .ico".Split(' ');
    static bool IsImage(string path){return ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());}
    void LoadFiles(string[] files) {
        var existing=files.Where(File.Exists).ToArray();if(existing.Length==0)return;
        if(existing.Length==1&&loaded&&!imageMode&&!homeMode&&!documentMode&&new[]{".srt",".ass",".ssa",".vtt",".sub"}.Contains(Path.GetExtension(existing[0]).ToLowerInvariant())){engine.Command("sub-add",existing[0],"select");Touch();return;}
        foreach(string file in existing)RecentStore.Record(file);HideHome();
        if(ToolFile(existing[0])){EnterToolsFile(existing[0]);return;}
        if(IsDocument(existing[0])){EnterDocuments(existing[0]);return;}
        LeaveDocuments();SetWindowChrome(true);if(shellBar!=null){shellBar.Active="media";KeepShell();}
        playlist=existing.ToList();index=0;
        if(existing.Length==1&&IsImage(existing[0])){try{playlist=Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(existing[0]))).Where(IsImage).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();index=playlist.FindIndex(p=>string.Equals(Path.GetFullPath(p),Path.GetFullPath(existing[0]),StringComparison.OrdinalIgnoreCase));}catch{playlist=existing.ToList();index=0;}}
        LoadCurrent();
    }
    void LoadCurrent() {if(index<0||index>=playlist.Count)return;if(ToolFile(playlist[index])){EnterToolsFile(playlist[index]);return;}if(IsDocument(playlist[index])){EnterDocuments(playlist[index]);return;}LeaveDocuments();HideHome();SetWindowChrome(true);if(shellBar!=null){shellBar.Active="media";KeepShell();}StopGif();audioMode=false;video.Audio=false;imageDecode=false;current=playlist[index];loaded=false;video.Loaded=false;video.Message="正在打开…";video.Invalidate();Text=Path.GetFileName(current)+" · 开物";
        if(IsImage(current)){ImageMode(true);engine.Command("stop");images.Clear();images.Visible=true;images.BringToFront();try{images.LoadFile(current);loaded=true;AdjustImageWindow();UpdateImageInfo();}catch{engine.Set("vid","auto");imageDecode=true;imageReady=false;imageDeadline=DateTime.Now.AddSeconds(8);engine.Set("pause","no");if(engine.Command("loadfile",current,"replace")<0)FailImage();}Touch();return;}
        ImageMode(false);engine.Set("vid",new[]{".mp3",".wav",".m4a",".flac",".ogg",".opus",".aac",".wma"}.Contains(Path.GetExtension(current).ToLowerInvariant())?"no":"auto");engine.Set("pause","no");int r=engine.Command("loadfile",current,"replace");if(r<0)Fail("无法打开这个文件");Touch();}
    void FailImage(){imageDecode=false;images.Visible=false;video.Visible=true;Fail("图片无法解码、已损坏或无法访问");}
    void Fail(string message){loaded=false;video.Loaded=false;video.Message=message;video.Invalidate();play.Text="▶";play.Invalidate();footer.Visible=true;Text="开物 · 打开失败";}
    void TickPlayer() {
        if(engine==null||closing)return;
        if(documentMode){TickCapture();footer.Visible=false;return;}
        if(homeMode){TickCapture();footer.Visible=false;return;}
        documentButton.Visible=toolsButton.Visible=!loaded&&!imageMode;documentButton.Location=new Point(124,30);time.Visible=(!imageMode||footer.Width>=530)&&!documentButton.Visible;
        for(int i=0;i<50;i++) {
            IntPtr p=Native.mpv_wait_event(engine.Handle,0);int id=Marshal.ReadInt32(p);if(id==0)break;
            if(id==8&&!imageMode){loaded=true;audioMode=new[]{".mp3",".wav",".m4a",".flac",".ogg",".opus",".aac",".wma"}.Contains(Path.GetExtension(current).ToLowerInvariant())||engine.Number("width")==0;video.Loaded=!audioMode;video.Audio=audioMode;video.AudioTitle=Path.GetFileNameWithoutExtension(current);video.Invalidate();video.Message="";Text=Path.GetFileName(engine.Get("path"))+" · 开物";}
            if(id==8&&imageDecode&&string.Equals(engine.Get("path"),current,StringComparison.OrdinalIgnoreCase))imageReady=true;
            if(id==16){IntPtr data=Marshal.ReadIntPtr(p,16);if(data!=IntPtr.Zero&&Marshal.ReadInt32(data)>=2){IntPtr a=Marshal.ReadIntPtr(data,8);if(Native.Read(Marshal.ReadIntPtr(a))=="glint")InputAction(Native.Read(Marshal.ReadIntPtr(a,IntPtr.Size)));}}
            if(id==7){IntPtr data=Marshal.ReadIntPtr(p,16);if(data!=IntPtr.Zero && Marshal.ReadInt32(data)==4){if(imageDecode)FailImage();else if(!imageMode)Fail("文件无法解码、已损坏或无法访问");}}
        }
        string dropTime=engine.Get("dropped-files/time");
        if(dropTime.Length>0&&dropTime!=lastDrop){lastDrop=dropTime;try{var files=new JavaScriptSerializer().Deserialize<string[]>(engine.Get("dropped-files/files"));if(files!=null)LoadFiles(files);}catch{}}
        if(imageDecode){if(DateTime.Now>imageDeadline)FailImage();else if(imageReady&&engine.Number("width")>0)try{images.SetBitmap(CaptureFrame.Read(engine,0));imageDecode=false;loaded=true;engine.Command("stop");AdjustImageWindow();UpdateImageInfo();}catch{}}
        if(loaded&&!imageMode){double pos=engine.Number("time-pos"),duration=engine.Number("duration");time.Text=Clock(pos)+" / "+Clock(duration);if(!seek.Dragging){seek.Fraction=duration>0?Math.Min(1,pos/duration):0;seek.Invalidate();}
            string state=engine.Get("pause")=="yes"?"▶":"Ⅱ";if(play.Text!=state){play.Text=state;play.Invalidate();}
            string rate=engine.Number("speed").ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"×";if(speed.Text!=rate){speed.Text=rate;speed.Invalidate();}
            string vol=engine.Get("mute")=="yes"?"静音":Math.Round(engine.Number("volume"))+"%";if(sound.Text!=vol){sound.Text=vol;sound.Invalidate();}
        }
        if(Cursor.Position!=lastCursor){lastCursor=Cursor.Position;if(ClientRectangle.Contains(PointToClient(lastCursor)))Touch();}
        TickCapture();
        if(recording!=null)time.Text="● GIF "+Clock(recordSeconds)+" · G 停止";
        else if(DateTime.Now<noticeUntil)time.Text=notice;
        bool show=audioMode||(imageMode&&!fullscreen)||recording!=null||DateTime.Now<noticeUntil||!loaded||(!imageMode&&engine.Get("pause")=="yes")||(DateTime.Now-activity).TotalSeconds<2.6||menu.Visible||seek.Dragging;
        if(footer.Visible!=show)footer.Visible=show;
    }
    string lastDrop="";
    void BindInput(){foreach(string entry in new[]{"s|screenshot","g|gif","SPACE|pause","MBTN_LEFT|pause","MBTN_LEFT_DBL|full","MBTN_RIGHT|menu","f|full","F11|full","ESC|escape","LEFT|back","RIGHT|forward","UP|up","DOWN|down","WHEEL_UP|up","WHEEL_DOWN|down","m|mute","Ctrl+o|open","Ctrl+RIGHT|next","Ctrl+LEFT|previous"}){var parts=entry.Split('|');engine.Command("keybind",parts[0],"script-message glint "+parts[1]);}}
    void InputAction(string action){Touch();switch(action){case "screenshot":TakeScreenshot();break;case "gif":ToggleGif();break;case "pause":Toggle();break;case "full":ToggleFullscreen();break;case "menu":ShowMenu(video,video.PointToClient(Cursor.Position));break;case "escape":if(fullscreen)ToggleFullscreen();break;case "back":engine.Command("seek","-5","relative");break;case "forward":engine.Command("seek","5","relative");break;case "up":Volume(5);break;case "down":Volume(-5);break;case "mute":engine.Command("cycle","mute");break;case "open":Open();break;case "next":Next(1);break;case "previous":Next(-1);break;}}
    string CapturePath(string extension){Directory.CreateDirectory(CaptureFolder);string name=Path.GetFileNameWithoutExtension(current);foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');if(name.Length>60)name=name.Substring(0,60);return Path.Combine(CaptureFolder,name+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,4)+extension);}
    void Notify(string text){notice=text;noticeUntil=DateTime.Now.AddSeconds(5);Touch();}
    bool HasVideo(){return !imageMode&&loaded&&engine.Number("width")>0;}
    void CaptureError(Exception ex){Notify("保存失败");MessageBox.Show(this,"保存失败："+ex.Message,"开物");}
    void TakeScreenshot(){if(!HasVideo()){Notify("请先打开视频");return;}try{string path=CapturePath(".png");using(var frame=CaptureFrame.Read(engine,0))frame.Save(path,System.Drawing.Imaging.ImageFormat.Png);Notify("截图已保存 · 右键打开目录");}catch(Exception ex){CaptureError(ex);}}
    void ToggleGif(){if(recording!=null){StopGif();return;}if(exporting!=null){Notify("GIF 正在保存，请稍候");return;}if(!HasVideo()){Notify("请先打开视频");return;}
        try{if(engine.Get("eof-reached")=="yes"){engine.Command("seek","0","absolute+exact");}engine.Set("pause","no");
            var frame=CaptureFrame.Read(engine,640);try{recording=new GifRecorder(CapturePath(".gif"));}catch{frame.Dispose();throw;}
            recording.Add(frame,0);recordSeconds=0;lastFrameTime=0;lastRecordTick=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;Notify("GIF 录制中 · G 停止");
        }catch(Exception ex){CaptureError(ex);}}
    void StopGif(){if(recording==null)return;exporting=recording;recording=null;exporting.Stop();Notify("正在保存 GIF…");}
    void TickCapture(){
        if(exporting!=null&&exporting.Work.IsCompleted){var finished=exporting;exporting=null;if(finished.Error!=null)CaptureError(finished.Error);else Notify("GIF 已保存 · 右键打开目录");}
        if(recording==null)return;
        double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency,delta=now-lastRecordTick;lastRecordTick=now;
        if(recording.Work.IsCompleted||!HasVideo()||engine.Get("eof-reached")=="yes"){StopGif();return;}
        if(engine.Get("pause")=="yes")return;
        recordSeconds+=delta;if(recordSeconds>=30){StopGif();return;}
        if(recordSeconds-lastFrameTime<0.095)return;
        try{var frame=CaptureFrame.Read(engine,640);if(recording.Add(frame,recordSeconds))lastFrameTime=recordSeconds;}catch(Exception ex){StopGif();CaptureError(ex);}
    }
    void OpenCaptureFolder(){try{Directory.CreateDirectory(CaptureFolder);Process.Start(new ProcessStartInfo(CaptureFolder){UseShellExecute=true});}catch(Exception ex){CaptureError(ex);}}
    static string Clock(double seconds) {var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return t.TotalHours>=1?((int)t.TotalHours)+t.ToString(@"\:mm\:ss"):t.ToString(@"mm\:ss");}
    void Touch(){activity=DateTime.Now;if(fullscreen)footer.BringToFront();if(!homeMode&&!documentMode&&!footer.Visible)footer.Visible=true;}
    void Toggle(){if(engine.Get("eof-reached")=="yes"){engine.Command("seek","0","absolute");engine.Set("pause","no");}else engine.Command("cycle","pause");Touch();}
    void Volume(int delta){if(imageMode)return;engine.Set("volume",Math.Max(0,Math.Min(100,engine.Number("volume")+delta)).ToString(System.Globalization.CultureInfo.InvariantCulture));Touch();}
    void ToggleFullscreen(){if(!fullscreen&&(!loaded||homeMode||documentMode))return;Touch();if(!fullscreen){savedBounds=Bounds;savedState=WindowState;fullscreenFrame=SizeFromClientSize(Size.Empty);WindowState=FormWindowState.Normal;if(shellBar!=null)shellBar.Visible=false;Bounds=Screen.FromControl(this).Bounds;fullscreen=true;footer.Dock=DockStyle.None;footer.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;footer.SetBounds(0,ClientSize.Height-footer.Height,ClientSize.Width,footer.Height);footer.BringToFront();}else{fullscreen=false;footer.Anchor=AnchorStyles.Left|AnchorStyles.Top;footer.Dock=DockStyle.Bottom;if(shellBar!=null)shellBar.Visible=true;Bounds=savedBounds;WindowState=savedState;KeepShell();}}
    protected override bool ProcessCmdKey(ref Message msg,Keys key) {
        if(engine==null)return base.ProcessCmdKey(ref msg,key);
        if(key==(Keys.Control|Keys.H)){ShowHome();return true;}
        if(key==(Keys.Control|Keys.Shift|Keys.E)){ConvertCurrent();return true;}
        if(key==(Keys.Control|Keys.D)){EnterDocuments(null);return true;}
        if(key==(Keys.Control|Keys.T)){EnterTools();return true;}
        if(homeMode){if(key==(Keys.Control|Keys.O)){Open();return true;}return base.ProcessCmdKey(ref msg,key);}
        if(documentMode){if(key==(Keys.Control|Keys.O)){Open();return true;}return base.ProcessCmdKey(ref msg,key);}
        if(imageMode){switch(key){case Keys.Left:Next(-1);return true;case Keys.Right:Next(1);return true;case Keys.D0:case Keys.NumPad0:images.Fit();return true;case Keys.D1:case Keys.NumPad1:images.ActualSize();return true;case Keys.R:RotateImage();return true;case Keys.Up:images.ZoomAt(1.2,new Point(images.Width/2,images.Height/2));return true;case Keys.Down:images.ZoomAt(1/1.2,new Point(images.Width/2,images.Height/2));return true;case Keys.Space:return true;case Keys.S:case Keys.G:return true;}}
        if(key==Keys.S){TakeScreenshot();return true;}if(key==Keys.G){ToggleGif();return true;}
        switch(key){case Keys.Space:if(loaded)Toggle();return true;case Keys.Left:engine.Command("seek","-5","relative");Touch();return true;case Keys.Right:engine.Command("seek","5","relative");Touch();return true;case Keys.Up:Volume(5);return true;case Keys.Down:Volume(-5);return true;case Keys.F:case Keys.F11:ToggleFullscreen();return true;case Keys.Escape:if(fullscreen)ToggleFullscreen();return true;case Keys.Control|Keys.O:Open();return true;case Keys.M:engine.Command("cycle","mute");Touch();return true;case Keys.Control|Keys.Right:Next(1);return true;case Keys.Control|Keys.Left:Next(-1);return true;}
        return base.ProcessCmdKey(ref msg,key);
    }
    void Next(int step){int n=index+step;if(n>=0&&n<playlist.Count){index=n;LoadCurrent();}}
    ToolStripMenuItem Item(string text,Action action){var i=new ToolStripMenuItem(text);i.Click+=(s,e)=>{action();Touch();};return i;}
    ContextMenuStrip NewMenu(){return new ContextMenuStrip{BackColor=Color.FromArgb(31,35,42),ForeColor=Color.FromArgb(226,230,235),ShowImageMargin=false,Font=Font};}
    void ShowSpeed(Control anchor){var m=NewMenu();foreach(string v in new[]{"0.5","0.75","1","1.25","1.5","2","3"}){string n=v;m.Items.Add(Item(v+"×",()=>engine.Set("speed",n)));}m.Closed+=(s,e)=>m.Dispose();m.Show(anchor,new Point(0,-m.PreferredSize.Height));Touch();}
    void ShowMenu(Control anchor,Point point) {
        menu.Dispose();menu=NewMenu();menu.Items.Add(Item("返回首页    Ctrl+H",ShowHome));menu.Items.Add(Item("打开文件…    Ctrl+O",Open));
        menu.Items.Add(Item("视频下载（粘贴链接）",()=>OpenTool("download",null,false)));
        menu.Items.Add(Item("文件工具（PDF / 图片 / OCR / ZIP / 字幕）    Ctrl+T",EnterTools));
        menu.Items.Add(Item("文档（新建 / 继续编辑）    Ctrl+D",()=>EnterDocuments(null)));
        if(loaded&&imageMode){menu.Items.Add(new ToolStripSeparator());menu.Items.Add(Item("上一张    ←",()=>Next(-1)));menu.Items.Add(Item("下一张    →",()=>Next(1)));menu.Items.Add(Item("适应窗口    0",images.Fit));menu.Items.Add(Item("实际大小    1",images.ActualSize));menu.Items.Add(Item("向右旋转    R",RotateImage));}
        if(loaded&&!imageMode){menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("提取视频文案…",TranscribeCurrent));menu.Items.Add(Item("保存当前画面（PNG）    S",TakeScreenshot));menu.Items.Add(Item(recording!=null?"停止录制 GIF    G":"开始录制 GIF    G",ToggleGif));
            var rates=new ToolStripMenuItem("播放速度");foreach(string v in new[]{"0.5","0.75","1","1.25","1.5","2","3"}){string n=v;var item=Item(v+"×",()=>engine.Set("speed",n));item.Checked=Math.Abs(engine.Number("speed")-double.Parse(v,System.Globalization.CultureInfo.InvariantCulture))<0.01;rates.DropDownItems.Add(item);}menu.Items.Add(rates);
            AddTracks("音轨","audio","aid");AddTracks("字幕","sub","sid");menu.Items.Add(Item("加载字幕…",()=>{using(var d=new OpenFileDialog()){d.Filter="字幕|*.srt;*.ass;*.ssa;*.vtt;*.sub|所有文件|*.*";if(d.ShowDialog(this)==DialogResult.OK)engine.Command("sub-add",d.FileName,"select");}}));
            var loop=Item("循环播放",()=>engine.Set("loop-file",engine.Get("loop-file")=="inf"?"no":"inf"));loop.Checked=engine.Get("loop-file")=="inf";menu.Items.Add(loop);
            if(playlist.Count>1){var list=new ToolStripMenuItem("播放列表（"+playlist.Count+"）");for(int j=0;j<playlist.Count;j++){int n=j;var it=Item(Path.GetFileName(playlist[j]),()=>{index=n;LoadCurrent();});it.Checked=j==index;list.DropDownItems.Add(it);}menu.Items.Add(list);}
        }
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add(Item("打开截图与 GIF 文件夹",OpenCaptureFolder));var pin=Item("窗口置顶",()=>TopMost=!TopMost);pin.Checked=TopMost;menu.Items.Add(pin);
        menu.Items.Add(Item(fullscreen?"退出全屏    F":"全屏    F",ToggleFullscreen));menu.Items.Add(Item("设置默认打开方式…",RegisterDefault));menu.Items.Add(Item("快捷键与关于",()=>MessageBox.Show(this,"开物 0.4.4 · 视频、图片与文档\n\n空格  播放 / 暂停\n← →  后退 / 前进 5 秒\n↑ ↓ / 滚轮  调整音量\nF / F11 / 双击  全屏\nEsc  退出全屏\nM  静音\nS  保存当前画面（PNG）\nG  开始 / 停止录制 GIF\nCtrl+O  打开文件\nCtrl+D  文档\nCtrl+Shift+E  文档转换格式\nCtrl+← →  切换列表文件\n\n看图：← → 上下张，滚轮缩放，按住拖动\n0 适应窗口，1 实际大小，R 旋转\n\nGIF 最长 30 秒、宽度不超过 640 像素、约 10 帧/秒。\n截图和 GIF 默认保存到「图片\\开物」。\n支持拖入视频；播放中拖入字幕即可加载。\n\n解码内核：mpv / FFmpeg\n开源许可与来源见随附说明。","开物")));
        if(anchor==more)point=new Point(anchor.Width-menu.PreferredSize.Width,-menu.PreferredSize.Height);menu.Show(anchor,point);Touch();
    }
    public static readonly string[] Extensions=".mp4 .mkv .mov .avi .webm .flv .ts .m2ts .mts .m4v .wmv .mpg .mpeg .vob .3gp .ogv .rm .rmvb .asf .mxf".Split(' ');
    public static void RegisterApplication(){
        string exe=Application.ExecutablePath,command="\""+exe+"\" \"%1\"";
        using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\GlintPlayer.Video")){k.SetValue("","开物视频");using(var icon=k.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+exe+"\",0");using(var c=k.CreateSubKey(@"shell\open\command"))c.SetValue("",command);}
        using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\GlintPlayer.Image")){k.SetValue("","开物图片");using(var icon=k.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+exe+"\",0");using(var c=k.CreateSubKey(@"shell\open\command"))c.SetValue("",command);}
        using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\GlintPlayer.Document")){k.SetValue("","开物文档");using(var icon=k.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+exe+"\",0");using(var c=k.CreateSubKey(@"shell\open\command"))c.SetValue("",command);}
        using(var k=Registry.CurrentUser.CreateSubKey(@"Software\GlintPlayer\Capabilities")){k.SetValue("ApplicationName","开物");k.SetValue("ApplicationDescription","视频、图片与轻文档");k.SetValue("ApplicationIcon","\""+exe+"\",0");using(var f=k.CreateSubKey("FileAssociations")){foreach(string ext in Extensions)f.SetValue(ext,"GlintPlayer.Video");foreach(string ext in ImageExtensions)f.SetValue(ext,"GlintPlayer.Image");foreach(string ext in DocumentExtensions)f.SetValue(ext,"GlintPlayer.Document");}}
        using(var k=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))k.SetValue("开物",@"Software\GlintPlayer\Capabilities");
        using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\开物.exe")){k.SetValue("FriendlyAppName","开物");using(var c=k.CreateSubKey(@"shell\open\command"))c.SetValue("",command);using(var types=k.CreateSubKey("SupportedTypes"))foreach(string ext in Extensions.Concat(ImageExtensions).Concat(DocumentExtensions))types.SetValue(ext,"");}
        foreach(string ext in Extensions)using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids"))k.SetValue("GlintPlayer.Video",new byte[0],RegistryValueKind.None);
        foreach(string ext in ImageExtensions)using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids"))k.SetValue("GlintPlayer.Image",new byte[0],RegistryValueKind.None);
        foreach(string ext in DocumentExtensions)using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids"))k.SetValue("GlintPlayer.Document",new byte[0],RegistryValueKind.None);
        RegisterExtraTypes(exe);
    }
    internal static bool DefaultRegistrationReady(string exe){
        string command="\""+exe+"\" \"%1\"";
        foreach(string id in new[]{"GlintPlayer.Video","GlintPlayer.Image","GlintPlayer.Document","GlintPlayer.File","Applications\\开物.exe"})
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+id+@"\shell\open\command"))
                if(key==null||!string.Equals(key.GetValue("") as string,command,StringComparison.OrdinalIgnoreCase))return false;
        using(var registered=Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"))
            if(registered==null||!string.Equals(registered.GetValue("开物") as string,@"Software\GlintPlayer\Capabilities",StringComparison.OrdinalIgnoreCase))return false;
        using(var associations=Registry.CurrentUser.OpenSubKey(@"Software\GlintPlayer\Capabilities\FileAssociations"))
        using(var supported=Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\开物.exe\SupportedTypes")){
            if(associations==null||supported==null)return false;
            foreach(var group in new[]{new{Extensions=Extensions,Id="GlintPlayer.Video"},new{Extensions=ImageExtensions,Id="GlintPlayer.Image"},new{Extensions=DocumentExtensions,Id="GlintPlayer.Document"},new{Extensions=ExtraExtensions,Id="GlintPlayer.File"}})
                foreach(string ext in group.Extensions){
                    if(!string.Equals(associations.GetValue(ext) as string,group.Id,StringComparison.OrdinalIgnoreCase)||supported.GetValue(ext)==null)return false;
                    using(var openWith=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids"))
                        if(openWith==null||!openWith.GetValueNames().Contains(group.Id,StringComparer.OrdinalIgnoreCase))return false;
                }
        }
        return true;
    }
    void RegisterDefault(){try{if(!DefaultRegistrationReady(Application.ExecutablePath))RegisterApplication();Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString("开物")){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"无法打开默认应用设置："+ex.Message,"开物");}}
    void AddTracks(string title,string type,string property){
        var parent=new ToolStripMenuItem(title);parent.DropDownItems.Add(Item("关闭",()=>engine.Set(property,"no")));
        try {var tracks=new JavaScriptSerializer().DeserializeObject(engine.Get("track-list")) as object[];if(tracks!=null)foreach(var raw in tracks){var t=raw as Dictionary<string,object>;if(t==null||!t.ContainsKey("type")||Convert.ToString(t["type"])!=type)continue;string id=Convert.ToString(t["id"]);string label="#"+id;foreach(string k in new[]{"lang","title","codec"})if(t.ContainsKey(k))label+=" · "+Convert.ToString(t[k]);var item=Item(label,()=>engine.Set(property,id));item.Checked=t.ContainsKey("selected")&&Convert.ToBoolean(t["selected"]);parent.DropDownItems.Add(item);}}catch{}
        menu.Items.Add(parent);
    }
    void SaveSettings(){if(engine==null)return;try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));File.WriteAllLines(settingsPath,new[]{engine.Get("volume"),engine.Get("mute")});}catch{}}
    void ReadSettings(){try{var lines=File.ReadAllLines(settingsPath);double v;if(lines.Length>0&&double.TryParse(lines[0],out v))engine.Set("volume",Math.Max(0,Math.Min(100,v)).ToString(System.Globalization.CultureInfo.InvariantCulture));if(lines.Length>1&&(lines[1]=="yes"||lines[1]=="no"))engine.Set("mute",lines[1]);}catch{}}
}
static class Program {
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [STAThread] static void Main(string[] args){try{if(!SetProcessDpiAwarenessContext(new IntPtr(-4)))SetProcessDPIAware();}catch{SetProcessDPIAware();}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);if(args.Length==1&&args[0]=="--register"){Player.RegisterApplication();return;}Application.Run(new Player(args));}
}
}



using System;using System.IO;using System.Drawing;using System.Linq;using System.Windows.Forms;
namespace Glint {
partial class Player {
 static readonly string[] ExtraExtensions=".mp3 .wav .m4a .aac .flac .ogg .opus .wma .zip .md .json .log .srt .vtt .tsv".Split(' ');
 static void RegisterExtraTypes(string exe){
  using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\GlintPlayer.File")){key.SetValue("","开物文件");using(var icon=key.CreateSubKey("DefaultIcon"))icon.SetValue("","\""+exe+"\",0");using(var command=key.CreateSubKey(@"shell\open\command"))command.SetValue("","\""+exe+"\" \"%1\"");}
  using(var cap=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\GlintPlayer\Capabilities\FileAssociations"))foreach(string ext in ExtraExtensions)cap.SetValue(ext,"GlintPlayer.File");
  using(var types=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\开物.exe\SupportedTypes"))foreach(string ext in ExtraExtensions)types.SetValue(ext,"");
  foreach(string ext in ExtraExtensions)using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids"))key.SetValue("GlintPlayer.File",new byte[0],Microsoft.Win32.RegistryValueKind.None);
 }
 DocumentWindow toolsWindow;FlatButton toolsButton=new FlatButton("工具");
 static bool ToolFile(string path){return new[]{".txt",".zip",".md",".json",".log",".srt",".vtt"}.Contains(Path.GetExtension(path).ToLowerInvariant());}
 void InitializeTools(){footer.Controls.Add(toolsButton);toolsButton.Size=new Size(64,34);toolsButton.Location=new Point(204,30);toolsButton.Click+=(s,e)=>EnterTools();}
 void EnterTools(){EnterToolsFile(null);}
 void EnterToolsFile(string path){
  PrepareWorkspace();if(document!=null&&!document.IsDisposed)document.Hide();
  if(toolsWindow==null||toolsWindow.IsDisposed){
   toolsWindow=new DocumentWindow("--tools"){ToolStartupFile=path,TopLevel=false,FormBorderStyle=FormBorderStyle.None,MinimumSize=Size.Empty,Dock=DockStyle.Fill};
   toolsWindow.OpenMedia=file=>{if(file==null)LeaveDocuments();else LoadFiles(new[]{file});};toolsWindow.ToolModeChanged=mode=>{if(shellBar!=null){shellBar.Active=mode=="download"?"download":"tools";KeepShell();}};toolsWindow.SwitchHome=ShowHome;toolsWindow.SwitchDocument=()=>EnterDocuments(null);toolsWindow.SwitchTools=EnterTools;
   documentHost.Controls.Add(toolsWindow);toolsWindow.FormClosed+=(s,e)=>{if(pendingShellClose&&!closing&&!IsDisposed)BeginInvoke((Action)(()=>{if(!IsDisposed)Close();}));};toolsWindow.Show();
  }else{toolsWindow.Show();if(path!=null){if(!toolsWindow.Ready)toolsWindow.ToolStartupFile=path;else toolsWindow.EvaluateAsync("acceptExternal("+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(toolsWindow.ToolsBackend.Packet(path))+")");}}
  toolsWindow.BringToFront();if(shellBar!=null){shellBar.Active="tools";KeepShell();}Text="开物 · 文件工具";
 }
 void TranscribeCurrent(){if(!loaded||imageMode)return;engine.Set("pause","yes");string source=current;if(fullscreen)ToggleFullscreen();EnterToolsFile(null);if(!toolsWindow.Ready){toolsWindow.ToolStartupMode="transcribe";toolsWindow.ToolStartupFile=source;}else toolsWindow.EvaluateAsync("openTranscription("+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{token=toolsWindow.ToolsBackend.Add(source),name=Path.GetFileName(source)})+")");}
 void OpenTool(string mode,string link,bool start){EnterToolsFile(null);if(!toolsWindow.Ready){toolsWindow.ToolStartupMode=mode;toolsWindow.ToolStartupLink=link;toolsWindow.ToolAutoDownload=start;}else toolsWindow.EvaluateAsync("openTool("+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{mode=mode,link=link,start=start})+")");}
}
}

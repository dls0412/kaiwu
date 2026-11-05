using System;using System.Drawing;using System.IO;using System.Reflection;using System.Threading;using System.Windows.Forms;
namespace Glint { static class BuildSmoke {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(Player p,string name){return typeof(Player).GetField(name,Flags).GetValue(p);}
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread]static int Main(){try{
  string folder=AppDomain.CurrentDomain.BaseDirectory;
  foreach(string path in new[]{"libmpv-2.dll","kaiwu.ico","ui/atlas.png","app/tools.html","app/engine/soffice.wasm.br","helpers/download/lux.exe","helpers/speech/whisper-cli.exe"})Check(File.Exists(Path.Combine(folder,path)),"dependency "+path);
  using(var icon=new Icon(Path.Combine(folder,"kaiwu.ico"),new Size(48,48)))Check(icon.Width==48&&icon.Height==48,"Windows icon contains a 48px frame");
  using(var player=new Player(new string[0]){Opacity=0,ShowInTaskbar=false}){player.Show();var deadline=DateTime.UtcNow.AddSeconds(2);while(DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(10);}Check(Field(player,"engine")!=null,"media engine initializes");Check(Field(player,"document")==null&&Field(player,"toolsWindow")==null,"home starts without document or tool WebViews");Check(((HomeView)Field(player,"home")).Visible,"home is visible");player.Close();}
  return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
}}

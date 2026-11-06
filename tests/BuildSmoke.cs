using System;using System.Drawing;using System.IO;using System.Reflection;using System.Threading;using System.Windows.Forms;
namespace Glint { static class BuildSmoke {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(Player p,string name){return typeof(Player).GetField(name,Flags).GetValue(p);}
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 [STAThread]static int Main(){try{
  string folder=AppDomain.CurrentDomain.BaseDirectory;
  foreach(string path in new[]{"libmpv-2.dll","kaiwu.ico","ui/atlas.png","ui/app-icon.png","app/tools.html","app/engine/soffice.wasm.br","helpers/download/lux.exe","helpers/speech/whisper-cli.exe"})Check(File.Exists(Path.Combine(folder,path)),"dependency "+path);
  foreach(int size in new[]{16,24,32,48,64,128,256})using(var icon=new Icon(Path.Combine(folder,"kaiwu.ico"),new Size(size,size)))using(var bitmap=new Bitmap(size,size)){using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.White);g.DrawIcon(icon,new Rectangle(0,0,size,size));}using(var expected=new Bitmap(size,size)){using(var g=Graphics.FromImage(expected)){g.Clear(Color.White);string exact=Path.Combine(folder,"ui","icon-sizes",size+".png");using(var source=Image.FromFile(File.Exists(exact)?exact:Path.Combine(folder,"ui","app-icon.png")))g.DrawImageUnscaled(source,0,0);}long difference=0;for(int y=0;y<size;y++)for(int x=0;x<size;x++){Color a=bitmap.GetPixel(x,y),b=expected.GetPixel(x,y);difference+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);}Check(difference/(double)(size*size*3)<8,"Windows native icon matches the artwork without noise at "+size+"px");}}
  using(var player=new Player(new string[0]){Opacity=0,ShowInTaskbar=false}){player.Show();var deadline=DateTime.UtcNow.AddSeconds(2);while(DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(10);}Check(Field(player,"engine")!=null,"media engine initializes");Check(Field(player,"document")==null&&Field(player,"toolsWindow")==null,"home starts without document or tool WebViews");Check(((HomeView)Field(player,"home")).Visible,"home is visible");player.Close();}
  return 0;
 }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
}}

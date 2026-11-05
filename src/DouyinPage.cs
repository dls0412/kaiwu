using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
namespace Glint {
// Visits the public page with a new anonymous profile; never imports browser cookies.
static class DouyinPage {
 internal static Dictionary<string,object> VideoInfo(Dictionary<string,object> item,string id){
  if(!item.ContainsKey("aweme_id")||Convert.ToString(item["aweme_id"])!=id)return null;
  object value;if(!item.TryGetValue("video",out value))return null;var video=value as Dictionary<string,object>;if(video==null)return null;
  var choices=new List<Dictionary<string,object>>();
  if(video.TryGetValue("bit_rate",out value)&&value is object[])foreach(var entry in (object[])value){var rate=entry as Dictionary<string,object>;if(rate==null||!rate.ContainsKey("format")||Convert.ToString(rate["format"])!="mp4"||!rate.ContainsKey("play_addr"))continue;if(rate.ContainsKey("is_h265")&&Convert.ToInt32(rate["is_h265"])!=0)continue;choices.Add((Dictionary<string,object>)rate["play_addr"]);}
  if(choices.Count==0){if(video.TryGetValue("play_addr_h264",out value)||video.TryGetValue("play_addr",out value)){var address=value as Dictionary<string,object>;if(address!=null)choices.Add(address);}}
  var streams=new Dictionary<string,object>();int n=0;
  foreach(var address in choices){
   if(!address.TryGetValue("url_list",out value)||!(value is object[]))continue;
   long size=address.ContainsKey("data_size")?Convert.ToInt64(address["data_size"]):0;
   foreach(var raw in (object[])value){
    Uri uri;if(!Uri.TryCreate(Convert.ToString(raw),UriKind.Absolute,out uri)||uri.Scheme!="https")continue;
    var part=new Dictionary<string,object>{{"url",uri.AbsoluteUri},{"ext","mp4"},{"size",size}};
    streams["mp4-"+(++n)]=new Dictionary<string,object>{{"size",size},{"ext","mp4"},{"parts",new object[]{part}}};
   }
  }
  if(streams.Count==0)return null;
  return new Dictionary<string,object>{{"url","https://www.douyin.com/video/"+id},{"title",item.ContainsKey("desc")?item["desc"]:"抖音视频"},{"type","video"},{"streams",streams},{"sourceId",id},{"parser","public anonymous WebView2"}};
 }
 internal static async Task<Dictionary<string,object>> ParseAsync(Form owner,string id){
  string profile=Path.Combine(Path.GetTempPath(),"glint-download-"+Guid.NewGuid().ToString("N"));
  var complete=new TaskCompletionSource<Dictionary<string,object>>();
  using(var hidden=new Form{Width=1024,Height=768,Opacity=0,ShowInTaskbar=false})using(var web=new WebView2{Dock=DockStyle.Fill}){
   hidden.Controls.Add(web);hidden.Show(owner);
   try{
    var environment=await CoreWebView2Environment.CreateAsync(null,profile);
    environment.BrowserProcessExited+=delegate{Task.Run(()=>{try{if(Directory.Exists(profile))Directory.Delete(profile,true);}catch{}});};
    await web.EnsureCoreWebView2Async(environment);
    var core=web.CoreWebView2;core.IsMuted=true;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.AreDefaultScriptDialogsEnabled=false;
    core.Settings.UserAgent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    core.NewWindowRequested+=(s,e)=>e.Handled=true;
    core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
    core.DownloadStarting+=(s,e)=>e.Cancel=true;
    core.NavigationStarting+=(s,e)=>{Uri target;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out target)||(target.Scheme!="https"&&target.Scheme!="http"))e.Cancel=true;};
    core.WebResourceResponseReceived+=async(s,e)=>{
     Uri uri;if(complete.Task.IsCompleted||!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)||uri.Host!="www.douyin.com"||!uri.AbsolutePath.EndsWith("/aweme/detail/"))return;
     try{using(var response=await e.Response.GetContentAsync())using(var reader=new StreamReader(response,System.Text.Encoding.UTF8)){var data=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(await reader.ReadToEndAsync()) as Dictionary<string,object>;if(data!=null&&data.ContainsKey("aweme_detail")){var item=data["aweme_detail"] as Dictionary<string,object>;var info=item==null?null:VideoInfo(item,id);if(info!=null)complete.TrySetResult(info);}}}catch{}
    };
    core.Navigate("https://www.douyin.com/video/"+id);
    if(await Task.WhenAny(complete.Task,Task.Delay(40000))!=complete.Task)throw new IOException("平台暂未提供可公开下载的视频，请稍后重试；当前无需登录的解析未成功");
    return await complete.Task;
   }finally{hidden.Close();}
  }
 }
}}

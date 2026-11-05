using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Glint {
sealed class ImageCanvas : Control {
    Image picture; MemoryStream source; bool animated,dragged; Point mouseStart; PointF panStart,pan;
    double zoom=1; bool fit=true;int turns;
    public Action Activity,Fullscreen,ViewChanged;
    public Action<string[]> DropFiles;
    public bool HasImage {get{return picture!=null;}}
    public Size ImageSize {get{return picture==null?Size.Empty:turns%2==0?picture.Size:new Size(picture.Height,picture.Width);}}
    public double Zoom {get{return fit?FitZoom():zoom;}}
    public bool IsFit {get{return fit;}}
    public bool IsAnimated {get{return animated;}}
    public ImageCanvas(){BackColor=Color.FromArgb(15,17,21);Dock=DockStyle.Fill;AllowDrop=true;TabStop=true;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        MouseDown+=(s,e)=>{Focus();if(e.Button==MouseButtons.Left&&picture!=null){mouseStart=e.Location;panStart=pan;dragged=false;Capture=true;Cursor=Cursors.SizeAll;}};
        MouseMove+=(s,e)=>{if(Activity!=null)Activity();if(Capture&&e.Button==MouseButtons.Left){if(Math.Abs(e.X-mouseStart.X)+Math.Abs(e.Y-mouseStart.Y)>3)dragged=true;pan=new PointF(panStart.X+e.X-mouseStart.X,panStart.Y+e.Y-mouseStart.Y);ClampPan();Invalidate();}};
        MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Left){Capture=false;Cursor=Cursors.Default;}};
        MouseDoubleClick+=(s,e)=>{if(!dragged&&e.Button==MouseButtons.Left&&Fullscreen!=null)Fullscreen();};
        MouseWheel+=(s,e)=>ZoomAt(Math.Pow(1.2,e.Delta/120.0),e.Location);
        DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
        DragDrop+=(s,e)=>{if(DropFiles!=null)DropFiles((string[])e.Data.GetData(DataFormats.FileDrop));};
        Resize+=(s,e)=>{ClampPan();if(ViewChanged!=null)ViewChanged();};
    }
    public void LoadFile(string path){var stream=new MemoryStream(File.ReadAllBytes(path));Image image=null;try{image=Image.FromStream(stream,true,true);ApplyOrientation(image);SetImage(image,stream);}catch{if(image!=null)image.Dispose();stream.Dispose();throw;}}
    public void SetBitmap(Bitmap image){SetImage(image,null);}
    void SetImage(Image image,MemoryStream stream){Clear();picture=image;source=stream;animated=ImageAnimator.CanAnimate(image);if(animated)ImageAnimator.Animate(image,Animate);Fit();}
    static void ApplyOrientation(Image image){try{if(Array.IndexOf(image.PropertyIdList,0x112)<0)return;int orientation=image.GetPropertyItem(0x112).Value[0];RotateFlipType[] turns={RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone};if(orientation>1&&orientation<turns.Length)image.RotateFlip(turns[orientation]);image.RemovePropertyItem(0x112);}catch{}}
    void Animate(object sender,EventArgs e){if(IsHandleCreated&&!IsDisposed)try{BeginInvoke((Action)(()=>{if(!IsDisposed)Invalidate();}));}catch(InvalidOperationException){}}
    public void Clear(){if(picture!=null){if(animated)ImageAnimator.StopAnimate(picture,Animate);picture.Dispose();picture=null;}if(source!=null){source.Dispose();source=null;}animated=false;turns=0;pan=PointF.Empty;Invalidate();}
    double FitZoom(){return picture==null?1:Math.Max(0.001,Math.Min(1,Math.Min((double)ClientSize.Width/ImageSize.Width,(double)ClientSize.Height/ImageSize.Height)));}
    void ClampPan(){if(picture==null)return;float x=(float)Math.Max(0,(ImageSize.Width*Zoom-Width)/2),y=(float)Math.Max(0,(ImageSize.Height*Zoom-Height)/2);pan.X=Math.Max(-x,Math.Min(x,pan.X));pan.Y=Math.Max(-y,Math.Min(y,pan.Y));}
    public void Fit(){fit=true;pan=PointF.Empty;Invalidate();if(ViewChanged!=null)ViewChanged();}
    public void ActualSize(){fit=false;zoom=1;pan=PointF.Empty;ClampPan();Invalidate();if(ViewChanged!=null)ViewChanged();}
    public void ZoomAt(double factor,Point point){if(picture==null)return;double old=Zoom;zoom=Math.Max(0.01,Math.Min(32,old*factor));fit=false;double ratio=zoom/old;pan=new PointF((float)(point.X-Width/2.0-(point.X-Width/2.0-pan.X)*ratio),(float)(point.Y-Height/2.0-(point.Y-Height/2.0-pan.Y)*ratio));ClampPan();Invalidate();if(ViewChanged!=null)ViewChanged();}
    public void Rotate(){if(picture==null)return;turns=(turns+1)%4;Fit();}
    public Bitmap Snapshot(){if(picture==null)throw new InvalidOperationException("没有图片");if(animated)ImageAnimator.UpdateFrames(picture);var bitmap=new Bitmap(picture);if(turns>0)bitmap.RotateFlip((RotateFlipType)turns);return bitmap;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(picture==null)return;if(animated)ImageAnimator.UpdateFrames(picture);double z=Zoom;float w=(float)(picture.Width*z),h=(float)(picture.Height*z);e.Graphics.InterpolationMode=z>=4?InterpolationMode.NearestNeighbor:InterpolationMode.HighQualityBicubic;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;e.Graphics.TranslateTransform(Width/2f+pan.X,Height/2f+pan.Y);e.Graphics.RotateTransform(turns*90);e.Graphics.DrawImage(picture,new RectangleF(-w/2,-h/2,w,h));}
    protected override void Dispose(bool disposing){if(disposing)Clear();base.Dispose(disposing);}
}
}

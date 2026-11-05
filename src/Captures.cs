using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Glint {
// Frames come from the decoder, never from desktop/window screenshots.
static class CaptureFrame {
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern int mpv_command_ret(IntPtr h,IntPtr[] args,IntPtr result);
    [DllImport("libmpv-2.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern void mpv_free_node_contents(IntPtr node);
    public static Bitmap Read(Engine engine,int maxWidth) {
        IntPtr node=Marshal.AllocHGlobal(16);IntPtr[] args=new IntPtr[4];
        for(int i=0;i<16;i++)Marshal.WriteByte(node,i,0);
        try {
            string[] command={"screenshot-raw","subtitles","bgr0"};
            for(int i=0;i<3;i++){byte[] b=Native.U(command[i]);args[i]=Marshal.AllocHGlobal(b.Length);Marshal.Copy(b,0,args[i],b.Length);}
            if(mpv_command_ret(engine.Handle,args,node)<0)throw new InvalidOperationException("当前没有可截取的视频画面");
            if(Marshal.ReadInt32(node,8)!=8)throw new InvalidOperationException("解码器返回的画面格式不正确");
            IntPtr list=Marshal.ReadIntPtr(node),values=Marshal.ReadIntPtr(list,8),keys=Marshal.ReadIntPtr(list,16);
            int width=0,height=0,stride=0;IntPtr data=IntPtr.Zero;long length=0;
            for(int i=0;i<Marshal.ReadInt32(list);i++){
                string key=Native.Read(Marshal.ReadIntPtr(keys,i*8));IntPtr value=IntPtr.Add(values,i*16);
                if(key=="w")width=checked((int)Marshal.ReadInt64(value));
                if(key=="h")height=checked((int)Marshal.ReadInt64(value));
                if(key=="stride")stride=checked((int)Marshal.ReadInt64(value));
                if(key=="data"){IntPtr array=Marshal.ReadIntPtr(value);data=Marshal.ReadIntPtr(array);length=Marshal.ReadInt64(array,8);}
            }
            if(width<=0||height<=0||data==IntPtr.Zero||Math.Abs((long)stride)<width*4L||length<Math.Abs((long)stride)*height)throw new InvalidOperationException("视频帧数据不完整");
            using(var original=new Bitmap(width,height,PixelFormat.Format32bppRgb)) {
                var bits=original.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
                try{byte[] row=new byte[width*4];for(int y=0;y<height;y++){Marshal.Copy(IntPtr.Add(data,y*stride),row,0,row.Length);Marshal.Copy(row,0,IntPtr.Add(bits.Scan0,y*bits.Stride),row.Length);}}finally{original.UnlockBits(bits);}
                if(maxWidth<=0||width<=maxWidth)return new Bitmap(original);
                int targetHeight=Math.Max(1,(int)Math.Round(height*(double)maxWidth/width));
                var resized=new Bitmap(maxWidth,targetHeight,PixelFormat.Format32bppRgb);
                using(var g=Graphics.FromImage(resized)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(original,0,0,resized.Width,resized.Height);}return resized;
            }
        } finally {mpv_free_node_contents(node);Marshal.FreeHGlobal(node);foreach(var p in args)if(p!=IntPtr.Zero)Marshal.FreeHGlobal(p);}
    }
}

// Bounded queue + streaming writer: memory does not grow with clip length.
sealed class GifRecorder {
    sealed class Frame : IDisposable {public Bitmap Image;public double Time;public void Dispose(){Image.Dispose();}}
    readonly BlockingCollection<Frame> frames=new BlockingCollection<Frame>(2);
    public readonly string Path;
    public readonly Task Work;
    public Exception Error;
    public int FrameCount;
    public GifRecorder(string path){Path=path;Work=Task.Run((Action)Write);}
    public bool Add(Bitmap image,double time){var frame=new Frame{Image=image,Time=time};if(frames.IsAddingCompleted||!frames.TryAdd(frame)){frame.Dispose();return false;}return true;}
    public void Stop(){frames.CompleteAdding();}
    void Write(){string partial=Path+".partial";Frame previous=null;try{
        using(var stream=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new AnimatedGif(stream)){
            foreach(Frame frame in frames.GetConsumingEnumerable()){
                if(previous!=null){try{writer.Add(previous.Image,Math.Max(2,(int)Math.Round((frame.Time-previous.Time)*100)));FrameCount++;}finally{previous.Dispose();previous=null;}}
                previous=frame;
            }
            if(previous!=null){writer.Add(previous.Image,10);FrameCount++;previous.Dispose();previous=null;}
        }
        if(FrameCount==0)throw new InvalidOperationException("还没有录到视频画面");File.Move(partial,Path);
    }catch(Exception ex){Error=ex;}finally{if(previous!=null)previous.Dispose();Frame queued;while(frames.TryTake(out queued))queued.Dispose();try{if(File.Exists(partial))File.Delete(partial);}catch(Exception ex){if(Error==null)Error=ex;}}}
}

// GIF89a, per-frame median-cut palettes and 12-bit LZW. No external encoder.
sealed class AnimatedGif : IDisposable {
    readonly BinaryWriter writer;bool started;int width,height;
    public AnimatedGif(Stream stream){writer=new BinaryWriter(stream,Encoding.ASCII,true);}
    sealed class Box {public List<int> Colors;public int Axis,Range,Count;public Box(List<int> colors,int[] counts){Colors=colors;int[] min={31,31,31},max={0,0,0};foreach(int c in colors){Count+=counts[c];for(int a=0;a<3;a++){int v=Channel(c,a);min[a]=Math.Min(min[a],v);max[a]=Math.Max(max[a],v);}}for(int a=0;a<3;a++)if(max[a]-min[a]>Range){Range=max[a]-min[a];Axis=a;}}}
    static int Channel(int color,int axis){return (color>>(10-axis*5))&31;}
    static byte[] Quantize(Bitmap image,out byte[] palette){
        int w=image.Width,h=image.Height;byte[] pixels=new byte[w*h*4];var bits=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
        try{for(int y=0;y<h;y++)Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),pixels,y*w*4,w*4);}finally{image.UnlockBits(bits);}
        int[] counts=new int[32768];long[] reds=new long[32768],greens=new long[32768],blues=new long[32768];
        for(int p=0;p<pixels.Length;p+=4){int b=pixels[p],g=pixels[p+1],r=pixels[p+2],key=((r>>3)<<10)|((g>>3)<<5)|(b>>3);counts[key]++;reds[key]+=r;greens[key]+=g;blues[key]+=b;}
        var colors=new List<int>();for(int c=0;c<32768;c++)if(counts[c]>0)colors.Add(c);
        var boxes=new List<Box>{new Box(colors,counts)};
        while(boxes.Count<256){Box split=null;long score=-1;foreach(var box in boxes){long s=(long)box.Range*box.Count;if(box.Colors.Count>1&&s>score){score=s;split=box;}}if(split==null)break;
            int axis=split.Axis;split.Colors.Sort((a,b)=>Channel(a,axis).CompareTo(Channel(b,axis)));int total=0,at=0;while(at<split.Colors.Count-1){total+=counts[split.Colors[at++]];if(total>=split.Count/2)break;}at=Math.Max(1,at);
            boxes.Remove(split);boxes.Add(new Box(split.Colors.GetRange(0,at),counts));boxes.Add(new Box(split.Colors.GetRange(at,split.Colors.Count-at),counts));
        }
        palette=new byte[768];int[] map=new int[32768];
        for(int i=0;i<boxes.Count;i++){long r=0,g=0,b=0;foreach(int c in boxes[i].Colors){r+=reds[c];g+=greens[c];b+=blues[c];map[c]=i;}palette[i*3]=(byte)(r/boxes[i].Count);palette[i*3+1]=(byte)(g/boxes[i].Count);palette[i*3+2]=(byte)(b/boxes[i].Count);}
        byte[] indexed=new byte[w*h];for(int i=0,p=0;i<indexed.Length;i++,p+=4)indexed[i]=(byte)map[((pixels[p+2]>>3)<<10)|((pixels[p+1]>>3)<<5)|(pixels[p]>>3)];return indexed;
    }
    public void Add(Bitmap image,int delay){
        if(!started){width=image.Width;height=image.Height;writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)width);writer.Write((ushort)height);writer.Write(new byte[]{0x70,0,0});writer.Write(new byte[]{0x21,0xff,11});writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));writer.Write(new byte[]{3,1,0,0,0});started=true;}
        Bitmap scaled=null;try{if(image.Width!=width||image.Height!=height){scaled=new Bitmap(width,height,PixelFormat.Format32bppRgb);using(var g=Graphics.FromImage(scaled))g.DrawImage(image,0,0,width,height);image=scaled;}
            byte[] palette;byte[] indexed=Quantize(image,out palette);
            writer.Write(new byte[]{0x21,0xf9,4,4});writer.Write((ushort)Math.Min(65535,delay));writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((byte)0x2c);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write((ushort)width);writer.Write((ushort)height);writer.Write((byte)0x87);writer.Write(palette);writer.Write((byte)8);
            byte[] data=Lzw(indexed);for(int p=0;p<data.Length;p+=255){int n=Math.Min(255,data.Length-p);writer.Write((byte)n);writer.Write(data,p,n);}writer.Write((byte)0);
        }finally{if(scaled!=null)scaled.Dispose();}
    }
    static byte[] Lzw(byte[] data){
        var output=new List<byte>();var dictionary=new Dictionary<int,int>(4096);int size=9,next=258,buffer=0,count=0;
        Action<int> emit=code=>{buffer|=code<<count;count+=size;while(count>=8){output.Add((byte)buffer);buffer>>=8;count-=8;}};
        emit(256);int prefix=data[0];
        for(int i=1;i<data.Length;i++){int value=data[i],key=(prefix<<8)|value,found;if(dictionary.TryGetValue(key,out found)){prefix=found;continue;}
            emit(prefix);if(next<4096){dictionary[key]=next++;if(next>(1<<size)&&size<12)size++;}else{emit(256);dictionary.Clear();size=9;next=258;}prefix=value;
        }
        emit(prefix);if(next==(1<<size)&&size<12)size++;emit(257);if(count>0)output.Add((byte)buffer);return output.ToArray();
    }
    public void Dispose(){if(started)writer.Write((byte)0x3b);writer.Dispose();}
}
}

using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.Collections.Generic;
// Preserve RGBA PNG frames directly; Icon.FromHandle().Save() can discard alpha.
static class PackageIcon {
 static void Main(string[] args){
  if(args.Length<2)throw new ArgumentException("Usage: PackageIcon atlas.png output.ico [preview.png]");
  int[] sizes={16,24,32,48,64,128,256};var frames=new List<byte[]>();
  using(var atlas=Image.FromFile(args[0]))foreach(int size in sizes)using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb)){
   using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(atlas,new RectangleF(-size*.1015625f,-size*.1015625f,size*1.203125f,size*1.203125f),new RectangleF(atlas.Width/3f,0,atlas.Width/3f,atlas.Height/3f),GraphicsUnit.Pixel);}
   using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);frames.Add(stream.ToArray());}
   if(size==256&&args.Length>2)bitmap.Save(args[2],ImageFormat.Png);
  }
  using(var writer=new BinaryWriter(File.Create(args[1]))){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}foreach(var frame in frames)writer.Write(frame);}
 }
}

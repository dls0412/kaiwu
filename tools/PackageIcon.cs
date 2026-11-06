using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.Collections.Generic;
// Use 32-bit Windows DIB + AND masks for GDI, WinForms and Explorer compatibility.
static class PackageIcon {
 static byte[] Dib(Bitmap image){int size=image.Width,maskStride=((size+31)/32)*4;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(40);writer.Write(size);writer.Write(size*2);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(0);writer.Write(size*size*4+maskStride*size);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){Color c=image.GetPixel(x,y);writer.Write(c.B);writer.Write(c.G);writer.Write(c.R);writer.Write(c.A);}for(int y=size-1;y>=0;y--){byte[] row=new byte[maskStride];for(int x=0;x<size;x++)if(image.GetPixel(x,y).A==0)row[x/8]|=(byte)(128>>(x%8));writer.Write(row);}return stream.ToArray();}}
 static void Main(string[] args){
  if(args.Length<2)throw new ArgumentException("Usage: PackageIcon app-icon.png output.ico [preview.png]");
  int[] sizes={16,24,32,48,64,128,256};var frames=new List<byte[]>();
  using(var atlas=Image.FromFile(args[0]))foreach(int size in sizes)using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb)){
   using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;string exact=Path.Combine(Path.GetDirectoryName(args[0]),"icon-sizes",size+".png");if(File.Exists(exact)){using(var frame=Image.FromFile(exact))g.DrawImageUnscaled(frame,0,0);}else g.DrawImage(atlas,new RectangleF(0,0,size,size),new RectangleF(0,0,atlas.Width,atlas.Height),GraphicsUnit.Pixel);}
   frames.Add(Dib(bitmap));
   if(size==256&&args.Length>2)bitmap.Save(args[2],ImageFormat.Png);
  }
  using(var writer=new BinaryWriter(File.Create(args[1]))){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}foreach(var frame in frames)writer.Write(frame);}
 }
}

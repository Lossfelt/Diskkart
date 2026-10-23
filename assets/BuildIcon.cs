using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class BuildIcon
{
    static GraphicsPath Rounded(float x, float y, float w, float h, float radius)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90); path.AddArc(x, y + h - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    static byte[] Render(int size)
    {
        using (var bitmap = new Bitmap(size, size))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size / 64f, size / 64f);
            using (var shape = Rounded(3, 3, 58, 58, 14)) using (var fill = new SolidBrush(Color.FromArgb(27, 53, 75))) g.FillPath(fill, shape);
            using (var pen = new Pen(Color.FromArgb(163, 214, 238), 3) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new PointF[] { new PointF(21, 15), new PointF(21, 34), new PointF(42, 34) });
                g.DrawLine(pen, 21, 24, 42, 24); g.DrawLine(pen, 21, 15, 42, 15);
            }
            using (var fill = new SolidBrush(Color.FromArgb(112, 191, 229))) { g.FillRectangle(fill, 37, 11, 10, 8); g.FillRectangle(fill, 37, 20, 10, 8); }
            using (var fill = new SolidBrush(Color.FromArgb(245, 179, 93))) g.FillRectangle(fill, 37, 30, 10, 8);
            using (var shape = Rounded(13, 41, 38, 13, 4)) using (var fill = new SolidBrush(Color.FromArgb(237, 245, 250))) g.FillPath(fill, shape);
            using (var pen = new Pen(Color.FromArgb(100, 154, 185), 2)) g.DrawLine(pen, 18, 47, 34, 47);
            using (var fill = new SolidBrush(Color.FromArgb(245, 179, 93))) g.FillEllipse(fill, 42, 45, 4, 4);
            using (var bytes = new MemoryStream()) { bitmap.Save(bytes, ImageFormat.Png); return bytes.ToArray(); }
        }
    }
    static void Main(string[] args)
    {
        string destination = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.GetDirectoryName(destination));
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 }; byte[][] images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) images[i] = Render(sizes[i]);
        using (var writer = new BinaryWriter(File.Create(destination)))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (byte[] bytes in images) writer.Write(bytes);
        }
        File.WriteAllBytes(Path.ChangeExtension(destination, ".png"), images[images.Length - 1]);
    }
}

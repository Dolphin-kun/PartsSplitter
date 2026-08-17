using System.Runtime.InteropServices;
using Vortice.Direct2D1;

namespace PartsSplitter
{
    internal static class BitmapReader
    {
        public static RasterBuffer Read(ID2D1DeviceContext dc, ID2D1Bitmap bitmap)
        {
            var bitmap1 = bitmap.QueryInterfaceOrNull<ID2D1Bitmap1>();
            ID2D1Bitmap1 cpuBitmap;

            if (bitmap1 != null && (bitmap1.Options & BitmapOptions.CpuRead) != 0)
            {
                cpuBitmap = bitmap1;
            }
            else
            {
                bitmap1?.Dispose();

                var prop = new BitmapProperties1(
                    new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    96, 96,
                    BitmapOptions.CpuRead | BitmapOptions.CannotDraw);

                cpuBitmap = dc.CreateBitmap(bitmap.PixelSize, prop);
                cpuBitmap.CopyFromBitmap(bitmap);
            }

            int w = cpuBitmap.PixelSize.Width;
            int h = cpuBitmap.PixelSize.Height;
            var data = new byte[w * h * 4];

            var mapped = cpuBitmap.Map(MapOptions.Read);
            try
            {
                int rowBytes = w * 4;
                for (int y = 0; y < h; y++)
                    Marshal.Copy(IntPtr.Add(mapped.Bits, y * mapped.Pitch), data, y * rowBytes, rowBytes);
            }
            finally
            {
                cpuBitmap.Unmap();
                cpuBitmap.Dispose();
            }

            return new RasterBuffer(data, w, h);
        }
    }
}

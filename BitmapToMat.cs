using OpenCvSharp;
using Vortice.Direct2D1;

namespace PartsSplitter
{
    public static class BitmapToMat
    {
        public static Mat BitmapToOpenCvMat(ID2D1DeviceContext dc, ID2D1Bitmap bitmap)
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

            var mapped = cpuBitmap.Map(MapOptions.Read);
            Mat mat;

            try
            {
                int w = cpuBitmap.PixelSize.Width;
                int h = cpuBitmap.PixelSize.Height;
                using var tmp = new Mat(h, w, MatType.CV_8UC4, mapped.Bits, mapped.Pitch);
                mat = tmp.Clone();
            }
            finally
            {
                cpuBitmap.Unmap();
                cpuBitmap.Dispose();
            }

            return mat;
        }
    }
}

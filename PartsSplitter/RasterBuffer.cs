namespace PartsSplitter
{
    internal sealed class RasterBuffer(byte[] data, int width, int height)
    {
        public readonly byte[] Data = data;
        public readonly int Width = width;
        public readonly int Height = height;

        public byte GetLuma(int x, int y)
        {
            int i = (y * Width + x) * 4;
            byte b = Data[i], g = Data[i + 1], r = Data[i + 2];
            return (byte)((r * 54 + g * 183 + b * 19) >> 8);
        }
    }
}

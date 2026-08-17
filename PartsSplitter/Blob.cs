namespace PartsSplitter
{
    internal sealed class Blob
    {
        public long PixelCount;
        public long SumX, SumY;

        public double CenterX => (double)SumX / PixelCount;
        public double CenterY => (double)SumY / PixelCount;
    }
}

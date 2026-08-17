namespace PartsSplitter
{
    internal static class BlobAnalysis
    {
        public static (int[] labels, List<Blob> blobs) Label(bool[] mask, int w, int h)
        {
            var labels = new int[w * h];
            var blobs = new List<Blob>();
            var stack = new Stack<int>();

            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || labels[start] != 0) continue;

                var blob = new Blob();
                int id = blobs.Count + 1;
                labels[start] = id;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int px = p % w, py = p / w;

                    blob.PixelCount++;
                    blob.SumX += px;
                    blob.SumY += py;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = py + dy;
                        if (ny < 0 || ny >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = px + dx;
                            if (nx < 0 || nx >= w) continue;

                            int np = ny * w + nx;
                            if (!mask[np] || labels[np] != 0) continue;
                            labels[np] = id;
                            stack.Push(np);
                        }
                    }
                }
                blobs.Add(blob);
            }
            return (labels, blobs);
        }
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace PartsSplitter
{
    internal class PartsSplitterEffectProcessor : IVideoEffectProcessor
    {
        readonly DisposeCollector disposer = new();
        ID2D1Image? input;
        readonly private IGraphicsDevicesAndContext devices;
        readonly PartsSplitterEffect item;
        readonly IDWriteFactory7 dwriteFactory;

        ID2D1Bitmap1? processed;
        private readonly AffineTransform2D wrap;

        public ID2D1Image Output { get; private set; }

        public PartsSplitterEffectProcessor(IGraphicsDevicesAndContext devices, PartsSplitterEffect item)
        {
            this.devices = devices;
            this.item = item;

            dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory7>();
            disposer.Collect(dwriteFactory);

            wrap = new AffineTransform2D(devices.DeviceContext);
            disposer.Collect(wrap);
            Output = wrap.Output;
        }

        public DrawDescription Update(EffectDescription effectDescription)
        {
            if (input is null)
                return effectDescription.DrawDescription;

            var frame = effectDescription.ItemPosition.Frame;
            var length = effectDescription.ItemDuration.Frame;
            var fps = effectDescription.FPS;

            var count = (int)item.Count.GetValue(frame, length, fps);
            double threshRatio = item.Thresh.GetValue(frame, length, fps);
            double thresh = threshRatio / 100.0 * 255.0;

            var dc = devices.DeviceContext;

            var bounds = dc.GetImageLocalBounds(input);
            int width = (int)(bounds.Right - bounds.Left);
            int height = (int)(bounds.Bottom - bounds.Top);

            if (width <= 0 || height <= 0)
            {
                wrap.TransformMatrix = Matrix3x2.Identity;
                wrap.SetInput(0, input, true);
                return effectDescription.DrawDescription;
            }

            using var target = dc.CreateBitmap(new SizeI(width, height),
                new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                    96, 96,
                    BitmapOptions.Target));

            dc.Target = target;
            dc.BeginDraw();
            dc.Clear(null);
            dc.DrawImage(input, new Vector2(-bounds.Left, -bounds.Top));
            dc.EndDraw();
            dc.Target = null;

            var raster = BitmapReader.Read(dc, target);

            var mask = new bool[width * height];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    mask[py * width + px] = raster.GetLuma(px, py) > thresh;
                }
            }

            var (labels, blobs) = BlobAnalysis.Label(mask, width, height);
            int numLabels = blobs.Count + 1;

            var labelGroups = numLabels > 1 ? ParseSortNum(item.SortNum, numLabels) : [];

            HashSet<int> selectedLabels;
            if (numLabels <= 1)
            {
                selectedLabels = [];
            }
            else if (labelGroups.Count > 0)
            {
                int clampedIndex = Math.Clamp(count - 1, 0, labelGroups.Count - 1);
                selectedLabels = [.. labelGroups[clampedIndex]];
            }
            else
            {
                if (count < 1 || count >= numLabels)
                {
                    count = numLabels - 1;
                }
                selectedLabels = [count];
            }

            var result = new byte[width * height * 4];
            for (int i = 0; i < labels.Length; i++)
            {
                int label = labels[i];
                if (label == 0 || !selectedLabels.Contains(label))
                    continue;

                int o = i * 4;
                result[o] = raster.Data[o];
                result[o + 1] = raster.Data[o + 1];
                result[o + 2] = raster.Data[o + 2];
                result[o + 3] = raster.Data[o + 3];
            }

            bool drawLabels = item.ShowIndexLabel && effectDescription.Usage == TimelineSourceUsage.Paused;

            if (processed == null || processed.PixelSize.Width != width || processed.PixelSize.Height != height)
            {
                disposer.RemoveAndDispose(ref processed);
                processed = dc.CreateBitmap(
                    new SizeI(width, height),
                    nint.Zero,
                    0,
                    new BitmapProperties1(
                        new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                        96, 96,
                        BitmapOptions.Target));
                disposer.Collect(processed);
            }

            var handle = GCHandle.Alloc(result, GCHandleType.Pinned);
            ID2D1Bitmap1 maskedSource;
            try
            {
                maskedSource = dc.CreateBitmap(
                    new SizeI(width, height),
                    handle.AddrOfPinnedObject(),
                    width * 4,
                    new BitmapProperties1(
                        new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                        96, 96,
                        BitmapOptions.None));
            }
            finally
            {
                handle.Free();
            }

            dc.Target = processed;
            dc.BeginDraw();
            dc.Clear(null);
            dc.DrawImage(maskedSource, Vector2.Zero);

            if (drawLabels)
            {
                DrawLabels(dc, blobs, labelGroups);
            }

            dc.EndDraw();
            dc.Target = null;
            maskedSource.Dispose();

            var processedRange = devices.DeviceContext.GetImageLocalBounds(processed);
            var x = -(processedRange.Left + processedRange.Right) / 2;
            var y = -(processedRange.Top + processedRange.Bottom) / 2;
            wrap.TransformMatrix = Matrix3x2.CreateTranslation(x, y);
            wrap.SetInput(0, processed, true);

            return effectDescription.DrawDescription;
        }

        static List<List<int>> ParseSortNum(string sortNum, int numLabels)
        {
            var labelGroups = new List<List<int>>();

            if (string.IsNullOrWhiteSpace(sortNum))
                return labelGroups;

            var normalizedString = sortNum.Replace('.', ',');
            var groupStrings = normalizedString.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var group in groupStrings)
            {
                var groupSet = new HashSet<int>();
                var elements = group.Split('-', StringSplitOptions.RemoveEmptyEntries);

                foreach (var elem in elements)
                {
                    if (elem.Contains('~'))
                    {
                        var rangeParts = elem.Split('~');
                        if (rangeParts.Length == 2 &&
                            int.TryParse(rangeParts[0], out int start) &&
                            int.TryParse(rangeParts[1], out int end))
                        {
                            if (start > end)
                            {
                                (start, end) = (end, start);
                            }

                            for (int i = start; i <= end; i++)
                            {
                                if (i >= 1 && i < numLabels)
                                {
                                    groupSet.Add(i);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (int.TryParse(elem, out int num) && num >= 1 && num < numLabels)
                        {
                            groupSet.Add(num);
                        }
                    }
                }

                if (groupSet.Count > 0)
                    labelGroups.Add([.. groupSet]);
            }

            return labelGroups;
        }

        void DrawLabels(ID2D1DeviceContext dc, List<Blob> blobs, List<List<int>> labelGroups)
        {
            const float boxSize = 120f;

            using var grayBrush = dc.CreateSolidColorBrush(new Color4(0.5f, 0.5f, 0.5f, 1f));
            using var blackBrush = dc.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 1f));
            using var whiteBrush = dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));

            using var smallFormat = dwriteFactory.CreateTextFormat("Meiryo", Vortice.DirectWrite.FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, Vortice.DirectWrite.FontStretch.Normal, 18f);
            smallFormat.TextAlignment = Vortice.DirectWrite.TextAlignment.Center;
            smallFormat.ParagraphAlignment = Vortice.DirectWrite.ParagraphAlignment.Center;

            using var largeFormat = dwriteFactory.CreateTextFormat("Meiryo", Vortice.DirectWrite.FontWeight.Bold, Vortice.DirectWrite.FontStyle.Normal, Vortice.DirectWrite.FontStretch.Normal, 22f);
            largeFormat.TextAlignment = Vortice.DirectWrite.TextAlignment.Center;
            largeFormat.ParagraphAlignment = Vortice.DirectWrite.ParagraphAlignment.Center;

            for (int label = 1; label <= blobs.Count; label++)
            {
                var b = blobs[label - 1];
                var rect = new Rect((float)b.CenterX - boxSize / 2, (float)b.CenterY - boxSize / 2, boxSize, boxSize);
                dc.DrawText(label.ToString(), smallFormat, rect, grayBrush);
            }

            for (int i = 0; i < labelGroups.Count; i++)
            {
                string text = (i + 1).ToString();

                foreach (var label in labelGroups[i])
                {
                    if (label < 1 || label > blobs.Count)
                        continue;

                    var b = blobs[label - 1];
                    var rect = new Rect((float)b.CenterX - boxSize / 2, (float)b.CenterY - boxSize / 2, boxSize, boxSize);
                    DrawOutlinedText(dc, text, largeFormat, rect, blackBrush, whiteBrush);
                }
            }
        }

        static void DrawOutlinedText(ID2D1DeviceContext dc, string text, IDWriteTextFormat format, Rect rect, ID2D1Brush outline, ID2D1Brush fill)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var r = new Rect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
                    dc.DrawText(text, format, r, outline);
                }
            }
            dc.DrawText(text, format, rect, fill);
        }

        public void ClearInput()
        {
            wrap.SetInput(0, null, true);
        }

        public void SetInput(ID2D1Image? input)
        {
            this.input = input;
        }

        public void Dispose()
        {
            Output?.Dispose();
            disposer.RemoveAndDispose(ref processed);
            disposer.Dispose();
        }
    }
}

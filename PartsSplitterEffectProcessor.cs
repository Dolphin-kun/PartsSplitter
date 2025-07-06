using OpenCvSharp;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
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

        ID2D1Bitmap1? processed;
        AffineTransform2D wrap;

        public ID2D1Image Output { get; private set; }

        public PartsSplitterEffectProcessor(IGraphicsDevicesAndContext devices, PartsSplitterEffect item)
        {
            this.devices = devices;
            this.item = item;

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

            using var mat = BitmapToMat.BitmapToOpenCvMat(dc, target);

            if (mat.Empty())
            {
                wrap.SetInput(0, input, true);
                return effectDescription.DrawDescription;
            }

            using var gray = new Mat();
            using var binary = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGRA2GRAY);
            Cv2.Threshold(gray, binary, thresh, 255, ThresholdTypes.Binary);

            //CCL
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int numLabels = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

            var labelGroups = new List<List<int>>();

            if (!string.IsNullOrWhiteSpace(item.SortNum))
            {
                var groupStrings = item.SortNum.Split(',', StringSplitOptions.RemoveEmptyEntries);

                foreach (var group in groupStrings)
                {
                    var groupList = new List<int>();

                    var elements = group.Split('-', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var elem in elements)
                    {
                        if (int.TryParse(elem, out int num) && num >= 1 && num < numLabels)
                        {
                            groupList.Add(num);
                        }
                    }

                    if (groupList.Count > 0)
                        labelGroups.Add(groupList);
                }
            }



            using var result = new Mat(mat.Rows, mat.Cols, MatType.CV_8UC4, Scalar.All(0));

            if (labelGroups.Count > 0)
            {
                int clampedIndex = Math.Clamp(count - 1, 0, labelGroups.Count - 1);
                var maskGroup = labelGroups[clampedIndex]; // List<int>

                using var combinedMask = new Mat(mat.Rows, mat.Cols, MatType.CV_8UC1, Scalar.All(0));

                for (int i = 0; i < maskGroup.Count; i++)
                {
                    int label = maskGroup[i];
                    using var mask = new Mat();
                    Cv2.Compare(labels, label, mask, CmpType.EQ);
                    Cv2.BitwiseOr(combinedMask, mask, combinedMask);
                }

                mat.CopyTo(result, combinedMask);
            }
            else
            {
                if (count < 1 || count >= numLabels)
                {
                    // 範囲外なら最後のラベル番号を使う
                    count = numLabels - 1;
                }

                using var singleMask = new Mat();
                Cv2.Compare(labels, count, singleMask, CmpType.EQ);
                mat.CopyTo(result, singleMask);
            }


            // 番号を描画
            if (item.ShowIndexLabel && effectDescription.Usage == TimelineSourceUsage.Paused)
            {
                for (int label = 1; label < numLabels; label++)
                {
                    double cx = centroids.Get<double>(label, 0);
                    double cy = centroids.Get<double>(label, 1);
                    string text = label.ToString();

                    var point = new Point((int)cx, (int)cy);

                    Cv2.PutText(result,text,point,HersheyFonts.HersheyDuplex,0.7,Scalar.Gray, 1,LineTypes.AntiAlias);
                }

                for (int i = 0; i < labelGroups.Count; i++)
                {
                    var group = labelGroups[i];

                    foreach (var label in group)
                    {
                        double cx = centroids.Get<double>(label, 0);
                        double cy = centroids.Get<double>(label, 1);

                        string text = (i + 1).ToString();

                        var point = new Point((int)cx, (int)cy);

                        Cv2.PutText(result, text, point, HersheyFonts.HersheyDuplex, 0.7, Scalar.Black, 3, LineTypes.AntiAlias);
                        Cv2.PutText(result, text, point, HersheyFonts.HersheyDuplex, 0.7, Scalar.White, 2, LineTypes.AntiAlias);
                    }
                }

            }

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
                        BitmapOptions.None));
                disposer.Collect(processed);
            }

            processed.CopyFromMemory(result.Data, (int)result.Step());

            var processedRange = devices.DeviceContext.GetImageLocalBounds(processed);
            var x = -(processedRange.Left + processedRange.Right) / 2;
            var y = -(processedRange.Top + processedRange.Bottom) / 2;
            wrap.TransformMatrix = Matrix3x2.CreateTranslation(x, y);
            wrap.SetInput(0, processed, true);

            return effectDescription.DrawDescription;
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

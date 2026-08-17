using PartsSplitter.PartsSplitterEnum;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace PartsSplitter
{
    [VideoEffect("パーツ分解", ["加工"], ["Parts Splitter", "パーツ分解"], isAviUtlSupported: false)]
    public class PartsSplitterEffect : VideoEffectBase
    {
        public override string Label => "パーツ分解";

        [Display(GroupName = "パーツ分解", Name = "パーツ", Description = "表示するパーツの数値")]
        [AnimationSlider("F0", "", 1, 10)]
        public Animation Count { get; } = new Animation(1, 1, 1000);

        [Display(GroupName = "パーツ分解", Name = "表示", Description = "表示形式")]
        [EnumComboBox]
        public OrderMode Order_Mode { get => order_Enum; set => Set(ref order_Enum, value); }
        OrderMode order_Enum = OrderMode.Default;

        [Display(GroupName = "パーツ分解", Name = "しきい値", Description = "パーツを抽出する強度")]
        [AnimationSlider("F1", "%", 0, 100)]
        public Animation Thresh { get; } = new Animation(50, 0, 100);

        [Display(GroupName = "パーツ分解", Name = "順番", Description = "順番の指定")]
        [ShowPropertyEditorWhen(nameof(Order_Mode), OrderMode.Custom)]
        [TextEditor(AcceptsReturn = true)]
        public string SortNum { get => text; set => Set(ref text, value); }
        string text = string.Empty;

        [Display(GroupName = "パーツ分解", Name = "順番表示", Description = "パーツの数値を表示する")]
        [ToggleSlider]
        public bool ShowIndexLabel { get => showIndexLabel; set => Set(ref showIndexLabel, value); }
        bool showIndexLabel = false;

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            return [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new PartsSplitterEffectProcessor(devices, this);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Count, Thresh];
    }
}

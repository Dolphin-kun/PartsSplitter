using System.ComponentModel.DataAnnotations;

namespace PartsSplitter.PartsSplitterEnum
{
    public enum OrderMode
    {
        [Display(Name = "デフォルト", Description = "デフォルト")]
        Default = 1,

        [Display(Name = "カスタム", Description = "カスタム")]
        Custom = 2,
    }
}

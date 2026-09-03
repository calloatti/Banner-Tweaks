using System.Collections.Generic;

namespace Calloatti.BannerTweaks
{
    internal class DecalGroupSpec
    {
        public string Id { get; set; }
        public string TitleLoc { get; set; }
        public int Order { get; set; }
        public string Category { get; set; }
        public List<string> DecalIdExacts { get; set; }
        public List<string> DecalIdPatterns { get; set; }
    }
}

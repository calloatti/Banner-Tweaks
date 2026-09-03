using System.Collections.Generic;

namespace Calloatti.BannerTweaks
{
    internal class GroupNode
    {
        internal string Id;
        internal string DisplayName;
        internal int Order;
        internal GroupNode Parent;
        internal List<GroupNode> Children = new();
        internal List<string> DecalIds = new();
        internal bool IsSpecGroup;

        internal GroupNode(string id, string displayName, int order, bool isSpecGroup, GroupNode parent = null)
        {
            Id = id;
            DisplayName = displayName;
            Order = order;
            IsSpecGroup = isSpecGroup;
            Parent = parent;
        }
    }
}

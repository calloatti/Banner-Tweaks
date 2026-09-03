using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Timberborn.DecalSystem;
using Timberborn.Localization;
using Timberborn.Modding;
using Timberborn.SingletonSystem;

namespace Calloatti.BannerTweaks
{
  internal class DecalMapping
  {
    public string ParentGroupId;
    public string GroupId;
    public string DisplayName;
    public int Order;
    public string DecalId;
    public bool IsSpecGroup;
    public bool IsFolderMapping;
  }

  public class DecalGroupStore : ILoadableSingleton
  {
    internal static DecalGroupStore Instance;

    private readonly ModRepository _modRepository;
    private readonly IDecalService _decalService;
    private readonly ILoc _loc;

    internal static Dictionary<string, List<DecalMapping>> Mappings = new();

    internal Dictionary<string, List<GroupNode>> GroupTrees = new();

    public DecalGroupStore(ModRepository modRepository, IDecalService decalService, ILoc loc)
    {
      Instance = this;
      _modRepository = modRepository;
      _decalService = decalService;
      _loc = loc;

      ParseAndResolveSpecGroups();
    }

    public void Load()
    {
    }

    internal static void ClearFolderMappings(string category)
    {
      if (Mappings.TryGetValue(category, out var list))
        list.RemoveAll(m => m.IsFolderMapping);
    }

    internal void ClearCache(string category)
    {
      GroupTrees.Remove(category);
    }

    private static List<DecalMapping> GetOrCreateMappingList(string category)
    {
      if (!Mappings.TryGetValue(category, out var list))
      {
        list = new List<DecalMapping>();
        Mappings[category] = list;
      }
      return list;
    }

    internal static void RecordDecal(string category, string folderPath, string decalId)
    {
      var list = GetOrCreateMappingList(category);

      if (string.IsNullOrEmpty(folderPath))
      {
        list.Add(new DecalMapping { ParentGroupId = "", GroupId = "", DecalId = decalId, IsFolderMapping = true });
        return;
      }

      var segments = folderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var pathSoFar = "";

      for (int i = 0; i < segments.Length; i++)
      {
        var parent = pathSoFar;
        pathSoFar = string.IsNullOrEmpty(pathSoFar) ? segments[i] : pathSoFar + "\\" + segments[i];

        if (i == segments.Length - 1)
        {
          list.Add(new DecalMapping { ParentGroupId = parent, GroupId = pathSoFar, DisplayName = segments[i], DecalId = decalId, IsFolderMapping = true });
        }
        else
        {
          list.Add(new DecalMapping { ParentGroupId = parent, GroupId = pathSoFar, DisplayName = segments[i], DecalId = null, IsFolderMapping = true });
        }
      }
    }

    internal List<GroupNode> GetGroupsForCategory(string category)
    {
      if (!GroupTrees.ContainsKey(category))
        ReconcileCategory(category);

      return GroupTrees.GetValueOrDefault(category) ?? new List<GroupNode>();
    }

    private void ReconcileCategory(string category)
    {
      var allDecalIds = _decalService.GetDecals(category).Select(d => d.Id).ToList();
      var roots = BuildTreeForCategory(category, allDecalIds);
      GroupTrees[category] = roots;
      if (roots.Count > 0)
        roots[0].DecalIds = allDecalIds;
    }

    private List<GroupNode> BuildTreeForCategory(string category, List<string> validDecalIds)
    {
      var roots = new List<GroupNode>();
      var rootNode = new GroupNode("", category, 0, false);
      roots.Add(rootNode);

      var nodeMap = new Dictionary<string, GroupNode> { [""] = rootNode };
      var list = GetOrCreateMappingList(category);

      // Filter mappings against the master list of valid game decals
      var validMappings = list.Where(m => string.IsNullOrEmpty(m.DecalId) || validDecalIds.Contains(m.DecalId)).ToList();

      foreach (var m in validMappings)
      {
        if (string.IsNullOrEmpty(m.GroupId)) continue;

        if (!nodeMap.TryGetValue(m.GroupId, out var node))
        {
          node = new GroupNode(m.GroupId, m.DisplayName, m.Order, m.IsSpecGroup, null);
          nodeMap[m.GroupId] = node;
        }
        else
        {
          if (!string.IsNullOrEmpty(m.DisplayName) && (string.IsNullOrEmpty(node.DisplayName) || node.DisplayName == m.GroupId))
            node.DisplayName = m.DisplayName;
          if (m.Order != 0)
            node.Order = m.Order;
          if (m.IsSpecGroup)
            node.IsSpecGroup = true;
        }
      }

      foreach (var m in validMappings)
      {
        if (string.IsNullOrEmpty(m.GroupId))
        {
          if (!string.IsNullOrEmpty(m.DecalId) && !rootNode.DecalIds.Contains(m.DecalId))
            rootNode.DecalIds.Add(m.DecalId);
          continue;
        }

        var node = nodeMap[m.GroupId];

        if (node.Parent == null)
        {
          var parentId = m.ParentGroupId ?? "";

          if (parentId == m.GroupId)
            parentId = "";

          if (nodeMap.TryGetValue(parentId, out var parentNode))
          {
            node.Parent = parentNode;
            if (!parentNode.Children.Contains(node))
              parentNode.Children.Add(node);
          }
          else
          {
            node.Parent = rootNode;
            if (!rootNode.Children.Contains(node))
              rootNode.Children.Add(node);
          }
        }

        if (!string.IsNullOrEmpty(m.DecalId) && !node.DecalIds.Contains(m.DecalId))
          node.DecalIds.Add(m.DecalId);
      }

      SortChildren(rootNode);
      PruneEmptyGroups(rootNode);
      return roots;
    }

    private static void SortChildren(GroupNode node)
    {
      node.Children.Sort((a, b) =>
      {
        var cmp = a.Order.CompareTo(b.Order);
        return cmp != 0 ? cmp : string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase);
      });
      foreach (var child in node.Children)
        SortChildren(child);
    }

    private static void PruneEmptyGroups(GroupNode node)
    {
      for (int i = node.Children.Count - 1; i >= 0; i--)
      {
        PruneEmptyGroups(node.Children[i]);
        if (node.Children[i].DecalIds.Count == 0 && node.Children[i].Children.Count == 0)
          node.Children.RemoveAt(i);
      }
    }

    private void ParseAndResolveSpecGroups()
    {
      foreach (var list in Mappings.Values)
        list.RemoveAll(m => m.IsSpecGroup);

      foreach (var mod in _modRepository.EnabledMods)
      {
        var modDir = mod.ModDirectory.Directory;
        if (modDir == null || !modDir.Exists)
          continue;

        var modDecalsByCategory = new Dictionary<string, HashSet<string>>();
        var modGroups = new List<DecalGroupSpec>();

        foreach (var file in modDir.GetFiles("*.json", SearchOption.AllDirectories))
        {
          try
          {
            var json = File.ReadAllText(file.FullName);
            var rootToken = JToken.Parse(json);

            var decalSpecs = new List<JToken>();
            var groupSpecs = new List<JToken>();
            ExtractTokens(rootToken, decalSpecs, groupSpecs);

            foreach (var decalToken in decalSpecs)
            {
              var texPath = decalToken["Texture"]?.ToString();
              var category = decalToken["Category"]?.ToString();

              if (!string.IsNullOrEmpty(texPath) && !string.IsNullOrEmpty(category))
              {
                // Changed Path.GetFileNameWithoutExtension to Path.GetFileName
                // to preserve dot notation in Decal IDs (e.g., "CustomBanner.Algae")
                var id = Path.GetFileName(texPath);
                if (!modDecalsByCategory.ContainsKey(category))
                  modDecalsByCategory[category] = new HashSet<string>();
                modDecalsByCategory[category].Add(id);
              }
            }

            foreach (var specToken in groupSpecs)
            {
              var spec = specToken.ToObject<DecalGroupSpec>();
              if (spec?.Id != null && spec.Category != null)
                modGroups.Add(spec);
            }
          }
          catch { }
        }

        var allCategories = new HashSet<string>(modDecalsByCategory.Keys);
        foreach (var spec in modGroups)
        {
          if (!string.IsNullOrEmpty(spec.Category))
            allCategories.Add(spec.Category);
        }

        foreach (var category in allCategories)
        {
          var list = GetOrCreateMappingList(category);
          var categoryDecals = modDecalsByCategory.GetValueOrDefault(category) ?? new HashSet<string>();

          list.Add(new DecalMapping { ParentGroupId = "", GroupId = mod.Manifest.Name, DisplayName = mod.Manifest.Name, IsSpecGroup = true });

          foreach (var decalId in categoryDecals)
          {
            list.Add(new DecalMapping { ParentGroupId = "", GroupId = mod.Manifest.Name, DecalId = decalId, IsSpecGroup = true });
          }

          var categoryGroups = modGroups.Where(g => g.Category == category).ToList();

          foreach (var spec in categoryGroups)
          {
            var displayName = !string.IsNullOrEmpty(spec.TitleLoc) ? _loc.T(spec.TitleLoc) : spec.Id;
            var absoluteGroupId = mod.Manifest.Name + "\\" + spec.Id;

            if (spec.Id == mod.Manifest.Name)
              absoluteGroupId = mod.Manifest.Name;

            list.Add(new DecalMapping { ParentGroupId = mod.Manifest.Name, GroupId = absoluteGroupId, DisplayName = displayName, Order = spec.Order, IsSpecGroup = true });

            var resolved = new HashSet<string>();

            if (spec.DecalIdExacts != null)
            {
              foreach (var id in spec.DecalIdExacts)
              {
                if (categoryDecals.Contains(id))
                  resolved.Add(id);
              }
            }

            if (spec.DecalIdPatterns != null)
            {
              foreach (var id in categoryDecals)
              {
                if (resolved.Contains(id)) continue;
                foreach (var pattern in spec.DecalIdPatterns)
                {
                  try
                  {
                    if (Regex.IsMatch(id, pattern))
                    {
                      resolved.Add(id);
                      break;
                    }
                  }
                  catch { }
                }
              }
            }

            foreach (var id in resolved)
            {
              list.Add(new DecalMapping { ParentGroupId = mod.Manifest.Name, GroupId = absoluteGroupId, DecalId = id, IsSpecGroup = true });
            }
          }
        }
      }
    }
    private static void ExtractTokens(JToken token, List<JToken> decalSpecs, List<JToken> groupSpecs)
    {
      if (token is JObject obj)
      {
        if (obj.TryGetValue("DecalSpec", out var ds))
        {
          if (ds is JArray dsArr) foreach (var item in dsArr) decalSpecs.Add(item);
          else decalSpecs.Add(ds);
        }

        if (obj.TryGetValue("DecalGroupSpec", out var dgs))
        {
          if (dgs is JArray dgsArr) foreach (var item in dgsArr) groupSpecs.Add(item);
          else groupSpecs.Add(dgs);
        }

        foreach (var prop in obj.Properties())
          ExtractTokens(prop.Value, decalSpecs, groupSpecs);
      }
      else if (token is JArray arr)
      {
        foreach (var item in arr)
          ExtractTokens(item, decalSpecs, groupSpecs);
      }
    }
  }
}
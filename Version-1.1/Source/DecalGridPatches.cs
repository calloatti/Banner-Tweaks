using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Timberborn.DecalSystem;
using Timberborn.DecalSystemUI;
using Timberborn.BaseComponentSystem;
using Timberborn.SelectionSystem;
using UnityEngine;

namespace Calloatti.BannerTweaks
{
  [HarmonyPatch]
  internal static class DecalGridPatches
  {
    private static BaseComponent _lastClickedEntity;
    private static float _lastClickTime;
    private static bool _openedPanel;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DecalSupplierFragment), "OnBrowseButtonClicked")]
    private static bool OnBrowseButtonClickedPrefix(DecalSupplierFragment __instance)
    {
      DecalGridPanel.Instance?.Show(__instance._decalSupplier);
      return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntitySelectionService), nameof(EntitySelectionService.Select))]
    private static void OnSelectEntityPrefix(BaseComponent target)
    {
      if (target == null || DecalGridPanel.Instance == null)
        return;

      float now = Time.unscaledTime;
      bool isDoubleClick = target == _lastClickedEntity
                           && (now - _lastClickTime) <= 0.5f;

      _lastClickedEntity = target;
      _lastClickTime = now;

      if (isDoubleClick)
      {
        var decalSupplier = target.GetComponent<DecalSupplier>();
        if (decalSupplier != null)
        {
          _openedPanel = true;
          DecalGridPanel.Instance.PlayClickSound();
          DecalGridPanel.Instance.Show(decalSupplier);
        }
      }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntitySelectionService), nameof(EntitySelectionService.Select))]
    private static void OnSelectEntityPostfix()
    {
      if (_openedPanel)
      {
        _openedPanel = false;
        return;
      }

      if (DecalGridPanel.Instance?.IsShowing == true)
        DecalGridPanel.Instance.Close();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EntitySelectionService), nameof(EntitySelectionService.Unselect), new System.Type[0])]
    private static void OnUnselectPostfix()
    {
      if (_openedPanel)
      {
        _openedPanel = false;
        return;
      }

      if (DecalGridPanel.Instance?.IsShowing == true)
        DecalGridPanel.Instance.Close();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UserDecalTextureRepository), nameof(UserDecalTextureRepository.LoadCustomTextures))]
    private static void LoadCustomTexturesPostfix(UserDecalTextureRepository __instance, string category, ref IEnumerable<Texture2D> __result)
    {
      // Calling static methods directly, entirely bypassing the Instance null check
      DecalGroupStore.ClearFolderMappings(category);

      var basePath = __instance.GetCustomDecalDirectory(category);
      if (Directory.Exists(basePath))
      {
        // Tag the root folder decals directly from the physical disk, bypassing __result
        foreach (var filePath in GetImageFiles(basePath))
        {
          var fileName = Path.GetFileName(filePath);
          DecalGroupStore.RecordDecal(category, "", fileName);
        }

        var resultList = __result.ToList();
        var existingNames = new HashSet<string>(resultList.Select(t => t.name));

        var loadedDict = __instance._loadedTextures;
        if (loadedDict != null && loadedDict.TryGetValue(category, out var loadedList))
        {
          LoadFromSubDirs(__instance, basePath, basePath, existingNames, resultList, loadedList, category);
          __result = resultList;
        }
      }

      // If the singleton exists, wipe its cache so it pulls the latest static Mappings when you open the UI
      if (DecalGroupStore.Instance != null)
        DecalGroupStore.Instance.ClearCache(category);
    }

    private static void LoadFromSubDirs(
        UserDecalTextureRepository instance,
        string basePath,
        string currentDir,
        HashSet<string> existingNames,
        List<Texture2D> resultList,
        List<Texture2D> loadedList,
        string category)
    {
      foreach (var subDir in Directory.GetDirectories(currentDir))
      {
        foreach (var filePath in GetImageFiles(subDir))
        {
          var relativePath = GetRelativePath(basePath, filePath);
          if (existingNames.Contains(relativePath))
            continue;

          try
          {
            var bytes = File.ReadAllBytes(filePath);
            var tex = new Texture2D(1, 1);
            tex.LoadImage(bytes);
            tex.name = relativePath;

            var folder = Path.GetDirectoryName(relativePath) ?? "";
            DecalGroupStore.RecordDecal(category, folder, relativePath);

            resultList.Add(tex);
            loadedList.Add(tex);
            existingNames.Add(relativePath);
          }
          catch
          {
          }
        }

        LoadFromSubDirs(instance, basePath, subDir, existingNames, resultList, loadedList, category);
      }
    }

    private static string GetRelativePath(string basePath, string filePath)
    {
      var fullBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
      var fullFile = Path.GetFullPath(filePath);
      return fullFile.Substring(fullBase.Length);
    }

    private static IEnumerable<string> GetImageFiles(string directory)
    {
      foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
      {
        foreach (var file in Directory.GetFiles(directory, "*" + ext))
          yield return file;
      }
    }
  }
}
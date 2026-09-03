using HarmonyLib;
using Timberborn.Modding;
using Timberborn.ModManagerScene;

namespace Calloatti.BannerTweaks
{
  public class ModStarter : IModStarter
  {
    public void StartMod(IModEnvironment modEnvironment)
    {
      new Harmony("Calloatti.BannerTweaks").PatchAll();
    }
  }
}

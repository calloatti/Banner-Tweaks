using HarmonyLib;
using Timberborn.DecalSystem;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Calloatti.BannerTweaks
{
    [HarmonyPatch]
    internal static class DecalEdgeFadePatch
    {
        private static readonly int GradientPropId = Shader.PropertyToID("_DetailAlbedoUV3Gradient");
        private static readonly int ColorPropId = Shader.PropertyToID("_DetailAlbedoUV3Color");
        private static readonly int IconPropertyId = Shader.PropertyToID("_DetailAlbedoMap3");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DecalSupplierBuildingIcon), nameof(DecalSupplierBuildingIcon.UpdateIcon))]
        private static void UpdateIconPostfix(DecalSupplierBuildingIcon __instance)
        {
            var templateSpec = __instance.GetComponent<TemplateSpec>();
            if (templateSpec == null || !templateSpec.TemplateName.StartsWith("BorderlessSquareBanner"))
                return;

            var iconRenderer = __instance._iconRenderer;
            if (iconRenderer?.material != null)
            {
                var mat = iconRenderer.material;
                mat.SetFloat(GradientPropId, 0f);
                var color = mat.GetVector(ColorPropId);
                color.w = 1f;
                mat.SetVector(ColorPropId, color);
                var tex = mat.GetTexture(IconPropertyId);
                if (tex != null) tex.wrapMode = TextureWrapMode.Clamp;
            }
        }
    }
}
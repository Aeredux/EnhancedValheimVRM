using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(GameCamera), "GetCameraBaseOffset")]
    internal static class PatchGameCameraGetCameraBaseOffset
    {
        // Vanilla anchors the camera on player.m_eye. That pivot stays put through a
        // pickaxe swing. Replacing it with the VRM left-eye bone made the view bob,
        // because the swing rotates the head and the eye orbits around it.
        private static void Postfix(Player player, ref Vector3 __result)
        {
            if (player == null) return;
            if (player.InBed())
            {
                __result = player.GetHeadPoint() - player.transform.position;
                return;
            }

            var vrmInstance = player.GetVrmInstance();
            if (vrmInstance == null) return;
            var settings = vrmInstance.GetSettings();
            if (!settings.FixCameraHeight) return;

            if (player.IsAttached() || player.IsSitting())
            {
                // 0.3f is the vanilla camera lift above the head while seated or attached.
                var scaledDistance = Vector3.up * 0.3f * settings.PlayerVrmScale;
                __result = player.GetHeadPoint() + scaledDistance - player.transform.position;
                return;
            }

            // Keep the vanilla eye offset, including crouch, and only scale its height
            // so a shorter avatar is not looked at from the vanilla eye line.
            if (settings.PlayerVrmScale > 0f)
                __result.y *= settings.PlayerVrmScale;
        }
    }
}

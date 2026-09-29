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
            // CollideRay2 still starts one cast at m_eye; PatchGameCameraCollideRay shifts
            // that cast by the same amount or the two rays fight near a wall.
            if (settings.PlayerVrmScale > 0f)
                __result.y *= settings.PlayerVrmScale;
        }
    }

    // Near a wall the game spherecasts from m_eye and from the camera pivot, then blends
    // the hits while the camera is between about 0.5 m and 2 m. Scaling only the pivot
    // leaves those origins at different heights, and the blend flips as you walk the wall.
    [HarmonyPatch(typeof(GameCamera), "CollideRay2")]
    internal static class PatchGameCameraCollideRay
    {
        private static bool Prepare()
        {
            var method = AccessTools.Method(typeof(GameCamera), "CollideRay2");
            if (method == null)
            {
                Logger.LogWarning(
                    "GameCamera.CollideRay2 was not found; a scaled camera height may jump near walls.");
                return false;
            }

            var eye = false;
            var offset = false;
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.Name == "eyePos") eye = true;
                if (parameter.Name == "offsetedEyePos") offset = true;
            }

            if (eye && offset) return true;
            Logger.LogWarning(
                "GameCamera.CollideRay2 parameters changed; a scaled camera height may jump near walls.");
            return false;
        }

        private static void Prefix(ref Vector3 eyePos)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.m_eye == null || player.InBed() || player.IsAttached() ||
                player.IsSitting())
                return;
            var vrmInstance = player.GetVrmInstance();
            if (vrmInstance == null) return;
            var settings = vrmInstance.GetSettings();
            if (!settings.FixCameraHeight) return;
            var scale = settings.PlayerVrmScale;
            if (!(scale > 0f)) return;
            var eyeHeight = player.m_eye.position.y - player.transform.position.y;
            eyePos.y += eyeHeight * (scale - 1f);
        }
    }
}

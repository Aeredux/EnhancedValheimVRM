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
            // so a shorter avatar is not looked at from the vanilla eye line. A taller
            // pivot is clamped under a roof: the jump repro snapped because the scaled
            // eye was inside the ceiling and the collision cast missed every other frame.
            if (settings.PlayerVrmScale > 0f)
                __result.y = ClampEyeHeight(player, __result, settings.PlayerVrmScale);
        }

        internal static float ClampEyeHeight(Player player, Vector3 vanillaOffset, float scale)
        {
            var originalY = vanillaOffset.y;
            var wantedY = originalY * scale;
            if (wantedY <= originalY + 0.02f) return wantedY;
            var clearance = RoofClearance();
            var origin = player.transform.position + new Vector3(vanillaOffset.x, originalY, vanillaOffset.z);
            var extra = wantedY - originalY;
            if (!Physics.Raycast(origin,
                    Vector3.up,
                    out var hit,
                    extra + clearance,
                    CameraBlockMask,
                    QueryTriggerInteraction.Ignore))
                return wantedY;
            // Stay a sphere-width under the ceiling. A pivot inside the arch makes the
            // collision cast miss one frame and hit the next, which is the jump snap.
            return originalY + Mathf.Max(0f, Mathf.Min(extra, hit.distance - clearance));
        }

        // Point clearance so the camera sphere is not already inside the roof.
        private static float RoofClearance()
        {
            const float fallback = 0.2f;
            var camera = GameCamera.instance;
            if (camera == null) return fallback;
            return Mathf.Max(fallback, camera.m_raycastWidth + 0.05f);
        }

        // piece roofs, terrain, and solid world. Not Default: that includes the avatar.
        private static readonly int CameraBlockMask =
            LayerMask.GetMask("terrain", "static_solid", "piece", "viewblock");
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

        private static void Prefix(ref Vector3 eyePos, Vector3 offsetedEyePos)
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
            // Same height as the pivot the other cast starts from, including while that
            // pivot is still smoothing during a jump. A reconstructed scale delta lags
            // and the two hits trade off, which is the in-and-out zoom in the clips.
            if (scale > 0f && Mathf.Abs(scale - 1f) > 0.001f)
                eyePos.y = offsetedEyePos.y;
        }
    }
}

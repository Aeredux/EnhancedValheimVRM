using UnityEngine;

namespace EnhancedValheimVRM
{
    // Kept so existing setup and teardown still find the component. It must not copy
    // the animated VRM eye onto player.m_eye: that pivot is the vanilla camera anchor,
    // and a mining swing moves the eye bone. Camera height is scaled in
    // PatchGameCameraGetCameraBaseOffset instead.
    public class VrmEyeAnimator : MonoBehaviour
    {
        public void Setup(Player player, Animator playerAnimator, VrmInstance vrmInstance)
        {
        }
    }
}

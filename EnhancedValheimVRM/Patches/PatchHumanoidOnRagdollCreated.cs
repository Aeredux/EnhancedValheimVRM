using System;
using System.Linq;
using HarmonyLib;

namespace EnhancedValheimVRM
{
    // The game only calls this on the client that owns the dying character.
    [HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
    internal static class PatchHumanoidOnRagdollCreated
    {
        private static void Postfix(Humanoid __instance, Ragdoll ragdoll)
        {
            if (!(__instance is Player player) || ragdoll == null) return;
            // The vanilla skinned meshes are copied onto the prefab around this call, and
            // sometimes just after it. Remember the owner so a later attempt still finds them
            // after the dead body has been pushed away from the ragdoll.
            VrmController.NoteRagdollOwner(ragdoll, player);
            VrmController.TransferToRagdoll(player.GetVrmInstance(), ragdoll);
        }
    }

    // Awake runs before Valheim copies the vanilla skinned body onto the ragdoll prefab.
    // Setup/Start run after that copy. Remote corpses often only get Awake.
    [HarmonyPatch(typeof(Ragdoll), "Awake")]
    internal static class PatchRagdollAwake
    {
        internal static void Apply(Harmony harmony)
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Ragdoll))
                         .Where(method => method.Name == "Setup" || method.Name == "Start"))
            {
                try
                {
                    harmony.Patch(original: method,
                        postfix: new HarmonyMethod(typeof(PatchRagdollAwake), nameof(Postfix)));
                }
                catch (Exception error)
                {
                    Logger.LogWarning("Could not watch Ragdoll." + method.Name + ": " + error.Message);
                }
            }
        }

        private static void Postfix(Ragdoll __instance)
        {
            VrmController.OnRagdollAppeared(__instance);
        }
    }
}

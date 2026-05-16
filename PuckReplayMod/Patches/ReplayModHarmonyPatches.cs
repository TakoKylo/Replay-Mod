using HarmonyLib;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    [HarmonyPatch(typeof(UIManager), "Awake")]
    public static class UIManagerAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIManager __instance)
        {
            ReplayModController instance = ReplayModController.Instance;
            if (instance != null)
            {
                instance.OnUiManagerAwake(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(UIMainMenu), "Initialize")]
    public static class UIMainMenuInitializePatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIMainMenu __instance, VisualElement rootVisualElement)
        {
            ReplayModController instance = ReplayModController.Instance;
            if (instance != null)
            {
                instance.OnMainMenuInitialized(__instance, rootVisualElement);
            }
        }
    }

    [HarmonyPatch(typeof(UIPauseMenu), "Initialize")]
    public static class UIPauseMenuInitializePatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIPauseMenu __instance, VisualElement rootVisualElement)
        {
            ReplayModController instance = ReplayModController.Instance;
            if (instance != null)
            {
                instance.OnPauseMenuInitialized(__instance, rootVisualElement);
            }
        }
    }

    [HarmonyPatch(typeof(UIManager), "OnPauseActionPerformed")]
    public static class UIManagerPauseActionReplayManagerClosePatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            ReplayModController instance = ReplayModController.Instance;
            if (instance == null || instance.Ui == null)
            {
                return true;
            }

            return !instance.Ui.TryCloseManager();
        }
    }

    [HarmonyPatch(typeof(UIScoreboard), "AddPlayer")]
    public static class ScoreboardAddObserverDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Player player)
        {
            if (!ReplayInputBlocker.ShouldHideObserverFromReplayScoreboard(player))
            {
                return true;
            }

            ReplayGameStatePlaybackService.RemoveLocalNonReplayPlayerFromScoreboard();
            return false;
        }
    }

    [HarmonyPatch(typeof(UIView), "Show")]
    public static class ReplaySelectionViewShowDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UIView __instance, ref bool __result)
        {
            if (!ReplayInputBlocker.IsPlaybackActive() || !ReplayInputBlocker.IsReplaySelectionView(__instance))
            {
                return true;
            }

            if (__instance != null && __instance.IsVisible)
            {
                __instance.Hide();
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "Client_RequestTeamRpc")]
    public static class PlayerRequestTeamDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, PlayerTeam team, RpcParams rpcParams)
        {
            return !ReplayInputBlocker.ShouldBlock(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Client_RequestTeamSelectRpc")]
    public static class PlayerRequestTeamSelectDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, RpcParams rpcParams)
        {
            return !ReplayInputBlocker.ShouldBlock(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Client_RequestPositionSelectRpc")]
    public static class PlayerRequestPositionSelectDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, RpcParams rpcParams)
        {
            return !ReplayInputBlocker.ShouldBlock(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Client_RequestClaimPositionRpc")]
    public static class PlayerRequestClaimPositionDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, NetworkObjectReference playerPositionReference, RpcParams rpcParams)
        {
            return !ReplayInputBlocker.ShouldBlock(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Client_RequestHandednessRpc")]
    public static class PlayerRequestHandednessDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, PlayerHandedness handedness, RpcParams rpcParams)
        {
            return !ReplayInputBlocker.ShouldBlock(__instance);
        }
    }

    [HarmonyPatch(typeof(PuckManager), "Server_SpawnPucksForPhase")]
    public static class PuckManagerSpawnPucksForPhaseDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GamePhase phase)
        {
            if (!ReplayInputBlocker.IsPlaybackActive())
            {
                return true;
            }

            ReplayModLog.Info("Blocked normal phase puck spawning during replay playback: " + phase);
            return false;
        }
    }

    [HarmonyPatch(typeof(PuckManager), "Server_SpawnPuck")]
    public static class PuckManagerSpawnPuckDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Vector3 position, Quaternion rotation, bool isReplay, ref Puck __result)
        {
            if (!ReplayInputBlocker.IsPlaybackActive() || isReplay)
            {
                if (ReplayInputBlocker.IsPlaybackActive() && isReplay)
                {
                    ReplayModLog.Info("Allowing replay puck spawn during replay playback.");
                }

                return true;
            }

            __result = null;
            ReplayModLog.Info("Blocked normal puck spawn during replay playback.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Stick), "FixedUpdate")]
    public static class StickFixedUpdateDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Stick __instance)
        {
            return !ReplayInputBlocker.IsReplayStick(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Server_SpawnSpectatorCamera")]
    public static class PlayerSpawnSpectatorCameraDuringReplayPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Player __instance, ref Vector3 position, ref Quaternion rotation)
        {
            if (!ReplayInputBlocker.ShouldUseReplaySpectatorCamera(__instance))
            {
                return;
            }

            position = NativeReplayPlaybackService.ReplaySpectatorSpawnPosition;
            rotation = NativeReplayPlaybackService.ReplaySpectatorSpawnRotation;
        }
    }

    [HarmonyPatch(typeof(ReplayPlayer), "Update")]
    public static class ReplayPlayerControlledPlaybackUpdatePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ReplayPlayer __instance)
        {
            return !ReplayPlaybackRuntime.TryUpdateControlledReplay(__instance);
        }
    }

    [HarmonyPatch(typeof(SpectatorCamera), "OnTick")]
    public static class SpectatorCameraReplayUiInputPatch
    {
        private static readonly FieldInfo MovementSpeedField = AccessTools.Field(typeof(SpectatorCamera), "movementSpeed");
        private static readonly FieldInfo PositionSmoothTimeField = AccessTools.Field(typeof(SpectatorCamera), "positionSmoothTime");
        private static readonly FieldInfo LookSmoothingField = AccessTools.Field(typeof(SpectatorCamera), "lookSmoothing");
        private static readonly FieldInfo PitchMinField = AccessTools.Field(typeof(SpectatorCamera), "pitchMin");
        private static readonly FieldInfo PitchMaxField = AccessTools.Field(typeof(SpectatorCamera), "pitchMax");
        private static readonly FieldInfo PositionField = AccessTools.Field(typeof(SpectatorCamera), "position");
        private static readonly FieldInfo PositionVelocityField = AccessTools.Field(typeof(SpectatorCamera), "positionVelocity");
        private static readonly FieldInfo PitchField = AccessTools.Field(typeof(SpectatorCamera), "pitch");
        private static readonly FieldInfo YawField = AccessTools.Field(typeof(SpectatorCamera), "yaw");
        private static readonly FieldInfo TargetPitchField = AccessTools.Field(typeof(SpectatorCamera), "targetPitch");
        private static readonly FieldInfo TargetYawField = AccessTools.Field(typeof(SpectatorCamera), "targetYaw");

        [HarmonyPrefix]
        public static bool Prefix(SpectatorCamera __instance, float deltaTime)
        {
            if (!ReplayInputBlocker.ShouldUsePlaybackUiMouseMode() || __instance == null)
            {
                return true;
            }

            if (!__instance.IsOwner)
            {
                return false;
            }

            if (!HasRequiredFields())
            {
                return true;
            }

            float rightInput = (float)((InputManager.TurnRightAction.IsPressed() ? 1 : 0) + (InputManager.TurnLeftAction.IsPressed() ? -1 : 0));
            float verticalInput = (float)(InputManager.JumpAction.IsPressed() ? 1 : (InputManager.SlideAction.IsPressed() ? -1 : 0));
            float forwardInput = (float)((InputManager.MoveForwardAction.IsPressed() ? 1 : 0) + (InputManager.MoveBackwardAction.IsPressed() ? -1 : 0));
            bool isSprinting = InputManager.SprintAction.IsPressed();

            float movementSpeed = GetFloat(__instance, MovementSpeedField);
            float positionSmoothTime = GetFloat(__instance, PositionSmoothTimeField);
            float lookSmoothing = GetFloat(__instance, LookSmoothingField);
            float pitchMin = GetFloat(__instance, PitchMinField);
            float pitchMax = GetFloat(__instance, PitchMaxField);
            Vector3 position = GetVector3(__instance, PositionField);
            Vector3 positionVelocity = GetVector3(__instance, PositionVelocityField);
            float pitch = GetFloat(__instance, PitchField);
            float yaw = GetFloat(__instance, YawField);
            float targetPitch = GetFloat(__instance, TargetPitchField);
            float targetYaw = GetFloat(__instance, TargetYawField);

            float speed = isSprinting ? movementSpeed * 2f : movementSpeed;
            position += __instance.transform.right * rightInput * speed * deltaTime;
            position += __instance.transform.up * verticalInput * speed * deltaTime;
            position += __instance.transform.forward * forwardInput * speed * deltaTime;
            __instance.transform.position = Vector3.SmoothDamp(__instance.transform.position, position, ref positionVelocity, positionSmoothTime, float.PositiveInfinity, deltaTime);

            targetPitch = Mathf.Clamp(targetPitch, pitchMin, pitchMax);
            pitch = Mathf.Lerp(pitch, targetPitch, lookSmoothing * deltaTime);
            yaw = Mathf.Lerp(yaw, targetYaw, lookSmoothing * deltaTime);
            __instance.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            PositionField.SetValue(__instance, position);
            PositionVelocityField.SetValue(__instance, positionVelocity);
            PitchField.SetValue(__instance, pitch);
            YawField.SetValue(__instance, yaw);
            TargetPitchField.SetValue(__instance, targetPitch);
            TargetYawField.SetValue(__instance, targetYaw);
            return false;
        }

        private static bool HasRequiredFields()
        {
            return MovementSpeedField != null &&
                PositionSmoothTimeField != null &&
                LookSmoothingField != null &&
                PitchMinField != null &&
                PitchMaxField != null &&
                PositionField != null &&
                PositionVelocityField != null &&
                PitchField != null &&
                YawField != null &&
                TargetPitchField != null &&
                TargetYawField != null;
        }

        private static float GetFloat(SpectatorCamera camera, FieldInfo field)
        {
            object value = field.GetValue(camera);
            return value is float ? (float)value : 0f;
        }

        private static Vector3 GetVector3(SpectatorCamera camera, FieldInfo field)
        {
            object value = field.GetValue(camera);
            return value is Vector3 ? (Vector3)value : Vector3.zero;
        }
    }

    internal static class ReplayInputBlocker
    {
        public static bool ShouldBlock(Player player)
        {
            return IsPlaybackActive() &&
                player != null &&
                player.IsLocalPlayer;
        }

        public static bool ShouldUseReplaySpectatorCamera(Player player)
        {
            return IsPlaybackActive() &&
                player != null &&
                player.IsLocalPlayer;
        }

        public static bool ShouldHideObserverFromReplayScoreboard(Player player)
        {
            if (!IsPlaybackActive() || player == null || !player.IsLocalPlayer)
            {
                return false;
            }

            return player.IsReplay == null || !player.IsReplay.Value;
        }

        public static bool IsPlaybackActive()
        {
            ReplayModController instance = ReplayModController.Instance;
            return instance != null &&
                instance.Playback != null &&
                instance.Playback.IsPlaybackActive;
        }

        public static bool ShouldUsePlaybackUiMouseMode()
        {
            ReplayModController instance = ReplayModController.Instance;
            return instance != null &&
                instance.Playback != null &&
                instance.Playback.IsPlaybackActive &&
                instance.Ui != null &&
                instance.Ui.IsPlaybackUiInputActive;
        }

        public static bool IsReplayStick(Stick stick)
        {
            if (stick == null)
            {
                return false;
            }

            Player player = stick.Player;
            return player != null &&
                player.IsReplay != null &&
                player.IsReplay.Value;
        }

        public static bool IsReplaySelectionView(UIView view)
        {
            return view is UITeamSelect || view is UIPositionSelect;
        }
    }
}

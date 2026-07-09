using System;
using HarmonyLib;
using DG.Tweening;
using System.Collections.Generic;
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

    [HarmonyPatch(typeof(UIChat), "StartInput")]
    public static class ChatInputStartDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !ReplayInputBlocker.ShouldBlockLiveChatDuringPlayback();
        }
    }

    [HarmonyPatch(typeof(UIManager), "OnAllChatActionPerformed")]
    public static class AllChatActionDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !ReplayInputBlocker.ShouldBlockLiveChatDuringPlayback();
        }
    }

    [HarmonyPatch(typeof(UIManager), "OnTeamChatActionPerformed")]
    public static class TeamChatActionDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !ReplayInputBlocker.ShouldBlockLiveChatDuringPlayback();
        }
    }

    [HarmonyPatch(typeof(UIManagerController), "Event_OnChatMessageAdded")]
    public static class ChatNotificationSoundDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !ReplayInputBlocker.IsPlaybackActive();
        }
    }

    [HarmonyPatch(typeof(ChatManager), "Client_SendChatMessage")]
    public static class ChatSendDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !ReplayInputBlocker.ShouldBlockLiveChatDuringPlayback();
        }
    }

    [HarmonyPatch(typeof(ChatManager), "Client_QuickChatAction")]
    public static class QuickChatActionDuringReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ChatManager __instance)
        {
            if (!ReplayInputBlocker.ShouldBlockLiveChatDuringPlayback())
            {
                return true;
            }

            if (__instance != null)
            {
                __instance.SetQuickChatEnabled(false, null);
            }

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

    [HarmonyPatch(typeof(ReplayManager), "Server_StartReplaying")]
    public static class ReplayManagerStartGameReplayDuringPlaybackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!ReplayInputBlocker.ShouldBlockNativeGameReplayDuringPlayback())
            {
                return true;
            }

            ReplayModLog.Info("Blocked native goal replay start during Replay Mod playback.");
            return false;
        }
    }

    [HarmonyPatch(typeof(ReplayManager), "Server_StopReplaying")]
    public static class ReplayManagerStopGameReplayDuringPlaybackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!ReplayInputBlocker.ShouldBlockNativeGameReplayDuringPlayback())
            {
                return true;
            }

            ReplayModLog.Info("Blocked native goal replay stop during Replay Mod playback.");
            return false;
        }
    }

    [HarmonyPatch(typeof(EventManager), "TriggerEvent")]
    public static class EventManagerGameStateDuringReplayPlaybackPatch
    {
        private static readonly FieldInfo EventsField = AccessTools.Field(typeof(EventManager), "events");

        [HarmonyPrefix]
        public static bool Prefix(string eventName, ref Dictionary<string, object> message)
        {
            if (!ReplayGameStatePlaybackService.IsApplyingReplayGameState || eventName != "Event_Everyone_OnGameStateChanged")
            {
                return true;
            }

            Dictionary<string, List<Action<Dictionary<string, object>>>> events =
                EventsField != null ? EventsField.GetValue(null) as Dictionary<string, List<Action<Dictionary<string, object>>>> : null;
            if (events == null || !events.ContainsKey(eventName))
            {
                return false;
            }

            if (message == null)
            {
                message = new Dictionary<string, object>
                {
                    { "eventName", eventName }
                };
            }
            else if (!message.ContainsKey("eventName"))
            {
                message.Add("eventName", eventName);
            }

            List<Action<Dictionary<string, object>>> listeners = events[eventName];
            if (listeners == null)
            {
                return false;
            }

            foreach (Action<Dictionary<string, object>> listener in new List<Action<Dictionary<string, object>>>(listeners))
            {
                if (listener == null || IsGameModeStateListener(listener))
                {
                    continue;
                }

                try
                {
                    listener(message);
                }
                catch (Exception exception)
                {
                    ReplayModLog.Warning("Replay game-state UI listener failed: " + exception.Message);
                }
            }

            return false;
        }

        private static bool IsGameModeStateListener(Delegate listener)
        {
            if (listener == null || listener.Method == null || listener.Method.Name != "Event_Everyone_OnGameStateChanged")
            {
                return false;
            }

            object target = listener.Target;
            Type type = target != null ? target.GetType() : listener.Method.DeclaringType;
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseGameMode<>))
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Stick), "FixedUpdate")]
    public static class StickFixedUpdateDuringReplayPatch
    {
        private static readonly FieldInfo BladeAngleStepField = AccessTools.Field(typeof(Stick), "bladeAngleStep");
        private static readonly FieldInfo RotationContainerField = AccessTools.Field(typeof(Stick), "rotationContainer");

        [HarmonyPrefix]
        public static bool Prefix(Stick __instance)
        {
            if (!ReplayInputBlocker.IsReplayStick(__instance))
            {
                return true;
            }

            ApplyReplayStickBladeTilt(__instance);
            return false;
        }

        private static void ApplyReplayStickBladeTilt(Stick stick)
        {
            if (stick == null || BladeAngleStepField == null || RotationContainerField == null)
            {
                return;
            }

            Player player = stick.Player;
            PlayerInput input = player != null ? player.PlayerInput : null;
            GameObject rotationContainer = RotationContainerField.GetValue(stick) as GameObject;
            if (input == null || rotationContainer == null)
            {
                return;
            }

            object bladeAngleStepValue = BladeAngleStepField.GetValue(stick);
            float bladeAngleStep = bladeAngleStepValue is float ? (float)bladeAngleStepValue : 12.5f;
            float angle = (float)input.BladeAngleInput.ServerValue * bladeAngleStep;
            rotationContainer.transform.localRotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    [HarmonyPatch(typeof(PlayerBody), "FixedUpdate")]
    public static class PlayerBodyFixedUpdateDuringPausedReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerBody __instance)
        {
            return !ReplayInputBlocker.IsPausedReplayPlayerBodyDuringPlayback(__instance);
        }
    }

    [HarmonyPatch(typeof(Puck), "FixedUpdate")]
    public static class PuckFixedUpdateDuringPausedReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Puck __instance)
        {
            return !ReplayInputBlocker.IsPausedReplayPuckDuringPlayback(__instance);
        }
    }

    [HarmonyPatch(typeof(Puck), "OnCollisionExit")]
    public static class PuckCollisionExitDuringPausedReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Puck __instance)
        {
            return !ReplayInputBlocker.IsPausedReplayPuckDuringPlayback(__instance);
        }
    }

    [HarmonyPatch(typeof(StickPositioner), "FixedUpdate")]
    public static class StickPositionerFixedUpdateDuringPausedReplayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(StickPositioner __instance)
        {
            return !ReplayInputBlocker.IsPausedReplayStickPositionerDuringPlayback(__instance);
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

    [HarmonyPatch(typeof(ReplayPlayer), "Server_ReplayEvent")]
    public static class ReplayPlayerInputReplayEventPatch
    {
        private static readonly FieldInfo ReplayPuckNetworkObjectIdMapField = AccessTools.Field(typeof(ReplayPlayer), "replayPuckNetworkObjectIdMap");

        [HarmonyPrefix]
        public static bool Prefix(ReplayPlayer __instance, string eventName, object eventData)
        {
            if (ReplayPlaybackRuntime.ShouldApplyEventsInstantly(__instance) && TryApplyMoveEventInstantly(__instance, eventName, eventData))
            {
                return false;
            }

            if (eventName != "PlayerInput" || !(eventData is ReplayPlayerInput))
            {
                return true;
            }

            ReplayPlayerInput replayPlayerInput = (ReplayPlayerInput)eventData;
            Player replayPlayer = MonoBehaviourSingleton<PlayerManager>.Instance != null
                ? MonoBehaviourSingleton<PlayerManager>.Instance.GetReplayPlayerByClientId(replayPlayerInput.OwnerClientId)
                : null;
            if (replayPlayer != null && replayPlayer.PlayerInput != null)
            {
                NativeReplayPlaybackService.ApplyPlayerInput(replayPlayer.PlayerInput, replayPlayerInput);
            }

            return false;
        }

        private static bool TryApplyMoveEventInstantly(ReplayPlayer replayPlayer, string eventName, object eventData)
        {
            if (eventName == "PlayerBodyMove" && eventData is ReplayPlayerBodyMove)
            {
                ReplayPlayerBodyMove move = (ReplayPlayerBodyMove)eventData;
                Player player = MonoBehaviourSingleton<PlayerManager>.Instance != null
                    ? MonoBehaviourSingleton<PlayerManager>.Instance.GetReplayPlayerByClientId(move.OwnerClientId)
                    : null;
                if (player == null || player.PlayerBody == null)
                {
                    return true;
                }

                player.PlayerBody.transform.DOKill(false);
                NativeReplayPlaybackService.ApplyTransformAndRigidbody(player.PlayerBody.transform, player.PlayerBody.Rigidbody, move.Position, move.Rotation);
                player.PlayerBody.Stamina.Value = move.Stamina;
                player.PlayerBody.Speed.Value = move.Speed;
                player.PlayerBody.IsSprinting.Value = move.IsSprinting;
                player.PlayerBody.IsSliding.Value = move.IsSliding;
                player.PlayerBody.IsStopping.Value = move.IsStopping;
                player.PlayerBody.IsExtendedLeft.Value = move.IsExtendedLeft;
                player.PlayerBody.IsExtendedRight.Value = move.IsExtendedRight;
                return true;
            }

            if (eventName == "StickMove" && eventData is ReplayStickMove)
            {
                ReplayStickMove move = (ReplayStickMove)eventData;
                Player player = MonoBehaviourSingleton<PlayerManager>.Instance != null
                    ? MonoBehaviourSingleton<PlayerManager>.Instance.GetReplayPlayerByClientId(move.OwnerClientId)
                    : null;
                if (player == null || player.Stick == null)
                {
                    return true;
                }

                player.Stick.transform.DOKill(false);
                NativeReplayPlaybackService.ApplyTransformAndRigidbody(player.Stick.transform, player.Stick.Rigidbody, move.Position, move.Rotation);
                return true;
            }

            if (eventName == "PuckMove" && eventData is ReplayPuckMove)
            {
                ReplayPuckMove move = (ReplayPuckMove)eventData;
                Puck puck = GetReplayPuck(replayPlayer, move.NetworkObjectId);
                if (puck == null)
                {
                    return true;
                }

                puck.transform.DOKill(false);
                NativeReplayPlaybackService.ApplyTransformAndRigidbody(puck.transform, puck.Rigidbody, move.Position, move.Rotation);
                return true;
            }

            return false;
        }

        private static Puck GetReplayPuck(ReplayPlayer replayPlayer, ulong originalNetworkObjectId)
        {
            if (replayPlayer == null || ReplayPuckNetworkObjectIdMapField == null || MonoBehaviourSingleton<PuckManager>.Instance == null)
            {
                return null;
            }

            Dictionary<ulong, ulong> puckIdMap = ReplayPuckNetworkObjectIdMapField.GetValue(replayPlayer) as Dictionary<ulong, ulong>;
            if (puckIdMap == null)
            {
                return null;
            }

            ulong replayNetworkObjectId;
            return puckIdMap.TryGetValue(originalNetworkObjectId, out replayNetworkObjectId)
                ? MonoBehaviourSingleton<PuckManager>.Instance.GetReplayPuckByNetworkObjectId(replayNetworkObjectId)
                : null;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), "Update")]
    public static class ReplayPlayerInputUpdateDuringPlaybackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInput __instance)
        {
            return !ReplayInputBlocker.IsReplayPlayerInputDuringPlayback(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerInput), "UpdateLookAngle")]
    public static class ReplayPlayerLookInputDuringPlaybackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInput __instance)
        {
            return !ReplayInputBlocker.IsReplayPlayerInputDuringPlayback(__instance);
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
            if (ReplayInputBlocker.TryApplyReplayPovCamera(__instance, deltaTime))
            {
                return false;
            }

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

        public static bool ShouldBlockLiveChatDuringPlayback()
        {
            return IsPlaybackActive();
        }

        public static bool ShouldBlockNativeGameReplayDuringPlayback()
        {
            return IsPlaybackActive();
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

        public static bool TryApplyReplayPovCamera(SpectatorCamera spectatorCamera, float deltaTime)
        {
            ReplayModController instance = ReplayModController.Instance;
            return instance != null &&
                instance.Playback != null &&
                instance.Playback.TryApplyPovCamera(spectatorCamera, deltaTime);
        }

        public static bool IsReplayStick(Stick stick)
        {
            if (!IsPlaybackActive() || stick == null)
            {
                return false;
            }

            Player player = stick.Player;
            return player != null &&
                player.IsReplay != null &&
                player.IsReplay.Value;
        }

        public static bool IsReplayPlayerInputDuringPlayback(PlayerInput playerInput)
        {
            if (!IsPlaybackActive() || playerInput == null)
            {
                return false;
            }

            Player player = playerInput.Player;
            return player != null &&
                player.IsReplay != null &&
                player.IsReplay.Value;
        }

        public static bool IsPausedReplayPlayerBodyDuringPlayback(PlayerBody playerBody)
        {
            if (!IsPlaybackActive() || !ReplayPlaybackRuntime.IsPaused || playerBody == null)
            {
                return false;
            }

            Player player = playerBody.Player;
            return player != null &&
                player.IsReplay != null &&
                player.IsReplay.Value;
        }

        public static bool IsPausedReplayPuckDuringPlayback(Puck puck)
        {
            return IsPlaybackActive() &&
                ReplayPlaybackRuntime.IsPaused &&
                puck != null &&
                puck.IsReplay != null &&
                puck.IsReplay.Value;
        }

        public static bool IsPausedReplayStickPositionerDuringPlayback(StickPositioner stickPositioner)
        {
            if (!IsPlaybackActive() || !ReplayPlaybackRuntime.IsPaused || stickPositioner == null)
            {
                return false;
            }

            Player player = stickPositioner.Player;
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

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PuckReplayMod
{
    public class NativeReplayPlaybackService
    {
        internal static readonly Vector3 ReplaySpectatorSpawnPosition = new Vector3(0f, 6f, 0f);
        internal static readonly Quaternion ReplaySpectatorSpawnRotation = Quaternion.Euler(22f, 0f, 0f);

        private readonly NativeReplayEventConverter converter = new NativeReplayEventConverter();
        private readonly ReplayGameStatePlaybackService gameStatePlayback;

        private ReplayPlayer replayPlayer;
        private ReplaySessionData currentSession;
        private string currentFilePath;
        private SortedList<int, List<ValueTuple<string, object>>> currentEventMap;
        private int currentTickRate = 30;
        private bool replaySpectatorCameraAdjusted;

        public NativeReplayPlaybackService(ReplayModSettings settings)
        {
            this.gameStatePlayback = new ReplayGameStatePlaybackService(settings);
        }

        public bool IsPlaying { get; private set; }

        public string CurrentFilePath
        {
            get { return this.currentFilePath; }
        }

        public int CurrentTick
        {
            get
            {
                return this.replayPlayer != null ? this.replayPlayer.Tick : 0;
            }
        }

        public bool IsPaused
        {
            get { return this.IsPlaying && ReplayPlaybackRuntime.IsPaused; }
        }

        public float PlaybackSpeed
        {
            get { return ReplayPlaybackRuntime.PlaybackSpeed; }
        }

        public bool CanPlay
        {
            get
            {
                return NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsHost &&
                    NetworkManager.Singleton.ConnectedClientsList != null &&
                    NetworkManager.Singleton.ConnectedClientsList.Count <= 1 &&
                    MonoBehaviourSingleton<ReplayManager>.Instance != null &&
                    MonoBehaviourSingleton<ReplayManager>.Instance.ReplayPlayer != null;
            }
        }

        public bool CanStartLocalReplaySession
        {
            get
            {
                return NetworkManager.Singleton != null &&
                    !NetworkManager.Singleton.IsClient &&
                    !NetworkManager.Singleton.IsServer &&
                    NetworkBehaviourSingleton<ServerManager>.Instance != null;
            }
        }

        public bool TryStartLocalReplaySession()
        {
            if (!this.CanStartLocalReplaySession)
            {
                return false;
            }

            this.replaySpectatorCameraAdjusted = false;
            ReplayModLog.Info("Starting local practice session for native replay playback.");
            EventManager.TriggerEvent("Event_OnPlayClickPractice", null);
            return true;
        }

        public string GetUnavailableReason()
        {
            if (NetworkManager.Singleton == null)
            {
                return "network manager is unavailable";
            }

            if (!NetworkManager.Singleton.IsHost)
            {
                return "not in a local host/practice session";
            }

            if (NetworkManager.Singleton.ConnectedClientsList != null && NetworkManager.Singleton.ConnectedClientsList.Count > 1)
            {
                return "local host has connected clients";
            }

            if (MonoBehaviourSingleton<ReplayManager>.Instance == null || MonoBehaviourSingleton<ReplayManager>.Instance.ReplayPlayer == null)
            {
                return "native ReplayPlayer is unavailable";
            }

            return "unknown";
        }

        public bool TryPlay(ReplaySessionData session, string filePath)
        {
            if (!this.CanPlay)
            {
                return false;
            }

            ReplayManager replayManager = MonoBehaviourSingleton<ReplayManager>.Instance;
            this.replayPlayer = replayManager.ReplayPlayer;
            SortedList<int, List<ValueTuple<string, object>>> eventMap = this.converter.Convert(session);
            if (eventMap.Count == 0)
            {
                throw new InvalidOperationException("Replay has no native events to play.");
            }

            this.currentSession = session;
            this.currentEventMap = eventMap;
            this.currentTickRate = session != null && session.Header != null ? Math.Max(1, session.Header.TickRate) : 30;

            if (this.replayPlayer.IsReplaying)
            {
                this.replayPlayer.Server_StopReplay();
            }

            this.replaySpectatorCameraAdjusted = false;
            this.PrepareSceneForReplay();
            this.replayPlayer.Server_StartReplay(this.CloneEventMap(), this.currentTickRate, 0);
            this.currentFilePath = filePath;
            this.IsPlaying = this.replayPlayer.IsReplaying;
            if (this.IsPlaying)
            {
                ReplayPlaybackRuntime.Attach(this.replayPlayer);
                this.gameStatePlayback.Start(session);
                this.gameStatePlayback.ApplyThrough(this.CurrentTick);
            }

            return this.IsPlaying;
        }

        public void Tick()
        {
            if (!this.IsPlaying)
            {
                return;
            }

            if (this.replayPlayer == null || !this.replayPlayer.IsReplaying)
            {
                this.IsPlaying = false;
                this.gameStatePlayback.Close();
                return;
            }

            this.gameStatePlayback.ApplyThrough(this.CurrentTick);
        }

        public void SetPaused(bool paused)
        {
            if (!this.IsPlaying)
            {
                return;
            }

            ReplayPlaybackRuntime.SetPaused(paused);
        }

        public void SetPlaybackSpeed(float speed)
        {
            ReplayPlaybackRuntime.SetPlaybackSpeed(speed);
        }

        public bool SeekToTick(int targetTick)
        {
            if (!this.IsPlaying || this.replayPlayer == null || this.currentEventMap == null || this.currentEventMap.Count == 0)
            {
                return false;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                return false;
            }

            bool wasPaused = ReplayPlaybackRuntime.IsPaused;
            float speed = ReplayPlaybackRuntime.PlaybackSpeed;
            int minTick = this.currentEventMap.Keys[0];
            int maxTick = this.currentEventMap.Keys[this.currentEventMap.Keys.Count - 1];
            targetTick = Mathf.Clamp(targetTick, minTick, maxTick);

            if (this.replayPlayer.IsReplaying)
            {
                this.replayPlayer.Server_StopReplay();
            }

            this.PrepareSceneForReplay();
            this.replayPlayer.Server_StartReplay(this.CloneEventMap(), this.currentTickRate, targetTick);
            this.IsPlaying = this.replayPlayer.IsReplaying;
            if (!this.IsPlaying)
            {
                return false;
            }

            ReplayPlaybackRuntime.Attach(this.replayPlayer);
            ReplayPlaybackRuntime.SetPlaybackSpeed(speed);
            ReplayPlaybackRuntime.SetPaused(wasPaused);
            this.gameStatePlayback.ApplyThrough(this.CurrentTick);
            return true;
        }

        public void Close()
        {
            this.gameStatePlayback.Close();
            if (this.replayPlayer != null && this.replayPlayer.IsReplaying && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                this.replayPlayer.Server_StopReplay();
            }

            ReplayPlaybackRuntime.Detach(this.replayPlayer);
            this.IsPlaying = false;
            this.currentSession = null;
            this.currentFilePath = null;
            this.currentEventMap = null;
            this.currentTickRate = 30;
            this.replayPlayer = null;
            this.replaySpectatorCameraAdjusted = false;
        }

        public void EnforceSpectatorMode()
        {
            try
            {
                HideReplaySelectionViews();

                PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
                Player localPlayer = playerManager != null ? playerManager.GetLocalPlayer() : null;
                if (localPlayer == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                {
                    return;
                }

                if (localPlayer.IsCharacterSpawned)
                {
                    localPlayer.Server_DespawnCharacter();
                }

                localPlayer.Server_SetGameState(PlayerPhase.Spectate, PlayerTeam.Spectator, PlayerRole.None, null);
                this.EnsureReplaySpectatorCameraRaised(localPlayer);
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to enforce replay spectator mode: " + exception.Message);
            }
        }

        private void EnsureReplaySpectatorCameraRaised(Player localPlayer)
        {
            if (this.replaySpectatorCameraAdjusted || localPlayer == null || !localPlayer.IsSpectatorCameraSpawned || localPlayer.SpectatorCamera == null)
            {
                return;
            }

            if (localPlayer.SpectatorCamera.transform.position.y < 2f)
            {
                localPlayer.Server_DespawnSpectatorCamera();
                localPlayer.Server_SpawnSpectatorCamera(ReplaySpectatorSpawnPosition, ReplaySpectatorSpawnRotation);
            }

            this.replaySpectatorCameraAdjusted = true;
        }

        private static void HideReplaySelectionViews()
        {
            UIManager uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null)
            {
                return;
            }

            if (uiManager.TeamSelect != null && uiManager.TeamSelect.IsVisible)
            {
                uiManager.TeamSelect.Hide();
            }

            if (uiManager.PositionSelect != null && uiManager.PositionSelect.IsVisible)
            {
                uiManager.PositionSelect.Hide();
            }
        }

        private void PrepareSceneForReplay()
        {
            try
            {
                this.EnforceSpectatorMode();

                if (MonoBehaviourSingleton<PuckManager>.Instance != null)
                {
                    MonoBehaviourSingleton<PuckManager>.Instance.Server_DespawnPucks(false);
                }

                if (NetworkBehaviourSingleton<GameManager>.Instance != null)
                {
                    NetworkBehaviourSingleton<GameManager>.Instance.Server_StopTicking();
                }

                if (MonoBehaviourSingleton<ReplayManager>.Instance != null)
                {
                    MonoBehaviourSingleton<ReplayManager>.Instance.Server_StopRecording();
                }
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Native replay scene preparation failed: " + exception.Message);
            }
        }

        private SortedList<int, List<ValueTuple<string, object>>> CloneEventMap()
        {
            SortedList<int, List<ValueTuple<string, object>>> clone = new SortedList<int, List<ValueTuple<string, object>>>();
            if (this.currentEventMap == null)
            {
                return clone;
            }

            foreach (KeyValuePair<int, List<ValueTuple<string, object>>> entry in this.currentEventMap)
            {
                clone.Add(entry.Key, new List<ValueTuple<string, object>>(entry.Value));
            }

            return clone;
        }
    }
}

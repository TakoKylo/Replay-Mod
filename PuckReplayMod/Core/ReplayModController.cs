using System;
using UnityEngine;

namespace PuckReplayMod
{
    public class ReplayModController : MonoBehaviour
    {
        public static ReplayModController Instance { get; private set; }

        public ReplayModSettings Settings { get; private set; }

        public ReplayStorageService Storage { get; private set; }

        public ReplayFileReader Reader { get; private set; }

        public ClientReplayRecorder Recorder { get; private set; }

        public ReplayPlaybackService Playback { get; private set; }

        public ReplayModUiService Ui { get; private set; }

        public bool IsDedicatedServer { get; private set; }

        private bool recorderUpdateErrorLogged;
        private bool uiUpdateErrorLogged;
        private bool wasRecording;
        private float frameProfileEndRealtime;
        private float nextFrameProfileRealtime;
        private float frameProfileTotalMilliseconds;
        private float frameProfileMaxMilliseconds;
        private int frameProfileSamples;
        private int frameProfileHitches;

        public static void Create()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject gameObject = new GameObject("PuckReplayMod");
            Instance = gameObject.AddComponent<ReplayModController>();
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        public static void DestroyInstance()
        {
            if (Instance == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(Instance.gameObject);
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                UnityEngine.Object.Destroy(base.gameObject);
                return;
            }

            Instance = this;
            this.IsDedicatedServer = ApplicationManager.IsDedicatedGameServer;
            this.Settings = ReplayModSettings.Load();
            this.Storage = new ReplayStorageService();
            this.Storage.Initialize(this.IsDedicatedServer);
            this.Reader = new ReplayFileReader();
            this.Recorder = new ClientReplayRecorder(this.Settings, this.Storage, this.IsDedicatedServer);
            this.Recorder.Initialize();

            if (this.IsDedicatedServer)
            {
                // No UI exists on a dedicated server, so recordings left behind by a crash or
                // hard kill have to be recovered automatically at startup; this runs before any
                // new recording can create its own temp file.
                try
                {
                    ReplayRecoveryResult recovery = this.Storage.RecoverUnfinishedRecordings();
                    if (recovery.FoundCount > 0)
                    {
                        ReplayModLog.Info("Startup replay recovery: " + recovery.RecoveredCount + " recovered, " + recovery.FailedCount + " failed.");
                    }
                }
                catch (Exception exception)
                {
                    ReplayModLog.Warning("Startup replay recovery failed: " + exception.Message);
                }

                // A headless dedicated server has no in-game UI or replay viewer; only the
                // recorder runs, capturing the server-authoritative match and saving it locally.
                bool serverRecordingEnabled = this.Settings == null || this.Settings.EnableServerSideRecording;
                if (!serverRecordingEnabled)
                {
                    this.Recorder.SetRecordingSuppressed(true, "server-side recording disabled in settings");
                }

                ReplayModLog.Info("Dedicated server detected; server-side replay recording " +
                    (serverRecordingEnabled ? "enabled." : "disabled."));
            }
            else
            {
                this.Playback = new ReplayPlaybackService(this.Settings, this.Reader, this.Recorder);
                this.Ui = new ReplayModUiService(this.Settings, this.Recorder, this.Storage, this.Reader, this.Playback);
                this.Ui.Initialize();
                EventManager.AddEventListener("Event_OnClientStarted", this.Event_OnClientStarted);
                EventManager.AddEventListener("Event_OnClientStopped", this.Event_OnClientStopped);
            }
        }

        private void Update()
        {
            if (this.Recorder != null)
            {
                try
                {
                    this.Recorder.Tick(Time.unscaledDeltaTime);
                }
                catch (Exception exception)
                {
                    if (!this.recorderUpdateErrorLogged)
                    {
                        this.recorderUpdateErrorLogged = true;
                        ReplayModLog.Error("Recorder update failed: " + exception);
                    }
                }
            }

            if (this.Ui != null)
            {
                try
                {
                    this.Ui.TryAttachToExistingUi();
                    this.Ui.Tick();
                }
                catch (Exception exception)
                {
                    if (!this.uiUpdateErrorLogged)
                    {
                        this.uiUpdateErrorLogged = true;
                        ReplayModLog.Error("UI update failed: " + exception);
                    }
                }
            }

            if (this.Playback != null)
            {
                try
                {
                    this.Playback.Tick(Time.unscaledDeltaTime);
                }
                catch (Exception exception)
                {
                    ReplayModLog.Error("Playback update failed: " + exception);
                    this.Playback.Close();
                }
            }

            if (this.Settings != null && this.Settings.EnableDebugProfiling)
            {
                this.TrackJoinFrameProfile();
            }
        }

        private void TrackJoinFrameProfile()
        {
            bool isRecording = this.Recorder != null && this.Recorder.IsRecording;
            float realtime = Time.realtimeSinceStartup;
            if (isRecording && !this.wasRecording)
            {
                this.frameProfileEndRealtime = realtime + 14f;
                this.nextFrameProfileRealtime = realtime + 2f;
                this.frameProfileTotalMilliseconds = 0f;
                this.frameProfileMaxMilliseconds = 0f;
                this.frameProfileSamples = 0;
                this.frameProfileHitches = 0;
            }

            this.wasRecording = isRecording;
            if (!isRecording || realtime > this.frameProfileEndRealtime)
            {
                return;
            }

            float frameMilliseconds = Time.unscaledDeltaTime * 1000f;
            this.frameProfileTotalMilliseconds += frameMilliseconds;
            if (frameMilliseconds > this.frameProfileMaxMilliseconds)
            {
                this.frameProfileMaxMilliseconds = frameMilliseconds;
            }

            if (frameMilliseconds >= 50f)
            {
                this.frameProfileHitches++;
            }

            this.frameProfileSamples++;
            if (realtime < this.nextFrameProfileRealtime || this.frameProfileSamples <= 0)
            {
                return;
            }

            float averageMilliseconds = this.frameProfileTotalMilliseconds / this.frameProfileSamples;
            ReplayModLog.Info(
                "Frame profile: avg " + averageMilliseconds.ToString("0.00") +
                "ms, max " + this.frameProfileMaxMilliseconds.ToString("0.00") +
                "ms, hitches " + this.frameProfileHitches +
                " over " + this.frameProfileSamples + " frames.");

            this.frameProfileTotalMilliseconds = 0f;
            this.frameProfileMaxMilliseconds = 0f;
            this.frameProfileSamples = 0;
            this.frameProfileHitches = 0;
            this.nextFrameProfileRealtime += 2f;
        }

        private void OnDestroy()
        {
            if (!this.IsDedicatedServer)
            {
                EventManager.RemoveEventListener("Event_OnClientStarted", this.Event_OnClientStarted);
                EventManager.RemoveEventListener("Event_OnClientStopped", this.Event_OnClientStopped);
            }

            if (this.Ui != null)
            {
                this.Ui.Dispose();
                this.Ui = null;
            }

            if (this.Recorder != null)
            {
                this.Recorder.Dispose();
                this.Recorder = null;
            }

            if (this.Playback != null)
            {
                this.Playback.Close();
                this.Playback = null;
            }

            this.Reader = null;

            if (this.Settings != null)
            {
                this.Settings.Save();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void OnUiManagerAwake(UIManager uiManager)
        {
            if (this.Ui != null)
            {
                this.Ui.AttachRoot(uiManager);
            }
        }

        public void OnMainMenuInitialized(UIMainMenu mainMenu, UnityEngine.UIElements.VisualElement root)
        {
            if (this.Ui != null)
            {
                this.Ui.AttachMainMenuButton(root);
            }
        }

        public void OnPauseMenuInitialized(UIPauseMenu pauseMenu, UnityEngine.UIElements.VisualElement root)
        {
            if (this.Ui != null)
            {
                this.Ui.AttachPauseMenuButton(root);
            }
        }

        private void Event_OnClientStopped(System.Collections.Generic.Dictionary<string, object> message)
        {
            if (this.Playback != null && this.Playback.IsPlaybackActive)
            {
                ReplayModLog.Info("Client stopped during replay playback; closing playback.");
                this.Playback.Close();
            }

            ReplayGameStatePlaybackService.RemoveAllReplayPlayersFromScoreboard();
            ReplayGameStatePlaybackService.RemoveAllReplayObjectsFromMinimap();
            ReplayGameStatePlaybackService.RemoveAllReplayObjectsFromPlayerUsernames();
        }

        private void Event_OnClientStarted(System.Collections.Generic.Dictionary<string, object> message)
        {
            ReplayGameStatePlaybackService.RemoveAllReplayPlayersFromScoreboard();
            ReplayGameStatePlaybackService.RemoveAllReplayObjectsFromMinimap();
            ReplayGameStatePlaybackService.RemoveAllReplayObjectsFromPlayerUsernames();
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PuckReplayMod
{
    public class ReplayPlaybackService
    {
        private readonly ReplayModSettings settings;
        private readonly ReplayFileReader reader;
        private readonly ClientReplayRecorder recorder;
        private readonly NativeReplayPlaybackService nativePlayback;

        private ReplaySessionData session;
        private string currentFilePath;
        private bool isPreparingNativePlayback;
        private bool startedLocalSessionForPlayback;
        private bool localSessionHasStarted;
        private float nativePrepareStartRealtime;
        private float nextSpectatorEnforceRealtime;

        public ReplayPlaybackService(ReplayModSettings settings, ReplayFileReader reader, ClientReplayRecorder recorder)
        {
            this.settings = settings;
            this.reader = reader;
            this.recorder = recorder;
            this.nativePlayback = new NativeReplayPlaybackService(settings);
        }

        public bool IsPlaying { get; private set; }

        public bool IsPlaybackActive
        {
            get { return this.IsPlaying || this.isPreparingNativePlayback; }
        }

        public int CurrentTick
        {
            get { return this.nativePlayback.CurrentTick; }
        }

        public bool IsPaused
        {
            get { return this.nativePlayback.IsPaused; }
        }

        public float PlaybackSpeed
        {
            get { return this.nativePlayback.PlaybackSpeed; }
        }

        public string PlaybackMode
        {
            get
            {
                if (this.isPreparingNativePlayback)
                {
                    return "preparing-native";
                }

                return this.IsPlaying ? "native" : "idle";
            }
        }

        public int TotalTicks
        {
            get
            {
                return this.session != null && this.session.Header != null ? this.session.Header.TotalTicks : 0;
            }
        }

        public int TickRate
        {
            get
            {
                return this.session != null && this.session.Header != null ? Math.Max(1, this.session.Header.TickRate) : 30;
            }
        }

        public string CurrentFilePath
        {
            get { return this.currentFilePath; }
        }

        public void Play(string filePath)
        {
            this.Close();
            this.session = this.reader.Load(filePath);
            this.currentFilePath = filePath;
            this.recorder.SetRecordingSuppressed(true, "replay playback");

            try
            {
                if (this.IsNativePlaybackEnvironmentReady() && this.StartNativePlayback())
                {
                    return;
                }

                this.isPreparingNativePlayback = true;
                this.IsPlaying = true;
                this.nativePrepareStartRealtime = Time.realtimeSinceStartup;

                if (this.nativePlayback.TryStartLocalReplaySession())
                {
                    this.startedLocalSessionForPlayback = true;
                    ReplayModLog.Info("Preparing local native replay session for: " + filePath);
                    return;
                }

                this.isPreparingNativePlayback = false;
                this.IsPlaying = false;
                throw new InvalidOperationException("Native replay playback is unavailable: " + this.nativePlayback.GetUnavailableReason());
            }
            catch
            {
                this.recorder.SetRecordingSuppressed(false, "replay playback failed");
                throw;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!this.IsPlaybackActive || this.session == null)
            {
                return;
            }

            this.TrackLocalPlaybackSessionState();

            if (this.startedLocalSessionForPlayback && this.localSessionHasStarted && this.IsLocalPlaybackSessionStopped())
            {
                ReplayModLog.Info("Local replay playback session stopped; closing Replay Mod playback.");
                this.Close();
                return;
            }

            this.EnforceSpectatorMode();

            if (this.isPreparingNativePlayback)
            {
                if (this.IsNativePlaybackEnvironmentReady() && this.StartNativePlayback())
                {
                    this.isPreparingNativePlayback = false;
                    ReplayModLog.Info("Local native replay session is ready; playback started.");
                    return;
                }

                if (Time.realtimeSinceStartup - this.nativePrepareStartRealtime > 30f)
                {
                    ReplayModLog.Warning("Local native replay session did not become ready in time: " + this.nativePlayback.GetUnavailableReason());
                    this.Close();
                }

                return;
            }

            this.nativePlayback.Tick();
            if (!this.nativePlayback.IsPlaying)
            {
                ReplayModLog.Info("Native replay playback reached the end.");
                this.Close();
            }
        }

        public void TogglePause()
        {
            if (!this.IsPlaying || this.isPreparingNativePlayback)
            {
                return;
            }

            this.nativePlayback.SetPaused(!this.nativePlayback.IsPaused);
        }

        public void SetPlaybackSpeed(float speed)
        {
            this.nativePlayback.SetPlaybackSpeed(speed);
        }

        public void SeekToTick(int tick)
        {
            if (!this.IsPlaying || this.isPreparingNativePlayback)
            {
                return;
            }

            this.nativePlayback.SeekToTick(Mathf.Clamp(tick, 0, this.TotalTicks));
        }

        public void Close()
        {
            this.IsPlaying = false;
            this.isPreparingNativePlayback = false;
            this.nativePlayback.Close();
            if (this.startedLocalSessionForPlayback)
            {
                this.StopLocalPlaybackSession();
            }

            this.recorder.SetRecordingSuppressed(false, "replay playback closed");
            this.startedLocalSessionForPlayback = false;
            this.localSessionHasStarted = false;
            this.nativePrepareStartRealtime = 0f;
            this.nextSpectatorEnforceRealtime = 0f;
            this.currentFilePath = null;
            this.session = null;
        }

        private bool StartNativePlayback()
        {
            if (!this.nativePlayback.TryPlay(this.session, this.currentFilePath))
            {
                return false;
            }

            this.IsPlaying = true;
            this.EnforceSpectatorMode();
            ReplayModLog.Info("Playing replay with native Puck replay actors: " + this.currentFilePath);
            return true;
        }

        private bool IsNativePlaybackEnvironmentReady()
        {
            if (!this.nativePlayback.CanPlay)
            {
                return false;
            }

            Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name != "level_default")
            {
                return false;
            }

            if (global::SceneManager.IsSceneLoadInProgress || !global::SceneManager.IsInitialSceneLoaded)
            {
                return false;
            }

            return true;
        }

        private void EnforceSpectatorMode()
        {
            float realtime = Time.realtimeSinceStartup;
            if (realtime < this.nextSpectatorEnforceRealtime)
            {
                return;
            }

            this.nextSpectatorEnforceRealtime = realtime + 0.75f;
            this.nativePlayback.EnforceSpectatorMode();
        }

        private void StopLocalPlaybackSession()
        {
            try
            {
                if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsClient)
                {
                    Unity.Netcode.NetworkManager.Singleton.Shutdown(true);
                }
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to stop local replay session: " + exception.Message);
            }
        }

        private void TrackLocalPlaybackSessionState()
        {
            if (!this.startedLocalSessionForPlayback || this.localSessionHasStarted)
            {
                return;
            }

            Unity.Netcode.NetworkManager networkManager = Unity.Netcode.NetworkManager.Singleton;
            if (networkManager != null && (networkManager.IsClient || networkManager.IsServer || networkManager.IsListening))
            {
                this.localSessionHasStarted = true;
                ReplayModLog.Info("Local replay playback session started.");
            }
        }

        private bool IsLocalPlaybackSessionStopped()
        {
            Unity.Netcode.NetworkManager networkManager = Unity.Netcode.NetworkManager.Singleton;
            if (networkManager == null)
            {
                return true;
            }

            return !networkManager.IsClient && !networkManager.IsServer && !networkManager.IsListening;
        }
    }
}

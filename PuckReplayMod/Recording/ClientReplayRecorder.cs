using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PuckReplayMod
{
    public class ClientReplayRecorder
    {
        private const int KeyframeIntervalSeconds = 30;
        private readonly ReplayModSettings settings;
        private readonly ReplayStorageService storage;
        private readonly bool isDedicatedServer;
        private readonly List<Player> activeBodyPlayers = new List<Player>(16);
        private readonly List<Puck> activePucks = new List<Puck>(16);
        private readonly List<Task<ReplaySaveResult>> pendingSaveTasks = new List<Task<ReplaySaveResult>>();

        private ReplaySessionData currentSession;
        private ReplayChunkedRecordingWriter currentWriter;
        private float tickAccumulator;
        private float lastRealtime;
        private int currentTick;
        private int currentEventCount;
        private int nextKeyframeTick;
        private bool initialized;
        private bool isRecordingSuppressed;
        private bool autoRecordingPausedByManualStop;
        private string lastObservedPhase;
        private bool startRequested;
        private bool scoreboardSnapshotRequested;
        private bool firstTickLogged;
        private bool stalledTickWarningLogged;
        private bool transformCaptureFailureLogged;
        private bool scoreboardCaptureFailureLogged;
        private bool markerInputFailureLogged;
        private bool streamingWriteFailureLogged;
        private bool currentRecordingIsManual;
        private bool currentRecordingHasSeenGame;
        private bool currentRecordingSaveConfirmed;
        private float recordingStartedRealtime;
        private float nextCaptureProfileRealtime;
        private float captureProfileEndRealtime;
        private long captureProfileTotalTicks;
        private long captureProfileMaxTicks;
        private int captureProfileFrames;
        private string startReason;

        public ClientReplayRecorder(ReplayModSettings settings, ReplayStorageService storage, bool isDedicatedServer)
        {
            this.settings = settings;
            this.storage = storage;
            this.isDedicatedServer = isDedicatedServer;
        }

        public event Action RecordingStateChanged;

        public event Action TickAdvanced;

        public bool IsRecording
        {
            get { return this.currentSession != null; }
        }

        public int CurrentTick
        {
            get { return this.currentTick; }
        }

        public bool IsRecordingSuppressed
        {
            get { return this.isRecordingSuppressed; }
        }

        public bool IsCurrentRecordingSaveConfirmed
        {
            get { return this.currentRecordingSaveConfirmed; }
        }

        public void Initialize()
        {
            if (this.initialized)
            {
                return;
            }

            this.initialized = true;
            this.Subscribe();
        }

        public void Dispose()
        {
            this.Unsubscribe();
            this.StopRecording(true, "mod disabled");
            this.WaitForPendingSaves("mod shutdown");
            this.initialized = false;
        }

        public void Tick(float deltaTime)
        {
            this.PollPendingSaves();
            this.PollAutomaticRecordingThreshold();

            if (this.startRequested && !this.IsRecording)
            {
                string reason = string.IsNullOrEmpty(this.startReason) ? "gameplay observed" : this.startReason;
                this.startRequested = false;
                this.startReason = null;
                this.StartRecording(reason);
            }

            if (this.IsKeyPressed(this.settings.ManualRecordingKey))
            {
                this.ToggleManualRecording();
            }

            if (this.IsRecording && this.IsKeyPressed(this.settings.MarkerKey))
            {
                this.AddMarker();
            }

            if (!this.IsRecording)
            {
                this.lastRealtime = Time.realtimeSinceStartup;
                return;
            }

            float realtime = Time.realtimeSinceStartup;
            if (this.lastRealtime <= 0f)
            {
                this.lastRealtime = realtime;
            }

            deltaTime = Mathf.Max(0f, realtime - this.lastRealtime);
            this.lastRealtime = realtime;
            float tickInterval = 1f / Mathf.Max(1, this.settings.CaptureTickRate);
            this.tickAccumulator += deltaTime;
            while (this.tickAccumulator >= tickInterval)
            {
                this.tickAccumulator -= tickInterval;
                try
                {
                    long captureStartTicks = this.settings.EnableDebugProfiling ? Stopwatch.GetTimestamp() : 0L;
                    this.RecordTransformFrame();
                    this.RecordKeyframeIfDue();
                    if (this.settings.EnableDebugProfiling)
                    {
                        this.TrackCaptureProfile(Stopwatch.GetTimestamp() - captureStartTicks, realtime);
                    }
                }
                catch (Exception exception)
                {
                    if (!this.transformCaptureFailureLogged)
                    {
                        this.transformCaptureFailureLogged = true;
                        ReplayModLog.Warning("Transform frame capture failed: " + exception);
                    }
                }

                if (this.scoreboardSnapshotRequested)
                {
                    try
                    {
                        this.RecordScoreboardSnapshot();
                    }
                    catch (Exception exception)
                    {
                        if (!this.scoreboardCaptureFailureLogged)
                        {
                            this.scoreboardCaptureFailureLogged = true;
                            ReplayModLog.Warning("Scoreboard snapshot capture failed: " + exception);
                        }
                    }

                    this.scoreboardSnapshotRequested = false;
                }

                this.currentTick++;
                if (!this.firstTickLogged)
                {
                    this.firstTickLogged = true;
                    ReplayModLog.Info("Recording tick stream started.");
                }

                Action tickAdvanced = this.TickAdvanced;
                if (tickAdvanced != null)
                {
                    tickAdvanced();
                }
            }

            if (this.currentTick == 0 && !this.stalledTickWarningLogged && realtime - this.recordingStartedRealtime > 2f)
            {
                this.stalledTickWarningLogged = true;
                ReplayModLog.Warning("Recording is active, but no replay ticks have advanced after two seconds.");
            }
        }

        public void StartRecording(string reason)
        {
            this.StartRecording(reason, false);
        }

        public void StartManualRecording()
        {
            if (!this.IsInGame())
            {
                ReplayModLog.Info("Manual recording was ignored because no game session is active.");
                return;
            }

            this.autoRecordingPausedByManualStop = false;
            this.startRequested = false;
            this.startReason = null;
            this.StartRecording("manual hotkey", true);
        }

        public void StopManualRecording()
        {
            if (!this.IsRecording)
            {
                return;
            }

            this.autoRecordingPausedByManualStop = true;
            this.startRequested = false;
            this.startReason = null;
            this.currentRecordingSaveConfirmed = true;
            this.StopRecording(true, "manual hotkey");
        }

        private void StartRecording(string reason, bool ignoreAutoRecord)
        {
            if (this.IsRecording || (!ignoreAutoRecord && !IsAutomaticRecordingEnabled(this.settings)) || this.isRecordingSuppressed)
            {
                return;
            }

            if (!ignoreAutoRecord && !this.HasEnoughPlayersToAutoRecord())
            {
                return;
            }

            if (!ignoreAutoRecord && !this.AllowAutoRecordStartNow())
            {
                return;
            }

            this.currentTick = 0;
            this.tickAccumulator = 0f;
            this.lastRealtime = Time.realtimeSinceStartup;
            this.recordingStartedRealtime = this.lastRealtime;
            this.firstTickLogged = false;
            this.stalledTickWarningLogged = false;
            this.transformCaptureFailureLogged = false;
            this.scoreboardCaptureFailureLogged = false;
            this.streamingWriteFailureLogged = false;
            this.currentRecordingIsManual = ignoreAutoRecord;
            this.currentRecordingHasSeenGame = false;
            this.currentRecordingSaveConfirmed = this.settings.SaveOnDisconnect;
            if (this.settings.EnableDebugProfiling)
            {
                this.ResetCaptureProfile(this.recordingStartedRealtime);
            }
            this.currentEventCount = 0;
            this.nextKeyframeTick = 0;
            this.currentSession = new ReplaySessionData();
            this.currentSession.Header.StartedUtcTicks = DateTime.UtcNow.Ticks;
            this.currentSession.Header.TickRate = Mathf.Max(1, this.settings.CaptureTickRate);
            this.currentSession.Header.ServerName = this.GetServerName();
            this.currentSession.Header.RecordedBy = this.GetRecorderName();
            try
            {
                this.currentWriter = this.storage.CreateChunkedRecordingWriter(this.currentSession.Header);
            }
            catch (Exception exception)
            {
                this.currentSession = null;
                ReplayModLog.Warning("Failed to start chunked replay writer: " + exception.Message);
                return;
            }

            this.RebuildActiveObjects();

            InitialSnapshotPayload initialSnapshot = this.BuildInitialSnapshot();
            this.TrackGameSeen(initialSnapshot.GameState);
            this.RecordEvent("InitialSnapshot", initialSnapshot);
            this.RecordKeyframe(initialSnapshot);
            this.nextKeyframeTick = Math.Max(1, this.currentSession.Header.TickRate * KeyframeIntervalSeconds);
            ReplayModLog.Info("Recording started (" + reason + ").");
            Action recordingStateChanged = this.RecordingStateChanged;
            if (recordingStateChanged != null)
            {
                recordingStateChanged();
            }
        }

        public void StopRecording(bool save, string reason)
        {
            if (!this.IsRecording)
            {
                this.startRequested = false;
                this.startReason = null;
                return;
            }

            ReplaySessionData session = this.currentSession;
            ReplayChunkedRecordingWriter writer = this.currentWriter;
            bool saveConfirmed = this.currentRecordingSaveConfirmed;
            int eventCount = this.currentEventCount;
            this.currentSession = null;
            this.currentWriter = null;
            this.currentEventCount = 0;
            this.nextKeyframeTick = 0;
            this.currentRecordingIsManual = false;
            this.currentRecordingHasSeenGame = false;
            this.currentRecordingSaveConfirmed = false;
            this.tickAccumulator = 0f;
            this.lastRealtime = 0f;
            session.Header.EndedUtcTicks = DateTime.UtcNow.Ticks;
            session.Header.TotalTicks = this.currentTick;
            session.Header.EventCount = eventCount;

            if (save && eventCount > 0)
            {
                float durationSeconds = session.Header.TickRate > 0 ? session.Header.TotalTicks / (float)session.Header.TickRate : 0f;
                if (!this.settings.SaveOnDisconnect && !saveConfirmed)
                {
                    ReplayModLog.Info("Discarded replay because Save on disconnect is off and the recording was not marked to save (" + durationSeconds.ToString("0.0") + "s).");
                }
                else if (this.settings.MinimumReplayLengthSeconds > 0 && durationSeconds < this.settings.MinimumReplayLengthSeconds)
                {
                    ReplayModLog.Info("Discarded short replay (" + durationSeconds.ToString("0.0") + "s, minimum " + this.settings.MinimumReplayLengthSeconds + "s).");
                }
                else
                {
                    if (writer != null)
                    {
                        this.pendingSaveTasks.Add(this.storage.FinalizeChunkedReplayAsync(writer, session.Header, this.settings.MinimumReplayLengthSeconds, this.settings.StorageLimitMb));
                        writer = null;
                    }
                    else
                    {
                        this.pendingSaveTasks.Add(this.storage.SaveReplayAsync(session, this.settings.MinimumReplayLengthSeconds, this.settings.StorageLimitMb));
                    }

                    ReplayModLog.Info(
                        "Queued chunked replay finalization in background (" +
                        durationSeconds.ToString("0.0") + "s, " +
                        eventCount + " events).");
                }
            }

            if (writer != null)
            {
                writer.Abort();
            }

            this.ClearActiveObjects();
            ReplayModLog.Info("Recording stopped (" + reason + ").");
            Action recordingStateChanged = this.RecordingStateChanged;
            if (recordingStateChanged != null)
            {
                recordingStateChanged();
            }
        }

        public void AddMarker()
        {
            if (!this.IsRecording)
            {
                return;
            }

            this.RecordEvent("Marker", new MarkerPayload
            {
                CreatedUtcTicks = DateTime.UtcNow.Ticks
            });
            this.currentSession.Header.HasMarkers = true;
            ReplayModLog.Info("Marker added at tick " + this.currentTick + ".");
        }

        public void SetRecordingSuppressed(bool isSuppressed, string reason)
        {
            if (this.isRecordingSuppressed == isSuppressed)
            {
                return;
            }

            this.isRecordingSuppressed = isSuppressed;
            this.startRequested = false;
            this.startReason = null;
            if (isSuppressed && this.IsRecording)
            {
                this.StopRecording(false, reason);
            }

            ReplayModLog.Info("Recording " + (isSuppressed ? "suppressed" : "unsuppressed") + " (" + reason + ").");
            Action recordingStateChanged = this.RecordingStateChanged;
            if (recordingStateChanged != null)
            {
                recordingStateChanged();
            }
        }

        private void PollPendingSaves()
        {
            for (int i = this.pendingSaveTasks.Count - 1; i >= 0; i--)
            {
                Task<ReplaySaveResult> task = this.pendingSaveTasks[i];
                if (!task.IsCompleted)
                {
                    continue;
                }

                this.pendingSaveTasks.RemoveAt(i);
                if (task.IsFaulted)
                {
                    ReplayModLog.Warning("Background replay save failed: " + task.Exception.GetBaseException().Message);
                    continue;
                }

                ReplaySaveResult result = task.Result;
                if (result == null || !result.Success)
                {
                    ReplayModLog.Warning("Background replay save failed: " + (result != null ? result.ErrorMessage : "unknown error"));
                    continue;
                }

                ReplayModLog.Info(
                    "Saved replay in background: " + result.FilePath +
                    " (" + result.SizeBytes + " bytes, " +
                    result.ElapsedMilliseconds + "ms).");
            }
        }

        private void WaitForPendingSaves(string reason)
        {
            if (this.pendingSaveTasks.Count == 0)
            {
                return;
            }

            // Saves run on background tasks; on shutdown the process would otherwise exit before
            // the last match finished writing, stranding it as an unfinished temp recording.
            ReplayModLog.Info("Waiting for " + this.pendingSaveTasks.Count + " replay save(s) to finish (" + reason + ").");
            try
            {
                if (!Task.WaitAll(this.pendingSaveTasks.ToArray(), 15000))
                {
                    ReplayModLog.Warning("Timed out waiting for replay saves; an unfinished recording may remain in the temp folder for recovery.");
                }
            }
            catch (AggregateException)
            {
                // Faulted saves are logged individually by PollPendingSaves below.
            }

            this.PollPendingSaves();
        }

        private void Subscribe()
        {
            EventManager.AddEventListener("Event_OnClientStopped", this.Event_OnClientStopped);
            if (this.isDedicatedServer)
            {
                EventManager.AddEventListener("Event_Server_OnServerStopped", this.Event_Server_OnServerStopped);
            }
            EventManager.AddEventListener("Event_Everyone_OnGameStateChanged", this.Event_Everyone_OnGameStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerSpawned", this.Event_Everyone_OnPlayerSpawned);
            EventManager.AddEventListener("Event_Everyone_OnPlayerDespawned", this.Event_Everyone_OnPlayerDespawned);
            EventManager.AddEventListener("Event_Everyone_OnPlayerGameStateChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerCustomizationStateChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerHandednessChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerUsernameChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerNumberChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerGoalsChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerAssistsChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnGoalScored", this.Event_Everyone_OnGoalScored);
            EventManager.AddEventListener("Event_Everyone_OnPlayerPositionChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerPatreonLevelChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerAdminLevelChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerSteamIdChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerIsMutedChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.AddEventListener("Event_Everyone_OnPlayerBodySpawned", this.Event_Everyone_OnPlayerBodySpawned);
            EventManager.AddEventListener("Event_Everyone_OnPlayerBodyDespawned", this.Event_Everyone_OnPlayerBodyDespawned);
            EventManager.AddEventListener("Event_Everyone_OnStickSpawned", this.Event_Everyone_OnStickSpawned);
            EventManager.AddEventListener("Event_Everyone_OnStickDespawned", this.Event_Everyone_OnStickDespawned);
            EventManager.AddEventListener("Event_Everyone_OnPuckSpawned", this.Event_Everyone_OnPuckSpawned);
            EventManager.AddEventListener("Event_Everyone_OnPuckDespawned", this.Event_Everyone_OnPuckDespawned);
            EventManager.AddEventListener("Event_OnChatMessageAdded", this.Event_OnChatMessageAdded);
        }

        private void Unsubscribe()
        {
            EventManager.RemoveEventListener("Event_OnClientStopped", this.Event_OnClientStopped);
            if (this.isDedicatedServer)
            {
                EventManager.RemoveEventListener("Event_Server_OnServerStopped", this.Event_Server_OnServerStopped);
            }
            EventManager.RemoveEventListener("Event_Everyone_OnGameStateChanged", this.Event_Everyone_OnGameStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerSpawned", this.Event_Everyone_OnPlayerSpawned);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerDespawned", this.Event_Everyone_OnPlayerDespawned);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerGameStateChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerCustomizationStateChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerHandednessChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerUsernameChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerNumberChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerGoalsChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerAssistsChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnGoalScored", this.Event_Everyone_OnGoalScored);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerPositionChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerPatreonLevelChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerAdminLevelChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerSteamIdChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerIsMutedChanged", this.Event_Everyone_OnPlayerStateChanged);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerBodySpawned", this.Event_Everyone_OnPlayerBodySpawned);
            EventManager.RemoveEventListener("Event_Everyone_OnPlayerBodyDespawned", this.Event_Everyone_OnPlayerBodyDespawned);
            EventManager.RemoveEventListener("Event_Everyone_OnStickSpawned", this.Event_Everyone_OnStickSpawned);
            EventManager.RemoveEventListener("Event_Everyone_OnStickDespawned", this.Event_Everyone_OnStickDespawned);
            EventManager.RemoveEventListener("Event_Everyone_OnPuckSpawned", this.Event_Everyone_OnPuckSpawned);
            EventManager.RemoveEventListener("Event_Everyone_OnPuckDespawned", this.Event_Everyone_OnPuckDespawned);
            EventManager.RemoveEventListener("Event_OnChatMessageAdded", this.Event_OnChatMessageAdded);
        }

        private void Event_OnClientStopped(Dictionary<string, object> message)
        {
            this.autoRecordingPausedByManualStop = false;
            this.StopRecording(true, "client stopped");
            this.ClearActiveObjects();
        }

        private void Event_Server_OnServerStopped(Dictionary<string, object> message)
        {
            // On a dedicated server there is no client lifecycle, so the server stopping is
            // the signal to flush and save the match currently being recorded.
            this.autoRecordingPausedByManualStop = false;
            this.StopRecording(true, "server stopped");
            this.WaitForPendingSaves("server stopped");
            this.ClearActiveObjects();
        }

        private void Event_Everyone_OnGameStateChanged(Dictionary<string, object> message)
        {
            GameStatePayload gameState = this.BuildGameState();
            string previousPhase = this.lastObservedPhase;
            this.lastObservedPhase = gameState != null ? gameState.Phase : null;
            this.ResumeAutoRecordingOnNewGame(previousPhase, gameState);
            if (!this.EnsureRecording("game state observed"))
            {
                return;
            }

            this.RecordEvent("GameState", gameState);
            this.HandleSplitRecordingGameState(gameState);
        }

        private void ResumeAutoRecordingOnNewGame(string previousPhase, GameStatePayload gameState)
        {
            if (!this.autoRecordingPausedByManualStop || gameState == null)
            {
                return;
            }

            // PreGame is only ever entered when a new match starts, so a manual stop pauses
            // automatic recording for the remainder of the current game rather than silently
            // disabling it for the rest of the session.
            if (gameState.Phase == "PreGame" && previousPhase != "PreGame")
            {
                this.autoRecordingPausedByManualStop = false;
                ReplayModLog.Info("Automatic recording resumed for the next game after a manual stop.");
            }
        }

        private void Event_Everyone_OnPlayerSpawned(Dictionary<string, object> message)
        {
            Player player = message["player"] as Player;
            if (!this.EnsureRecording("player observed"))
            {
                return;
            }

            if (this.ShouldSkipPlayer(player))
            {
                return;
            }

            this.RecordEvent("PlayerSpawned", new PlayerLifecyclePayload
            {
                Player = this.BuildPlayerSnapshot(player)
            });
            this.RequestScoreboardSnapshot();
        }

        private void Event_Everyone_OnPlayerDespawned(Dictionary<string, object> message)
        {
            Player player = message["player"] as Player;
            this.RemoveActivePlayer(player);
            if (!this.ShouldSkipPlayer(player))
            {
                this.RecordEvent("PlayerDespawned", new PlayerLifecyclePayload
                {
                    Player = this.BuildPlayerSnapshot(player)
                });
                this.RequestScoreboardSnapshot();
            }

            this.StopRecordingIfNoPlayersRemain(player);
        }

        private void Event_Everyone_OnPlayerStateChanged(Dictionary<string, object> message)
        {
            Player player = message["player"] as Player;
            if (this.ShouldSkipPlayer(player))
            {
                return;
            }

            if (!this.EnsureRecording("player state observed"))
            {
                return;
            }

            this.RecordEvent("PlayerState", this.BuildPlayerSnapshot(player));
            this.RequestScoreboardSnapshot();
        }

        private void Event_Everyone_OnGoalScored(Dictionary<string, object> message)
        {
            if (!this.EnsureRecording("goal observed"))
            {
                return;
            }

            this.RecordEvent("GoalScored", this.BuildGoalScoredPayload(message));
            this.RequestScoreboardSnapshot();
        }

        private void Event_Everyone_OnPlayerBodySpawned(Dictionary<string, object> message)
        {
            PlayerBody playerBody = message["playerBody"] as PlayerBody;
            if (!this.ShouldSkipPlayerBody(playerBody))
            {
                this.AddActivePlayer(playerBody.Player);
            }

            if (!this.EnsureRecording("player body observed"))
            {
                return;
            }

            if (this.ShouldSkipPlayerBody(playerBody))
            {
                return;
            }

            this.RecordEvent("PlayerBodySpawned", new BodyLifecyclePayload
            {
                OwnerClientId = playerBody.OwnerClientId,
                Position = Vector3Dto.From(playerBody.transform.position),
                Rotation = QuaternionDto.From(playerBody.transform.rotation),
                Player = this.BuildPlayerSnapshot(playerBody.Player)
            });
        }

        private void Event_Everyone_OnPlayerBodyDespawned(Dictionary<string, object> message)
        {
            PlayerBody playerBody = message["playerBody"] as PlayerBody;
            if (playerBody != null)
            {
                this.RemoveActivePlayer(playerBody.Player);
            }

            if (this.ShouldSkipPlayerBody(playerBody))
            {
                return;
            }

            this.RecordEvent("PlayerBodyDespawned", new BodyLifecyclePayload
            {
                OwnerClientId = playerBody.OwnerClientId,
                Position = Vector3Dto.From(playerBody.transform.position),
                Rotation = QuaternionDto.From(playerBody.transform.rotation),
                Player = this.BuildPlayerSnapshot(playerBody.Player)
            });
        }

        private void Event_Everyone_OnStickSpawned(Dictionary<string, object> message)
        {
            Stick stick = message["stick"] as Stick;
            if (stick == null || this.ShouldSkipPlayer(stick.Player))
            {
                return;
            }

            if (!this.EnsureRecording("stick observed"))
            {
                return;
            }

            if (stick.Player == null || this.ShouldSkipPlayer(stick.Player))
            {
                return;
            }

            this.RecordEvent("StickSpawned", new StickSnapshotPayload
            {
                OwnerClientId = stick.OwnerClientId,
                Position = Vector3Dto.From(stick.transform.position),
                Rotation = QuaternionDto.From(stick.transform.rotation)
            });
        }

        private void Event_Everyone_OnStickDespawned(Dictionary<string, object> message)
        {
            Stick stick = message["stick"] as Stick;
            if (stick == null || this.ShouldSkipPlayer(stick.Player))
            {
                return;
            }

            this.RecordEvent("StickDespawned", new StickSnapshotPayload
            {
                OwnerClientId = stick.OwnerClientId,
                Position = Vector3Dto.From(stick.transform.position),
                Rotation = QuaternionDto.From(stick.transform.rotation)
            });
        }

        private void Event_Everyone_OnPuckSpawned(Dictionary<string, object> message)
        {
            Puck puck = message["puck"] as Puck;
            if (!this.ShouldSkipPuck(puck))
            {
                this.AddActivePuck(puck);
            }

            if (!this.EnsureRecording("puck observed"))
            {
                return;
            }

            if (this.ShouldSkipPuck(puck))
            {
                return;
            }

            this.RecordEvent("PuckSpawned", new PuckLifecyclePayload
            {
                NetworkObjectId = puck.NetworkObjectId,
                Position = Vector3Dto.From(puck.transform.position),
                Rotation = QuaternionDto.From(puck.transform.rotation)
            });
        }

        private void Event_Everyone_OnPuckDespawned(Dictionary<string, object> message)
        {
            Puck puck = message["puck"] as Puck;
            this.RemoveActivePuck(puck);
            if (this.ShouldSkipPuck(puck))
            {
                return;
            }

            this.RecordEvent("PuckDespawned", new PuckLifecyclePayload
            {
                NetworkObjectId = puck.NetworkObjectId,
                Position = Vector3Dto.From(puck.transform.position),
                Rotation = QuaternionDto.From(puck.transform.rotation)
            });
        }

        private void Event_OnChatMessageAdded(Dictionary<string, object> message)
        {
            if (!this.EnsureRecording("chat observed"))
            {
                return;
            }

            ChatMessage chatMessage = message["chatMessage"] as ChatMessage;
            if (chatMessage == null)
            {
                return;
            }

            this.RecordEvent("ChatMessage", new ChatMessagePayload
            {
                SteamId = chatMessage.SteamID.HasValue ? chatMessage.SteamID.Value.ToString() : string.Empty,
                Username = chatMessage.Username.HasValue ? chatMessage.Username.Value.ToString() : string.Empty,
                Team = chatMessage.Team.HasValue ? chatMessage.Team.Value.ToString() : string.Empty,
                Message = chatMessage.Content.ToString(),
                IsQuickChat = chatMessage.IsQuickChat,
                IsTeamChat = chatMessage.IsTeamChat,
                IsSystem = chatMessage.IsSystem
            });
            this.currentSession.Header.HasChat = true;
        }

        private bool EnsureRecording(string reason)
        {
            if (this.isRecordingSuppressed)
            {
                return false;
            }

            if (this.autoRecordingPausedByManualStop)
            {
                return false;
            }

            if (this.IsRecording)
            {
                return true;
            }

            if (!IsAutomaticRecordingEnabled(this.settings))
            {
                return false;
            }

            if (!this.HasEnoughPlayersToAutoRecord())
            {
                return false;
            }

            if (!this.AllowAutoRecordStartNow())
            {
                return false;
            }

            this.startRequested = true;
            if (string.IsNullOrEmpty(this.startReason))
            {
                this.startReason = reason;
            }

            return false;
        }

        private void HandleSplitRecordingGameState(GameStatePayload gameState)
        {
            if (this.settings == null || !this.IsRecording)
            {
                return;
            }

            // A dedicated server runs indefinitely across many matches, so it always splits at
            // game end to save one replay per match instead of a single ever-growing recording.
            bool splitByGameEnd = this.settings.SplitRecordingsByGameEnd || this.isDedicatedServer;
            if (!splitByGameEnd)
            {
                return;
            }

            this.TrackGameSeen(gameState);
            if (!this.currentRecordingHasSeenGame || !IsGameEndState(gameState))
            {
                return;
            }

            bool wasManualRecording = this.currentRecordingIsManual;
            this.currentRecordingSaveConfirmed = true;
            this.StopRecording(true, "game ended split");
            if (wasManualRecording)
            {
                this.autoRecordingPausedByManualStop = true;
                ReplayModLog.Info("Manual recording stopped at game end.");
                return;
            }

            if (IsAutomaticRecordingEnabled(this.settings) && !this.isRecordingSuppressed && !this.autoRecordingPausedByManualStop && this.HasEnoughPlayersToAutoRecord())
            {
                this.startRequested = true;
                this.startReason = "new game split";
                ReplayModLog.Info("Queued next automatic recording after game-end split.");
            }
        }

        private void TrackGameSeen(GameStatePayload gameState)
        {
            if (IsInGameState(gameState))
            {
                this.currentRecordingHasSeenGame = true;
            }
        }

        private static bool IsInGameState(GameStatePayload gameState)
        {
            if (gameState == null || gameState.Period <= 0 || string.IsNullOrEmpty(gameState.Phase))
            {
                return false;
            }

            return gameState.Phase != "None" &&
                gameState.Phase != "Warmup" &&
                gameState.Phase != "PreGame" &&
                gameState.Phase != "GameOver" &&
                gameState.Phase != "PostGame";
        }

        private static bool IsGameEndState(GameStatePayload gameState)
        {
            return gameState != null &&
                (gameState.Phase == "GameOver" ||
                    gameState.Phase == "PostGame" ||
                    gameState.Phase == "Warmup" ||
                    gameState.Phase == "PreGame");   // votestart (/vs) restart: Play->PreGame, no GameOver/Warmup
        }

        private void PollAutomaticRecordingThreshold()
        {
            if (this.IsRecording || this.startRequested || this.isRecordingSuppressed || this.autoRecordingPausedByManualStop)
            {
                return;
            }

            if (this.settings == null || !IsAutomaticRecordingEnabled(this.settings) || !this.IsInGame() || !this.HasEnoughPlayersToAutoRecord() || !this.AllowAutoRecordStartNow())
            {
                return;
            }

            this.startRequested = true;
            this.startReason = this.settings.RecordOnlyDuringGames ? "game in progress" : "minimum player threshold reached";
        }

        private void RecordTransformFrame()
        {
            TransformFramePayload frame = new TransformFramePayload(this.activeBodyPlayers.Count, this.activeBodyPlayers.Count, this.activePucks.Count);
            for (int i = this.activeBodyPlayers.Count - 1; i >= 0; i--)
            {
                Player player = this.activeBodyPlayers[i];
                if (this.ShouldSkipPlayer(player))
                {
                    this.activeBodyPlayers.RemoveAt(i);
                    continue;
                }

                if (!player.PlayerBody)
                {
                    continue;
                }

                PlayerBody playerBody = player.PlayerBody;
                frame.PlayerBodies.Add(new PlayerBodyTransformPayload
                {
                    OwnerClientId = player.OwnerClientId,
                    Position = Vector3Dto.From(playerBody.transform.position),
                    Rotation = QuaternionDto.From(playerBody.transform.rotation),
                    Stamina = playerBody.Stamina.Value,
                    Speed = playerBody.Speed.Value,
                    IsSprinting = playerBody.IsSprinting.Value,
                    IsSliding = playerBody.IsSliding.Value,
                    IsStopping = playerBody.IsStopping.Value,
                    IsExtendedLeft = playerBody.IsExtendedLeft.Value,
                    IsExtendedRight = playerBody.IsExtendedRight.Value
                });

                if (player.Stick)
                {
                    frame.Sticks.Add(new StickTransformPayload
                    {
                        OwnerClientId = player.OwnerClientId,
                        Position = Vector3Dto.From(player.Stick.transform.position),
                        Rotation = QuaternionDto.From(player.Stick.transform.rotation)
                    });
                }

                if (player.PlayerInput)
                {
                    frame.PlayerInputs.Add(this.BuildPlayerInputSnapshot(player));
                }
            }

            for (int i = this.activePucks.Count - 1; i >= 0; i--)
            {
                Puck puck = this.activePucks[i];
                if (this.ShouldSkipPuck(puck))
                {
                    this.activePucks.RemoveAt(i);
                    continue;
                }

                frame.Pucks.Add(new PuckTransformPayload
                {
                    NetworkObjectId = puck.NetworkObjectId,
                    Position = Vector3Dto.From(puck.transform.position),
                    Rotation = QuaternionDto.From(puck.transform.rotation)
                });
            }

            if (frame.PlayerBodies.Count > 0 || frame.Sticks.Count > 0 || frame.Pucks.Count > 0)
            {
                this.RecordEvent("TransformFrame", frame);
            }
        }

        private InitialSnapshotPayload BuildInitialSnapshot()
        {
            InitialSnapshotPayload snapshot = new InitialSnapshotPayload(this.activeBodyPlayers.Count, this.activePucks.Count, this.activeBodyPlayers.Count)
            {
                GameState = this.BuildGameState()
            };

            PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager != null)
            {
                foreach (Player player in playerManager.GetPlayers(false))
                {
                    if (this.ShouldSkipPlayer(player))
                    {
                        continue;
                    }

                    snapshot.Players.Add(this.BuildPlayerSnapshot(player));
                    if (player.PlayerBody)
                    {
                        snapshot.PlayerBodies.Add(new BodyLifecyclePayload
                        {
                            OwnerClientId = player.OwnerClientId,
                            Position = Vector3Dto.From(player.PlayerBody.transform.position),
                            Rotation = QuaternionDto.From(player.PlayerBody.transform.rotation),
                            Player = this.BuildPlayerSnapshot(player)
                        });
                    }

                    if (player.Stick)
                    {
                        snapshot.Sticks.Add(new StickSnapshotPayload
                        {
                            OwnerClientId = player.OwnerClientId,
                            Position = Vector3Dto.From(player.Stick.transform.position),
                            Rotation = QuaternionDto.From(player.Stick.transform.rotation)
                        });
                    }

                    if (player.PlayerInput)
                    {
                        snapshot.PlayerInputs.Add(this.BuildPlayerInputSnapshot(player));
                    }
                }
            }

            for (int i = 0; i < this.activePucks.Count; i++)
            {
                Puck puck = this.activePucks[i];
                if (this.ShouldSkipPuck(puck))
                {
                    continue;
                }

                snapshot.Pucks.Add(new PuckSnapshotPayload
                {
                    NetworkObjectId = puck.NetworkObjectId,
                    Position = Vector3Dto.From(puck.transform.position),
                    Rotation = QuaternionDto.From(puck.transform.rotation)
                });
            }

            return snapshot;
        }

        private PlayerInputPayload BuildPlayerInputSnapshot(Player player)
        {
            PlayerInput input = player != null ? player.PlayerInput : null;
            if (input == null)
            {
                return new PlayerInputPayload();
            }

            return new PlayerInputPayload
            {
                OwnerClientId = player.OwnerClientId,
                LookAngleInput = Vector2Dto.From(input.LookAngleInput.ServerValue),
                BladeAngleInput = input.BladeAngleInput.ServerValue,
                TrackInput = input.TrackInput.ServerValue,
                LookInput = input.LookInput.ServerValue
            };
        }

        private GoalScoredPayload BuildGoalScoredPayload(Dictionary<string, object> message)
        {
            PlayerTeam byTeam = PlayerTeam.None;
            if (message != null && message.ContainsKey("byTeam") && message["byTeam"] is PlayerTeam)
            {
                byTeam = (PlayerTeam)message["byTeam"];
            }

            Player scorer = message != null && message.ContainsKey("goalPlayer") ? message["goalPlayer"] as Player : null;
            Player assist = message != null && message.ContainsKey("assistPlayer") ? message["assistPlayer"] as Player : null;
            Player secondAssist = message != null && message.ContainsKey("secondAssistPlayer") ? message["secondAssistPlayer"] as Player : null;
            Puck puck = message != null && message.ContainsKey("puck") ? message["puck"] as Puck : null;

            int blueScore;
            int redScore;
            this.GetProjectedScoreAfterGoal(byTeam, out blueScore, out redScore);

            return new GoalScoredPayload
            {
                Team = byTeam.ToString(),
                BlueScore = blueScore,
                RedScore = redScore,
                Scorer = this.BuildOptionalPlayerSnapshot(scorer),
                Assist = this.BuildOptionalPlayerSnapshot(assist),
                SecondAssist = this.BuildOptionalPlayerSnapshot(secondAssist),
                PuckNetworkObjectId = puck != null ? puck.NetworkObjectId : 0UL,
                PuckSpeed = puck != null ? puck.Speed : 0f,
                PuckShotSpeed = puck != null ? puck.ShotSpeed : 0f
            };
        }

        private void GetProjectedScoreAfterGoal(PlayerTeam byTeam, out int blueScore, out int redScore)
        {
            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            if (gameManager == null || gameManager.GameState == null)
            {
                blueScore = 0;
                redScore = 0;
                return;
            }

            blueScore = gameManager.BlueScore;
            redScore = gameManager.RedScore;
            if (byTeam == PlayerTeam.Blue && gameManager.Phase != GamePhase.BlueScore)
            {
                blueScore++;
            }
            else if (byTeam == PlayerTeam.Red && gameManager.Phase != GamePhase.RedScore)
            {
                redScore++;
            }
        }

        private PlayerSnapshotPayload BuildOptionalPlayerSnapshot(Player player)
        {
            return this.ShouldSkipPlayer(player) ? null : this.BuildPlayerSnapshot(player);
        }

        private PlayerSnapshotPayload BuildPlayerSnapshot(Player player)
        {
            string positionName = string.Empty;
            if (player.PlayerPosition)
            {
                positionName = player.PlayerPosition.Name.ToString();
            }

            return new PlayerSnapshotPayload
            {
                OwnerClientId = player.OwnerClientId,
                SteamId = player.SteamId.Value.ToString(),
                Username = player.Username.Value.ToString(),
                Number = player.Number.Value,
                Goals = player.Goals.Value,
                Assists = player.Assists.Value,
                PatreonLevel = player.PatreonLevel.Value,
                AdminLevel = player.AdminLevel.Value,
                Phase = player.Phase.ToString(),
                Team = player.Team.ToString(),
                Role = player.Role.ToString(),
                Handedness = player.Handedness.Value.ToString(),
                PositionName = positionName,
                IsMuted = player.IsMuted.Value,
                Customization = BuildCustomizationSnapshot(player.CustomizationState.Value)
            };
        }

        private static PlayerCustomizationPayload BuildCustomizationSnapshot(PlayerCustomizationState state)
        {
            return new PlayerCustomizationPayload
            {
                FlagID = state.FlagID,
                HeadgearIDBlueAttacker = state.HeadgearIDBlueAttacker,
                HeadgearIDRedAttacker = state.HeadgearIDRedAttacker,
                HeadgearIDBlueGoalie = state.HeadgearIDBlueGoalie,
                HeadgearIDRedGoalie = state.HeadgearIDRedGoalie,
                MustacheID = state.MustacheID,
                BeardID = state.BeardID,
                JerseyIDBlueAttacker = state.JerseyIDBlueAttacker,
                JerseyIDRedAttacker = state.JerseyIDRedAttacker,
                JerseyIDBlueGoalie = state.JerseyIDBlueGoalie,
                JerseyIDRedGoalie = state.JerseyIDRedGoalie,
                StickSkinIDBlueAttacker = state.StickSkinIDBlueAttacker,
                StickSkinIDRedAttacker = state.StickSkinIDRedAttacker,
                StickSkinIDBlueGoalie = state.StickSkinIDBlueGoalie,
                StickSkinIDRedGoalie = state.StickSkinIDRedGoalie,
                StickShaftTapeIDBlueAttacker = state.StickShaftTapeIDBlueAttacker,
                StickShaftTapeIDRedAttacker = state.StickShaftTapeIDRedAttacker,
                StickShaftTapeIDBlueGoalie = state.StickShaftTapeIDBlueGoalie,
                StickShaftTapeIDRedGoalie = state.StickShaftTapeIDRedGoalie,
                StickBladeTapeIDBlueAttacker = state.StickBladeTapeIDBlueAttacker,
                StickBladeTapeIDRedAttacker = state.StickBladeTapeIDRedAttacker,
                StickBladeTapeIDBlueGoalie = state.StickBladeTapeIDBlueGoalie,
                StickBladeTapeIDRedGoalie = state.StickBladeTapeIDRedGoalie
            };
        }

        private GameStatePayload BuildGameState()
        {
            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            if (gameManager == null || gameManager.GameState == null)
            {
                return new GameStatePayload();
            }

            return new GameStatePayload
            {
                Phase = gameManager.Phase.ToString(),
                Tick = gameManager.Tick,
                Period = gameManager.Period,
                BlueScore = gameManager.BlueScore,
                RedScore = gameManager.RedScore,
                IsOvertime = gameManager.IsOvertime
            };
        }

        private void RequestScoreboardSnapshot()
        {
            this.scoreboardSnapshotRequested = true;
        }

        private void RecordScoreboardSnapshot()
        {
            if (!this.IsRecording)
            {
                return;
            }

            PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager == null)
            {
                return;
            }

            List<Player> players = playerManager.GetPlayers(false);
            ScoreboardSnapshotPayload snapshot = new ScoreboardSnapshotPayload(players.Count);
            foreach (Player player in players)
            {
                if (!this.ShouldSkipPlayer(player))
                {
                    snapshot.Players.Add(this.BuildPlayerSnapshot(player));
                }
            }

            this.RecordEvent("ScoreboardSnapshot", snapshot);
            this.currentSession.Header.HasScoreboard = true;
        }

        private void RecordEvent(string type, object payload)
        {
            if (!this.IsRecording)
            {
                return;
            }

            ReplayEventDto replayEvent = new ReplayEventDto
            {
                Tick = this.currentTick,
                Type = type,
                Payload = payload
            };

            try
            {
                if (this.currentWriter != null)
                {
                    this.currentWriter.AppendEvent(replayEvent);
                }
                else
                {
                    this.currentSession.Events.Add(replayEvent);
                }

                this.currentEventCount++;
            }
            catch (Exception exception)
            {
                if (!this.streamingWriteFailureLogged)
                {
                    this.streamingWriteFailureLogged = true;
                    ReplayModLog.Warning("Replay chunk write failed; stopping recording without saving: " + exception.Message);
                }

                this.StopRecording(false, "chunk writer failed");
            }
        }

        private void RecordKeyframeIfDue()
        {
            if (!this.IsRecording || this.currentWriter == null || this.nextKeyframeTick <= 0 || this.currentTick < this.nextKeyframeTick)
            {
                return;
            }

            this.RecordKeyframe(this.BuildInitialSnapshot());
            int intervalTicks = Math.Max(1, this.currentSession.Header.TickRate * KeyframeIntervalSeconds);
            while (this.nextKeyframeTick <= this.currentTick)
            {
                this.nextKeyframeTick += intervalTicks;
            }
        }

        private void RecordKeyframe(InitialSnapshotPayload snapshot)
        {
            if (this.currentWriter == null || snapshot == null)
            {
                return;
            }

            this.currentWriter.AddKeyframe(this.currentTick, snapshot);
        }

        private void RebuildActiveObjects()
        {
            this.ClearActiveObjects();

            PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager != null)
            {
                foreach (Player player in playerManager.GetSpawnedPlayers(false))
                {
                    this.AddActivePlayer(player);
                }
            }

            PuckManager puckManager = MonoBehaviourSingleton<PuckManager>.Instance;
            if (puckManager != null)
            {
                foreach (Puck puck in puckManager.GetPucks(false))
                {
                    this.AddActivePuck(puck);
                }
            }
        }

        private void ClearActiveObjects()
        {
            this.activeBodyPlayers.Clear();
            this.activePucks.Clear();
        }

        private void AddActivePlayer(Player player)
        {
            if (this.ShouldSkipPlayer(player))
            {
                return;
            }

            if (!this.activeBodyPlayers.Contains(player))
            {
                this.activeBodyPlayers.Add(player);
            }
        }

        private void RemoveActivePlayer(Player player)
        {
            if (player == null)
            {
                return;
            }

            this.activeBodyPlayers.Remove(player);
        }

        private void AddActivePuck(Puck puck)
        {
            if (this.ShouldSkipPuck(puck))
            {
                return;
            }

            if (!this.activePucks.Contains(puck))
            {
                this.activePucks.Add(puck);
            }
        }

        private void RemoveActivePuck(Puck puck)
        {
            if (puck == null)
            {
                return;
            }

            this.activePucks.Remove(puck);
        }

        private void ResetCaptureProfile(float realtime)
        {
            this.nextCaptureProfileRealtime = realtime + 2f;
            this.captureProfileEndRealtime = realtime + 12f;
            this.captureProfileTotalTicks = 0L;
            this.captureProfileMaxTicks = 0L;
            this.captureProfileFrames = 0;
        }

        private void TrackCaptureProfile(long elapsedTicks, float realtime)
        {
            if (realtime > this.captureProfileEndRealtime)
            {
                return;
            }

            this.captureProfileTotalTicks += elapsedTicks;
            if (elapsedTicks > this.captureProfileMaxTicks)
            {
                this.captureProfileMaxTicks = elapsedTicks;
            }

            this.captureProfileFrames++;
            if (realtime < this.nextCaptureProfileRealtime || this.captureProfileFrames <= 0)
            {
                return;
            }

            double tickToMilliseconds = 1000.0 / Stopwatch.Frequency;
            double averageMilliseconds = this.captureProfileTotalTicks * tickToMilliseconds / this.captureProfileFrames;
            double maxMilliseconds = this.captureProfileMaxTicks * tickToMilliseconds;
            ReplayModLog.Info(
                "Capture profile: avg " + averageMilliseconds.ToString("0.000") +
                "ms, max " + maxMilliseconds.ToString("0.000") +
                "ms over " + this.captureProfileFrames +
                " frames; active players " + this.activeBodyPlayers.Count +
                ", pucks " + this.activePucks.Count + ".");

            this.captureProfileTotalTicks = 0L;
            this.captureProfileMaxTicks = 0L;
            this.captureProfileFrames = 0;
            this.nextCaptureProfileRealtime += 2f;
        }

        private void ToggleManualRecording()
        {
            if (this.IsRecording)
            {
                if (!this.settings.SaveOnDisconnect && !this.currentRecordingIsManual && !this.currentRecordingSaveConfirmed)
                {
                    this.ConfirmCurrentRecordingSave("manual hotkey");
                    return;
                }

                this.StopManualRecording();
                return;
            }

            this.StartManualRecording();
        }

        private void ConfirmCurrentRecordingSave(string reason)
        {
            if (!this.IsRecording)
            {
                return;
            }

            if (this.currentRecordingSaveConfirmed)
            {
                ReplayModLog.Info("Recording save was already confirmed (" + reason + ").");
                return;
            }

            this.currentRecordingSaveConfirmed = true;
            ReplayModLog.Info("Recording save confirmed (" + reason + ").");
            Action recordingStateChanged = this.RecordingStateChanged;
            if (recordingStateChanged != null)
            {
                recordingStateChanged();
            }
        }

        private bool IsKeyPressed(KeyCode keyCode)
        {
            try
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard == null)
                {
                    return false;
                }

                Key key;
                if (!this.TryConvertKey(keyCode, out key))
                {
                    return false;
                }

                return keyboard[key] != null && keyboard[key].wasPressedThisFrame;
            }
            catch (Exception exception)
            {
                if (!this.markerInputFailureLogged)
                {
                    this.markerInputFailureLogged = true;
                    ReplayModLog.Warning("Replay hotkey polling failed: " + exception.Message);
                }

                return false;
            }
        }

        private bool TryConvertKey(KeyCode keyCode, out Key key)
        {
            string keyName = keyCode.ToString();
            if (keyName.StartsWith("Alpha", StringComparison.Ordinal))
            {
                keyName = "Digit" + keyName.Substring("Alpha".Length);
            }

            if (Enum.TryParse(keyName, true, out key))
            {
                return key != Key.None;
            }

            return false;
        }

        private bool IsInGame()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null && (networkManager.IsClient || networkManager.IsServer || networkManager.IsListening);
        }

        private void StopRecordingIfNoPlayersRemain(Player despawningPlayer)
        {
            if (!this.IsRecording)
            {
                return;
            }

            if (this.HasAnyConnectedPlayer(despawningPlayer))
            {
                return;
            }

            // The server (or host) is now empty, so close out and keep the match that was captured
            // instead of letting it run on against an empty rink.
            this.currentRecordingSaveConfirmed = true;
            this.StopRecording(true, "all players disconnected");
        }

        private bool HasAnyConnectedPlayer(Player excludedPlayer)
        {
            PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager == null)
            {
                return false;
            }

            List<Player> players = playerManager.GetPlayers(false);
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
                if (player == excludedPlayer || this.ShouldSkipPlayer(player))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool AllowAutoRecordStartNow()
        {
            if (this.settings == null || !this.settings.RecordOnlyDuringGames)
            {
                return true;
            }

            return this.IsGameInProgress();
        }

        private bool IsGameInProgress()
        {
            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            if (gameManager == null || gameManager.GameState == null || gameManager.Period <= 0)
            {
                return false;
            }

            GamePhase phase = gameManager.Phase;
            return phase != GamePhase.None &&
                phase != GamePhase.Warmup &&
                phase != GamePhase.PreGame &&
                phase != GamePhase.GameOver &&
                phase != GamePhase.PostGame;
        }

        private bool HasEnoughPlayersToAutoRecord()
        {
            int minimumPlayers = this.settings != null ? Mathf.Clamp(this.settings.MinimumPlayersToAutoRecord, 1, 64) : 1;
            if (minimumPlayers <= 1)
            {
                return true;
            }

            PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
            if (playerManager == null)
            {
                return false;
            }

            return playerManager.GetPlayers(false).Count >= minimumPlayers;
        }

        private static bool IsAutomaticRecordingEnabled(ReplayModSettings settings)
        {
            return settings != null && settings.RecordingMode == ReplayRecordingMode.AutomaticSave;
        }

        private bool ShouldSkipPlayer(Player player)
        {
            return player == null || player.IsReplay.Value;
        }

        private bool ShouldSkipPlayerBody(PlayerBody playerBody)
        {
            return playerBody == null || playerBody.Player == null || playerBody.Player.IsReplay.Value;
        }

        private bool ShouldSkipPuck(Puck puck)
        {
            return puck == null || puck.IsReplay.Value;
        }

        private string GetServerName()
        {
            try
            {
                ServerManager serverManager = NetworkBehaviourSingleton<ServerManager>.Instance;
                if (serverManager != null && serverManager.Server != null)
                {
                    string value = serverManager.Server.Value.Name.Value.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }

                // On a dedicated server the replicated Server value may be empty, so fall back to
                // the loaded server config name.
                if (serverManager != null && serverManager.ServerConfig != null && !string.IsNullOrEmpty(serverManager.ServerConfig.name))
                {
                    return serverManager.ServerConfig.name;
                }
            }
            catch
            {
            }

            return "Unknown Server";
        }

        private string GetRecorderName()
        {
            if (this.isDedicatedServer)
            {
                return "Dedicated Server";
            }

            try
            {
                PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
                Player localPlayer = playerManager != null ? playerManager.GetLocalPlayer() : null;
                if (localPlayer != null)
                {
                    return localPlayer.Username.Value.ToString();
                }
            }
            catch
            {
            }

            return "Unknown Player";
        }
    }
}

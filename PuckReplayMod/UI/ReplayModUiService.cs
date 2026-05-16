using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    public class ReplayModUiService
    {
        private readonly ReplayModSettings settings;
        private readonly ClientReplayRecorder recorder;
        private readonly ReplayStorageService storage;
        private readonly ReplayFileReader reader;
        private readonly ReplayPlaybackService playback;
        private readonly List<Button> sectionButtons = new List<Button>();
        private readonly List<string> sectionNames = new List<string>
        {
            "Library",
            "Recording",
            "Hotkeys",
            "Display",
            "Storage",
            "Advanced"
        };

        private VisualElement root;
        private VisualElement managerPanel;
        private VisualElement contentHost;
        private VisualElement content;
        private VisualElement playbackControlsPanel;
        private VisualElement playbackProgressBar;
        private Button playbackPlayPauseButton;
        private PlaybackPlayPauseIcon playbackPlayPauseIcon;
        private Label playbackTimeLabel;
        private PopupField<string> playbackSpeedDropdown;
        private Label statusLabel;
        private Label timelineLabel;
        private bool isManagerVisible;
        private bool playbackUiInputActive;
        private bool playbackUiMouseRequiredApplied;
        private bool mainMenuButtonAttached;
        private bool pauseMenuButtonAttached;
        private bool isMainMenuVisible = true;
        private string selectedSection = "Library";
        private float nextReplayIndexRealtime;

        internal ReplayModSettings Settings { get { return this.settings; } }
        internal ClientReplayRecorder Recorder { get { return this.recorder; } }
        internal ReplayStorageService Storage { get { return this.storage; } }
        internal ReplayFileReader Reader { get { return this.reader; } }
        internal ReplayPlaybackService Playback { get { return this.playback; } }
        internal VisualElement ReplayList { get; set; }
        internal Label StorageLabel { get; set; }
        internal Label StorageUsageLabel { get; set; }
        internal Label PlaybackLabel { get; set; }
        internal Label StatusLabel { get { return this.statusLabel; } }
        internal Label TimelineLabel { get { return this.timelineLabel; } }

        public ReplayModUiService(ReplayModSettings settings, ClientReplayRecorder recorder, ReplayStorageService storage, ReplayFileReader reader, ReplayPlaybackService playback)
        {
            this.settings = settings;
            this.recorder = recorder;
            this.storage = storage;
            this.reader = reader;
            this.playback = playback;
            this.recorder.RecordingStateChanged += this.RefreshStatusIndicator;
            this.recorder.TickAdvanced += this.RefreshStatusIndicator;
        }

        public void Initialize()
        {
            EventManager.AddEventListener("Event_OnClientStarted", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnClientStopped", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnMainMenuShow", this.Event_OnMainMenuShow);
            EventManager.AddEventListener("Event_OnMainMenuHide", this.Event_OnMainMenuHide);
            EventManager.AddEventListener("Event_OnMainMenuClickPlay", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnPauseMenuClickSelectTeam", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnPauseMenuClickSelectPosition", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnPauseMenuClickServerBrowser", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnPauseMenuClickDisconnect", this.Event_HideManager);
            EventManager.AddEventListener("Event_OnReplayManagerClickClose", this.Event_HideManager);
        }

        public void Dispose()
        {
            EventManager.RemoveEventListener("Event_OnClientStarted", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnClientStopped", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnMainMenuShow", this.Event_OnMainMenuShow);
            EventManager.RemoveEventListener("Event_OnMainMenuHide", this.Event_OnMainMenuHide);
            EventManager.RemoveEventListener("Event_OnMainMenuClickPlay", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnPauseMenuClickSelectTeam", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnPauseMenuClickSelectPosition", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnPauseMenuClickServerBrowser", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnPauseMenuClickDisconnect", this.Event_HideManager);
            EventManager.RemoveEventListener("Event_OnReplayManagerClickClose", this.Event_HideManager);
            this.recorder.RecordingStateChanged -= this.RefreshStatusIndicator;
            this.recorder.TickAdvanced -= this.RefreshStatusIndicator;

            if (this.root != null && this.root.parent != null)
            {
                this.root.parent.Remove(this.root);
            }

            this.root = null;
            this.managerPanel = null;
            this.contentHost = null;
            this.content = null;
            this.playbackControlsPanel = null;
            this.playbackProgressBar = null;
            this.playbackPlayPauseButton = null;
            this.playbackPlayPauseIcon = null;
            this.playbackTimeLabel = null;
            this.playbackSpeedDropdown = null;
            this.statusLabel = null;
            this.timelineLabel = null;
            this.playbackUiInputActive = false;
            this.playbackUiMouseRequiredApplied = false;
            this.ReplayList = null;
            this.StorageLabel = null;
            this.StorageUsageLabel = null;
            this.PlaybackLabel = null;
            this.sectionButtons.Clear();
            this.isManagerVisible = false;
            this.isMainMenuVisible = true;
            this.mainMenuButtonAttached = false;
            this.pauseMenuButtonAttached = false;
        }

        public void Tick()
        {
            this.PollManagerCloseHotkey();
            this.PollPlaybackUiInputHotkey();
            this.PollDisplayHotkeys();
            this.RefreshStatusIndicator();
            this.RefreshPlaybackStatus();
            this.RefreshPlaybackControls();
            this.TickReplayLibraryIndex();
        }

        internal bool IsPlaybackUiInputActive
        {
            get { return this.playbackUiInputActive && this.playback != null && this.playback.IsPlaybackActive; }
        }

        public void TryAttachToExistingUi()
        {
            UIManager uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null || uiManager.RootVisualElement == null)
            {
                return;
            }

            this.AttachRoot(uiManager);
            this.AttachMainMenuButton(uiManager.RootVisualElement);
            this.AttachPauseMenuButton(uiManager.RootVisualElement);
        }

        public void AttachRoot(UIManager uiManager)
        {
            if (uiManager == null || uiManager.RootVisualElement == null)
            {
                return;
            }

            if (this.root != null && this.root.parent == null)
            {
                this.root = null;
                this.managerPanel = null;
                this.contentHost = null;
                this.content = null;
                this.playbackControlsPanel = null;
                this.playbackProgressBar = null;
                this.playbackPlayPauseButton = null;
                this.playbackPlayPauseIcon = null;
                this.playbackTimeLabel = null;
                this.playbackSpeedDropdown = null;
                this.statusLabel = null;
                this.timelineLabel = null;
                this.playbackUiInputActive = false;
                this.playbackUiMouseRequiredApplied = false;
                this.ReplayList = null;
                this.StorageLabel = null;
                this.StorageUsageLabel = null;
                this.PlaybackLabel = null;
                this.sectionButtons.Clear();
                this.isManagerVisible = false;
                this.mainMenuButtonAttached = false;
                this.pauseMenuButtonAttached = false;
            }

            if (this.root != null)
            {
                return;
            }

            this.root = new VisualElement
            {
                name = "PuckReplayModRoot"
            };
            this.root.style.position = Position.Absolute;
            this.root.style.left = 0f;
            this.root.style.right = 0f;
            this.root.style.top = 0f;
            this.root.style.bottom = 0f;
            this.root.pickingMode = PickingMode.Ignore;

            this.CreateStatusIndicator();
            this.CreateTimelineIndicator();
            this.CreatePlaybackControlsPanel();
            this.CreateManagerPanel();
            uiManager.RootVisualElement.Add(this.root);
            this.SyncMainMenuVisibilityFromUi();
            this.RefreshStatusIndicator();
            this.RefreshTimelineIndicator();
        }

        public void AttachMainMenuButton(VisualElement rootVisualElement)
        {
            if (this.mainMenuButtonAttached)
            {
                return;
            }

            VisualElement mainMenu = rootVisualElement != null ? rootVisualElement.Q<VisualElement>("MainMenu") : null;
            this.mainMenuButtonAttached = this.AttachReplayButton(mainMenu, "PuckReplayModMainMenuButton");
        }

        public void AttachPauseMenuButton(VisualElement rootVisualElement)
        {
            if (this.pauseMenuButtonAttached)
            {
                return;
            }

            VisualElement pauseMenu = rootVisualElement != null ? rootVisualElement.Q<VisualElement>("PauseMenu") : null;
            this.pauseMenuButtonAttached = this.AttachReplayButton(pauseMenu, "PuckReplayModPauseMenuButton");
        }

        internal void SaveSettings()
        {
            this.settings.Save();
            this.RefreshLibraryText();
            this.RefreshStorageUsage();
        }

        internal void RefreshLibraryText()
        {
            if (this.StorageLabel == null)
            {
                return;
            }

            int replayCount = 0;
            if (Directory.Exists(this.storage.ReplaysDirectory))
            {
                replayCount = Directory.GetFiles(this.storage.ReplaysDirectory, "*" + ReplayModConstants.ReplayFileExtension).Length;
            }

            string recordingState = this.recorder.IsRecording ? "Recording now" : (this.settings.AutoRecord ? "Ready to record" : "Automatic recording is off");
            this.StorageLabel.text = "Saved replays: " + replayCount + "\nStatus: " + recordingState + "\nRecord rate: " + ReplayRecordingSettingsSection.FormatCaptureRate(this.settings.CaptureTickRate);
        }

        internal void RefreshReplayList()
        {
            ReplayLibrarySection.RefreshReplayList(this);
        }

        internal void RefreshStorageUsage()
        {
            ReplayStorageSection.RefreshStorageUsage(this);
        }

        internal void PlayReplay(string filePath)
        {
            if (this.recorder.IsRecording)
            {
                ReplayModLog.Warning("Cannot start replay playback while recording a live session.");
                return;
            }

            try
            {
                this.playback.Play(filePath);
                this.RefreshPlaybackStatus();
                this.HideManager();
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to play replay " + filePath + ": " + exception.Message);
            }
        }

        internal void DeleteReplay(ReplayFileSummary replay)
        {
            if (replay == null || string.IsNullOrEmpty(replay.FilePath))
            {
                return;
            }

            try
            {
                this.storage.DeleteReplay(replay.FilePath);
                this.RefreshLibraryText();
                this.RefreshReplayList();
                this.RefreshStorageUsage();
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to delete replay " + replay.FilePath + ": " + exception.Message);
            }
        }

        internal void RenameReplay(ReplayFileSummary replay, string displayName)
        {
            if (replay == null || string.IsNullOrEmpty(replay.FilePath))
            {
                return;
            }

            try
            {
                this.storage.SetReplayDisplayName(replay.FilePath, displayName);
                this.RefreshReplayList();
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to rename replay " + replay.FilePath + ": " + exception.Message);
            }
        }

        internal void SetReplayFavorite(ReplayFileSummary replay, bool isFavorite)
        {
            if (replay == null || string.IsNullOrEmpty(replay.FilePath))
            {
                return;
            }

            try
            {
                this.storage.SetReplayFavorite(replay.FilePath, isFavorite);
                this.RefreshReplayList();
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to update replay favorite " + replay.FilePath + ": " + exception.Message);
            }
        }

        internal void OpenReplayLocation(ReplayFileSummary replay)
        {
            if (replay == null || string.IsNullOrEmpty(replay.FilePath))
            {
                return;
            }

            try
            {
                if (File.Exists(replay.FilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + replay.FilePath + "\"",
                        UseShellExecute = true
                    });
                    return;
                }

                if (Directory.Exists(this.storage.ReplaysDirectory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = this.storage.ReplaysDirectory,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception exception)
            {
                ReplayModLog.Warning("Failed to open replay location " + replay.FilePath + ": " + exception.Message);
            }
        }

        internal void CopyReplayPath(ReplayFileSummary replay)
        {
            if (replay == null || string.IsNullOrEmpty(replay.FilePath))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = replay.FilePath;
            ReplayModLog.Info("Copied replay path to clipboard: " + replay.FilePath);
        }

        internal void RefreshPlaybackStatus()
        {
            this.RefreshTimelineIndicator();
            if (this.PlaybackLabel == null)
            {
                return;
            }

            if (this.playback.IsPlaying)
            {
                this.PlaybackLabel.text = "Watching replay: " + this.FormatPlaybackTime(this.playback.CurrentTick) + " / " + this.FormatPlaybackTime(this.playback.TotalTicks);
                return;
            }

            this.PlaybackLabel.text = "Not watching a replay.";
        }

        private void RefreshPlaybackControls()
        {
            if (this.playbackControlsPanel == null)
            {
                return;
            }

            bool shouldShow = this.playback != null && this.playback.IsPlaybackActive && !this.isManagerVisible;
            this.playbackControlsPanel.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shouldShow)
            {
                if (this.playback == null || !this.playback.IsPlaybackActive)
                {
                    this.playbackUiInputActive = false;
                    this.SetPlaybackUiMouseRequired(false);
                }

                return;
            }

            bool controlsInteractive = this.playbackUiInputActive;
            this.SetPlaybackControlsPickingMode(this.playbackControlsPanel, PickingMode.Position);
            if (this.playbackProgressBar != null)
            {
                this.playbackProgressBar.pickingMode = PickingMode.Ignore;
            }

            this.SetPlaybackUiMouseRequired(controlsInteractive);

            int currentTick = Math.Max(0, this.playback.CurrentTick);
            int totalTicks = Math.Max(currentTick, this.playback.TotalTicks);
            if (this.playbackPlayPauseIcon != null)
            {
                this.playbackPlayPauseIcon.SetMode(this.playback.IsPaused ? PlaybackPlayPauseIconMode.Play : PlaybackPlayPauseIconMode.Pause);
            }

            if (this.playbackTimeLabel != null)
            {
                this.playbackTimeLabel.text = this.FormatPlaybackTime(currentTick) + " / " + this.FormatPlaybackTime(totalTicks);
            }

            if (this.playbackProgressBar != null)
            {
                float percent = totalTicks > 0 ? Mathf.Clamp01(currentTick / (float)totalTicks) * 100f : 0f;
                this.playbackProgressBar.style.width = new StyleLength(new Length(percent, LengthUnit.Percent));
            }

            if (this.playbackSpeedDropdown != null)
            {
                string speedText = FormatSpeed(this.playback.PlaybackSpeed);
                if (this.playbackSpeedDropdown.value != speedText)
                {
                    this.playbackSpeedDropdown.SetValueWithoutNotify(speedText);
                }
            }
        }

        private void OnPlaybackTimelinePointerDown(PointerDownEvent evt)
        {
            if (!this.IsPlaybackControlInputAllowed())
            {
                evt.StopImmediatePropagation();
                return;
            }

            VisualElement target = evt.currentTarget as VisualElement;
            if (target == null || this.playback == null || !this.playback.IsPlaying || this.playback.TotalTicks <= 0)
            {
                return;
            }

            float width = target.contentRect.width;
            if (width <= 0f)
            {
                return;
            }

            float normalized = Mathf.Clamp01(evt.localPosition.x / width);
            this.playback.SeekToTick(Mathf.RoundToInt(this.playback.TotalTicks * normalized));
            this.RefreshPlaybackControls();
            this.RefreshPlaybackStatus();
            evt.StopPropagation();
        }

        private bool IsPlaybackControlInputAllowed()
        {
            if (this.playback == null || !this.playback.IsPlaybackActive || this.isManagerVisible)
            {
                return false;
            }

            if (this.playbackUiInputActive)
            {
                return true;
            }

            return this.settings != null &&
                this.settings.PlaybackUiInputMode == ReplayPlaybackUiInputMode.Hold &&
                this.IsKeyHeld(this.settings.PlaybackUiInputKey);
        }

        private void OnPlaybackSpeedChanged(string value)
        {
            if (string.IsNullOrEmpty(value) || this.playback == null)
            {
                return;
            }

            string rawValue = value.Replace("x", string.Empty);
            float speed;
            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out speed))
            {
                return;
            }

            this.playback.SetPlaybackSpeed(speed);
            this.RefreshPlaybackControls();
        }

        private static string FormatSpeed(float speed)
        {
            if (Math.Abs(speed - 1f) < 0.01f)
            {
                return "1x";
            }

            return speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        }

        internal void RefreshTimelineIndicator()
        {
            if (this.timelineLabel == null)
            {
                return;
            }

            if (!this.settings.ShowPlaybackTimeline || !this.playback.IsPlaying)
            {
                this.timelineLabel.style.display = DisplayStyle.None;
                return;
            }

            int currentTick = Math.Max(0, this.playback.CurrentTick);
            int totalTicks = Math.Max(currentTick, this.playback.TotalTicks);
            this.timelineLabel.text = "REPLAY  " + this.FormatPlaybackTime(currentTick) + " / " + this.FormatPlaybackTime(totalTicks);
            this.timelineLabel.style.display = DisplayStyle.Flex;
        }

        internal void RefreshStatusIndicator()
        {
            if (this.statusLabel == null)
            {
                return;
            }

            if (!this.settings.ShowStatusIndicator || this.isMainMenuVisible)
            {
                this.statusLabel.style.display = DisplayStyle.None;
                return;
            }

            bool shouldShowReady = this.settings.StatusIndicatorVisibility == ReplayIndicatorVisibility.Always;
            bool shouldShowPlayback = this.settings.StatusIndicatorVisibility == ReplayIndicatorVisibility.Always ||
                this.settings.StatusIndicatorVisibility == ReplayIndicatorVisibility.RecordingAndPlayback;
            bool shouldShowRecording = this.settings.StatusIndicatorVisibility == ReplayIndicatorVisibility.Always ||
                this.settings.StatusIndicatorVisibility == ReplayIndicatorVisibility.RecordingAndPlayback ||
                this.settings.StatusIndicatorVisibility == ReplayIndicatorVisibility.RecordingOnly;

            if (this.recorder.IsRecording)
            {
                this.statusLabel.style.display = shouldShowRecording ? DisplayStyle.Flex : DisplayStyle.None;
                this.statusLabel.text = "REPLAY MOD  REC  " + this.recorder.CurrentTick;
                this.statusLabel.style.backgroundColor = new Color(0.55f, 0.05f, 0.06f, 0.9f);
                return;
            }

            if (this.recorder.IsRecordingSuppressed)
            {
                this.statusLabel.style.display = shouldShowPlayback ? DisplayStyle.Flex : DisplayStyle.None;
                this.statusLabel.text = "REPLAY MOD  PLAYBACK";
                this.statusLabel.style.backgroundColor = new Color(0.1f, 0.18f, 0.32f, 0.9f);
                return;
            }

            this.statusLabel.style.display = shouldShowReady ? DisplayStyle.Flex : DisplayStyle.None;
            this.statusLabel.text = "REPLAY MOD  READY";
            this.statusLabel.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.72f);
        }

        internal void ApplyOverlayPosition(VisualElement element, ReplayOverlayPosition position, float offset)
        {
            if (element == null)
            {
                return;
            }

            element.style.left = StyleKeyword.Auto;
            element.style.right = StyleKeyword.Auto;
            element.style.top = StyleKeyword.Auto;
            element.style.bottom = StyleKeyword.Auto;

            switch (position)
            {
                case ReplayOverlayPosition.TopLeft:
                    element.style.left = 18f;
                    element.style.top = offset;
                    break;
                case ReplayOverlayPosition.BottomRight:
                    element.style.right = 18f;
                    element.style.bottom = offset;
                    break;
                case ReplayOverlayPosition.BottomLeft:
                    element.style.left = 18f;
                    element.style.bottom = offset;
                    break;
                default:
                    element.style.right = 18f;
                    element.style.top = offset;
                    break;
            }
        }

        private bool AttachReplayButton(VisualElement menu, string name)
        {
            if (menu == null || menu.Q<Button>(name) != null)
            {
                return menu != null;
            }

            Button referenceButton = menu.Q<Button>("ModsButton") ?? menu.Q<Button>("SettingsButton") ?? menu.Q<Button>("ServerBrowserButton");
            Button button = new Button(this.ToggleManager)
            {
                name = name,
                text = "REPLAYS"
            };
            ReplayUiTools.StyleMenuAccessButton(referenceButton, button);

            if (referenceButton != null && referenceButton.parent == menu)
            {
                menu.Insert(referenceButton.parent.IndexOf(referenceButton) + 1, button);
            }
            else
            {
                menu.Add(button);
            }

            return true;
        }

        private void CreateStatusIndicator()
        {
            this.statusLabel = new Label("IDLE")
            {
                name = "PuckReplayModStatus"
            };
            this.statusLabel.style.position = Position.Absolute;
            this.statusLabel.style.paddingLeft = 8f;
            this.statusLabel.style.paddingRight = 8f;
            this.statusLabel.style.paddingTop = 4f;
            this.statusLabel.style.paddingBottom = 4f;
            this.statusLabel.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.72f);
            this.statusLabel.style.color = Color.white;
            this.statusLabel.style.fontSize = 13f;
            this.statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            this.statusLabel.pickingMode = PickingMode.Ignore;
            this.ApplyOverlayPosition(this.statusLabel, this.settings.StatusIndicatorPosition, 76f);
            this.root.Add(this.statusLabel);
        }

        private void CreateTimelineIndicator()
        {
            this.timelineLabel = new Label
            {
                name = "PuckReplayModTimeline"
            };
            this.timelineLabel.style.position = Position.Absolute;
            this.timelineLabel.style.paddingLeft = 8f;
            this.timelineLabel.style.paddingRight = 8f;
            this.timelineLabel.style.paddingTop = 4f;
            this.timelineLabel.style.paddingBottom = 4f;
            this.timelineLabel.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 0.78f);
            this.timelineLabel.style.color = Color.white;
            this.timelineLabel.style.fontSize = 12f;
            this.timelineLabel.style.display = DisplayStyle.None;
            this.timelineLabel.pickingMode = PickingMode.Ignore;
            this.ApplyOverlayPosition(this.timelineLabel, this.settings.PlaybackTimelinePosition, 110f);
            this.root.Add(this.timelineLabel);
        }

        private void CreatePlaybackControlsPanel()
        {
            this.playbackControlsPanel = new VisualElement
            {
                name = "PuckReplayModPlaybackControls"
            };
            this.playbackControlsPanel.style.position = Position.Absolute;
            this.playbackControlsPanel.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            this.playbackControlsPanel.style.bottom = 22f;
            this.playbackControlsPanel.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            this.playbackControlsPanel.style.width = new StyleLength(new Length(68f, LengthUnit.Percent));
            this.playbackControlsPanel.style.maxWidth = 860f;
            this.playbackControlsPanel.style.minWidth = 580f;
            this.playbackControlsPanel.style.height = 50f;
            this.playbackControlsPanel.style.minHeight = 50f;
            this.playbackControlsPanel.style.flexDirection = FlexDirection.Row;
            this.playbackControlsPanel.style.alignItems = Align.Center;
            this.playbackControlsPanel.style.paddingLeft = 8f;
            this.playbackControlsPanel.style.paddingRight = 8f;
            this.playbackControlsPanel.style.backgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.86f);
            this.playbackControlsPanel.style.display = DisplayStyle.None;
            this.playbackControlsPanel.pickingMode = PickingMode.Position;
            this.playbackControlsPanel.RegisterCallback<PointerDownEvent>(this.OnPlaybackControlsPointerDown, TrickleDown.TrickleDown);

            Button stopButton = ReplayUiTools.CreateButton("EXIT", null);
            stopButton.RegisterCallback<PointerDownEvent>(this.OnPlaybackExitPointerDown);
            stopButton.style.width = 70f;
            stopButton.style.minWidth = 70f;
            stopButton.style.height = 32f;
            stopButton.style.minHeight = 32f;
            stopButton.style.marginRight = 6f;
            this.playbackControlsPanel.Add(stopButton);

            this.playbackPlayPauseButton = ReplayUiTools.CreateButton(string.Empty, null);
            this.playbackPlayPauseButton.RegisterCallback<PointerDownEvent>(this.OnPlaybackPlayPausePointerDown);
            this.playbackPlayPauseButton.style.width = 78f;
            this.playbackPlayPauseButton.style.minWidth = 78f;
            this.playbackPlayPauseButton.style.height = 32f;
            this.playbackPlayPauseButton.style.minHeight = 32f;
            this.playbackPlayPauseButton.style.marginRight = 8f;
            this.playbackPlayPauseButton.style.alignItems = Align.Center;
            this.playbackPlayPauseButton.style.justifyContent = Justify.Center;
            this.playbackPlayPauseButton.style.paddingLeft = 0f;
            this.playbackPlayPauseButton.style.paddingRight = 0f;
            this.playbackPlayPauseButton.style.paddingTop = 0f;
            this.playbackPlayPauseButton.style.paddingBottom = 0f;
            this.playbackPlayPauseIcon = new PlaybackPlayPauseIcon(PlaybackPlayPauseIconMode.Pause, 20f);
            this.playbackPlayPauseButton.Add(this.playbackPlayPauseIcon);
            this.playbackPlayPauseButton.RegisterCallback<MouseEnterEvent>(delegate
            {
                this.playbackPlayPauseIcon.SetColor(Color.black);
            });
            this.playbackPlayPauseButton.RegisterCallback<MouseLeaveEvent>(delegate
            {
                this.playbackPlayPauseIcon.SetColor(Color.white);
            });
            this.playbackControlsPanel.Add(this.playbackPlayPauseButton);

            VisualElement timelineTrack = new VisualElement
            {
                name = "PuckReplayModPlaybackScrubTrack"
            };
            timelineTrack.style.flexGrow = 1f;
            timelineTrack.style.height = 28f;
            timelineTrack.style.marginRight = 8f;
            timelineTrack.style.backgroundColor = new Color(0.22f, 0.24f, 0.26f, 1f);
            timelineTrack.RegisterCallback<PointerDownEvent>(this.OnPlaybackTimelinePointerDown);

            this.playbackProgressBar = new VisualElement
            {
                name = "PuckReplayModPlaybackScrubProgress"
            };
            this.playbackProgressBar.style.position = Position.Absolute;
            this.playbackProgressBar.style.left = 0f;
            this.playbackProgressBar.style.top = 0f;
            this.playbackProgressBar.style.bottom = 0f;
            this.playbackProgressBar.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            this.playbackProgressBar.style.backgroundColor = new Color(0.72f, 0.72f, 0.72f, 0.55f);
            this.playbackProgressBar.pickingMode = PickingMode.Ignore;
            timelineTrack.Add(this.playbackProgressBar);
            this.playbackControlsPanel.Add(timelineTrack);

            this.playbackTimeLabel = new Label("00:00 / 00:00");
            this.playbackTimeLabel.style.width = 112f;
            this.playbackTimeLabel.style.minWidth = 112f;
            this.playbackTimeLabel.style.color = Color.white;
            this.playbackTimeLabel.style.fontSize = 12f;
            this.playbackTimeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            this.playbackTimeLabel.style.marginRight = 8f;
            this.playbackControlsPanel.Add(this.playbackTimeLabel);

            this.playbackSpeedDropdown = ReplayUiTools.CreateDropdown(new List<string>
            {
                "0.25x",
                "0.5x",
                "1x",
                "2x",
                "4x"
            }, "1x", this.OnPlaybackSpeedChanged);
            this.playbackSpeedDropdown.style.width = 82f;
            this.playbackSpeedDropdown.style.minWidth = 82f;
            this.playbackSpeedDropdown.style.maxWidth = 82f;
            this.playbackControlsPanel.Add(this.playbackSpeedDropdown);

            this.root.Add(this.playbackControlsPanel);
        }

        private void OnPlaybackControlsPointerDown(PointerDownEvent evt)
        {
            if (this.IsPlaybackControlInputAllowed())
            {
                return;
            }

            evt.StopImmediatePropagation();
        }

        private void OnPlaybackExitPointerDown(PointerDownEvent evt)
        {
            if (!this.IsPlaybackControlInputAllowed())
            {
                evt.StopImmediatePropagation();
                return;
            }

            this.playback.Close();
            this.RefreshPlaybackControls();
            this.RefreshPlaybackStatus();
            evt.StopImmediatePropagation();
        }

        private void OnPlaybackPlayPausePointerDown(PointerDownEvent evt)
        {
            if (!this.IsPlaybackControlInputAllowed())
            {
                evt.StopImmediatePropagation();
                return;
            }

            this.playback.TogglePause();
            this.RefreshPlaybackControls();
            evt.StopImmediatePropagation();
        }

        private enum PlaybackPlayPauseIconMode
        {
            Play,
            Pause
        }

        private sealed class PlaybackPlayPauseIcon : VisualElement
        {
            private PlaybackPlayPauseIconMode mode;
            private Color iconColor = Color.white;

            public PlaybackPlayPauseIcon(PlaybackPlayPauseIconMode mode, float size)
            {
                this.mode = mode;
                base.pickingMode = PickingMode.Ignore;
                base.style.width = size;
                base.style.height = size;
                base.generateVisualContent += this.OnGenerateVisualContent;
            }

            public void SetMode(PlaybackPlayPauseIconMode value)
            {
                if (this.mode == value)
                {
                    return;
                }

                this.mode = value;
                base.MarkDirtyRepaint();
            }

            public void SetColor(Color color)
            {
                if (this.iconColor == color)
                {
                    return;
                }

                this.iconColor = color;
                base.MarkDirtyRepaint();
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                Rect rect = base.contentRect;
                float size = Mathf.Min(rect.width, rect.height);
                if (size <= 0f)
                {
                    return;
                }

                Painter2D painter = context.painter2D;
                painter.fillColor = this.iconColor;
                if (this.mode == PlaybackPlayPauseIconMode.Play)
                {
                    this.DrawPlay(painter, rect, size);
                    return;
                }

                this.DrawPause(painter, rect, size);
            }

            private void DrawPlay(Painter2D painter, Rect rect, float size)
            {
                float left = rect.x + (rect.width * 0.34f);
                float right = rect.x + (rect.width * 0.72f);
                float top = rect.y + (rect.height * 0.22f);
                float middle = rect.y + (rect.height * 0.5f);
                float bottom = rect.y + (rect.height * 0.78f);

                painter.BeginPath();
                painter.MoveTo(new Vector2(left, top));
                painter.LineTo(new Vector2(right, middle));
                painter.LineTo(new Vector2(left, bottom));
                painter.ClosePath();
                painter.Fill();
            }

            private void DrawPause(Painter2D painter, Rect rect, float size)
            {
                float top = rect.y + (rect.height * 0.18f);
                float bottom = rect.y + (rect.height * 0.82f);
                float barWidth = size * 0.18f;
                float gap = size * 0.18f;
                float totalWidth = (barWidth * 2f) + gap;
                float left = rect.x + ((rect.width - totalWidth) * 0.5f);

                this.DrawRect(painter, left, top, barWidth, bottom - top);
                this.DrawRect(painter, left + barWidth + gap, top, barWidth, bottom - top);
            }

            private void DrawRect(Painter2D painter, float x, float y, float width, float height)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + width, y));
                painter.LineTo(new Vector2(x + width, y + height));
                painter.LineTo(new Vector2(x, y + height));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private void CreateManagerPanel()
        {
            this.managerPanel = new VisualElement
            {
                name = "PuckReplayModManager"
            };
            this.managerPanel.style.position = Position.Absolute;
            this.managerPanel.style.left = new StyleLength(new Length(50f, LengthUnit.Percent));
            this.managerPanel.style.top = new StyleLength(new Length(50f, LengthUnit.Percent));
            this.managerPanel.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), new Length(-50f, LengthUnit.Percent));
            this.managerPanel.style.width = new StyleLength(new Length(72f, LengthUnit.Percent));
            this.managerPanel.style.maxWidth = 980f;
            this.managerPanel.style.minWidth = 660f;
            this.managerPanel.style.height = new StyleLength(new Length(76f, LengthUnit.Percent));
            this.managerPanel.style.maxHeight = 720f;
            this.managerPanel.style.minHeight = 460f;
            this.managerPanel.style.backgroundColor = new StyleColor(ReplayUiTools.PanelColor);
            this.managerPanel.style.display = DisplayStyle.None;
            this.managerPanel.pickingMode = PickingMode.Ignore;

            this.CreateManagerHeader();
            this.CreateManagerBody();
            this.CreateManagerFooter();

            this.root.Add(this.managerPanel);
            this.ShowSection(this.selectedSection);
        }

        private void CreateManagerHeader()
        {
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.height = 70f;
            header.style.minHeight = 70f;
            header.style.paddingLeft = 14f;
            header.style.paddingRight = 14f;
            header.style.backgroundColor = new StyleColor(ReplayUiTools.HeaderColor);

            Label title = new Label("Replay Manager");
            title.style.fontSize = 28f;
            title.style.color = Color.white;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            header.Add(this.CreateVanillaCloseButton());
            this.managerPanel.Add(header);
        }

        private VisualElement CreateVanillaCloseButton()
        {
            VisualElement closeButtonContainer = new VisualElement
            {
                name = "CloseIconButtonContainer"
            };
            closeButtonContainer.AddToClassList("CloseIconButtonContainer");
            closeButtonContainer.style.width = 42f;
            closeButtonContainer.style.minWidth = 42f;
            closeButtonContainer.style.height = 42f;
            closeButtonContainer.style.minHeight = 42f;
            closeButtonContainer.style.alignItems = Align.Center;
            closeButtonContainer.style.justifyContent = Justify.Center;

            Button closeButton = new Button(this.RequestManagerClose)
            {
                name = "IconButton",
                text = "X"
            };
            closeButton.AddToClassList("IconButton");
            closeButton.AddToClassList("CloseIconButton");
            ReplayUiTools.StyleConfigButton(closeButton);
            closeButton.style.width = 42f;
            closeButton.style.minWidth = 42f;
            closeButton.style.height = 42f;
            closeButton.style.minHeight = 42f;
            closeButton.style.paddingLeft = 0f;
            closeButton.style.paddingRight = 0f;
            closeButton.style.paddingTop = 0f;
            closeButton.style.paddingBottom = 0f;
            closeButton.style.fontSize = 22f;
            closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeButtonContainer.Add(closeButton);
            return closeButtonContainer;
        }

        private void CreateManagerBody()
        {
            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;

            VisualElement sidebar = new VisualElement();
            sidebar.style.width = new StyleLength(new Length(28f, LengthUnit.Percent));
            sidebar.style.minWidth = 190f;
            sidebar.style.maxWidth = 250f;
            sidebar.style.backgroundColor = new StyleColor(ReplayUiTools.ControlColor);
            sidebar.style.flexShrink = 0f;
            body.Add(sidebar);

            ScrollView sidebarScroll = new ScrollView();
            sidebarScroll.style.flexGrow = 1f;
            sidebar.Add(sidebarScroll);

            for (int i = 0; i < this.sectionNames.Count; i++)
            {
                string sectionName = this.sectionNames[i];
                Button sectionButton = ReplayUiTools.CreateSidebarButton(sectionName, delegate
                {
                    this.ShowSection(sectionName);
                });
                this.sectionButtons.Add(sectionButton);
                sidebarScroll.Add(sectionButton);
            }

            this.contentHost = new VisualElement
            {
                name = "PuckReplayModManagerContentHost"
            };
            this.contentHost.style.flexGrow = 1f;
            body.Add(this.contentHost);
            this.managerPanel.Add(body);
        }

        private void CreateManagerFooter()
        {
            VisualElement footer = new VisualElement();
            footer.style.height = 10f;
            footer.style.minHeight = 10f;
            footer.style.backgroundColor = new StyleColor(ReplayUiTools.HeaderColor);
            this.managerPanel.Add(footer);
        }

        private void ShowSection(string sectionName)
        {
            if (this.contentHost == null)
            {
                return;
            }

            this.selectedSection = sectionName;
            this.ReplayList = null;
            this.StorageLabel = null;
            this.StorageUsageLabel = null;
            this.PlaybackLabel = null;
            this.content = null;
            this.contentHost.Clear();

            if (sectionName == "Library")
            {
                this.content = this.CreateSectionContent();
                this.contentHost.Add(this.content);
            }
            else
            {
                ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.style.flexGrow = 1f;
                scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                this.contentHost.Add(scrollView);

                this.content = this.CreateSectionContent();
                scrollView.Add(this.content);
            }

            this.content.Clear();

            if (sectionName == "Recording")
            {
                ReplayRecordingSettingsSection.Create(this, this.content);
            }
            else if (sectionName == "Hotkeys")
            {
                ReplayHotkeySettingsSection.Create(this, this.content);
            }
            else if (sectionName == "Display")
            {
                ReplayOverlaySettingsSection.Create(this, this.content);
            }
            else if (sectionName == "Storage")
            {
                ReplayStorageSection.Create(this, this.content);
            }
            else if (sectionName == "Advanced")
            {
                ReplayAdvancedSettingsSection.Create(this, this.content);
            }
            else
            {
                ReplayLibrarySection.Create(this, this.content);
            }

            this.UpdateSidebarSelection();
        }

        private VisualElement CreateSectionContent()
        {
            VisualElement sectionContent = new VisualElement
            {
                name = "PuckReplayModManagerContent"
            };
            sectionContent.style.flexGrow = 1f;
            sectionContent.style.paddingLeft = 18f;
            sectionContent.style.paddingRight = 18f;
            sectionContent.style.paddingTop = 16f;
            sectionContent.style.paddingBottom = 16f;
            return sectionContent;
        }

        private void UpdateSidebarSelection()
        {
            for (int i = 0; i < this.sectionButtons.Count; i++)
            {
                bool selected = i < this.sectionNames.Count && this.sectionNames[i] == this.selectedSection;
                ReplayUiTools.SetSidebarButtonSelected(this.sectionButtons[i], selected);
            }
        }

        private void ToggleManager()
        {
            if (this.isManagerVisible)
            {
                this.HideManager();
                return;
            }

            this.ShowManager();
        }

        private void ShowManager()
        {
            if (this.managerPanel == null)
            {
                return;
            }

            this.isManagerVisible = true;
            this.managerPanel.style.display = DisplayStyle.Flex;
            this.root.pickingMode = PickingMode.Ignore;
            this.managerPanel.pickingMode = PickingMode.Position;
            this.ShowSection(this.selectedSection);
            this.RefreshPlaybackControls();

            try
            {
                GlobalStateManager.SetUIState(new Dictionary<string, object>
                {
                    { "isMouseRequired", true }
                });
            }
            catch (Exception)
            {
            }
        }

        private void TickReplayLibraryIndex()
        {
            if (!this.isManagerVisible || this.ReplayList == null || this.reader == null || this.storage == null)
            {
                return;
            }

            float realtime = Time.realtimeSinceStartup;
            if (realtime < this.nextReplayIndexRealtime)
            {
                return;
            }

            this.nextReplayIndexRealtime = realtime + 0.2f;
            if (this.reader.IndexNextMissingSummary(this.storage.ReplaysDirectory, this.storage.SummariesDirectory, 12))
            {
                this.RefreshReplayList();
            }
        }

        private void HideManager()
        {
            if (this.managerPanel == null)
            {
                return;
            }

            this.isManagerVisible = false;
            this.managerPanel.style.display = DisplayStyle.None;
            this.root.pickingMode = PickingMode.Ignore;
            this.managerPanel.pickingMode = PickingMode.Ignore;
            this.RefreshPlaybackControls();
        }

        internal bool TryCloseManager()
        {
            if (!this.isManagerVisible)
            {
                return false;
            }

            this.RequestManagerClose();
            return true;
        }

        private void RequestManagerClose()
        {
            EventManager.TriggerEvent("Event_OnReplayManagerClickClose", null);
        }

        private void PollManagerCloseHotkey()
        {
            if (!this.isManagerVisible)
            {
                return;
            }

            if (this.IsKeyPressed(KeyCode.Escape))
            {
                this.RequestManagerClose();
            }
        }

        private void PollPlaybackUiInputHotkey()
        {
            if (this.settings == null || this.playback == null || !this.playback.IsPlaybackActive)
            {
                if (this.playbackUiInputActive)
                {
                    this.playbackUiInputActive = false;
                    this.RefreshPlaybackControls();
                }

                return;
            }

            bool previous = this.playbackUiInputActive;
            if (this.settings.PlaybackUiInputMode == ReplayPlaybackUiInputMode.Hold)
            {
                this.playbackUiInputActive = this.IsKeyHeld(this.settings.PlaybackUiInputKey);
            }
            else if (this.IsKeyPressed(this.settings.PlaybackUiInputKey))
            {
                this.playbackUiInputActive = !this.playbackUiInputActive;
            }

            if (previous != this.playbackUiInputActive)
            {
                this.RefreshPlaybackControls();
            }
        }

        private void SetPlaybackControlsPickingMode(VisualElement element, PickingMode pickingMode)
        {
            if (element == null)
            {
                return;
            }

            element.pickingMode = pickingMode;
            foreach (VisualElement child in element.Children())
            {
                this.SetPlaybackControlsPickingMode(child, pickingMode);
            }
        }

        private void SetPlaybackUiMouseRequired(bool isRequired)
        {
            if (this.playbackUiMouseRequiredApplied == isRequired)
            {
                return;
            }

            this.playbackUiMouseRequiredApplied = isRequired;
            try
            {
                GlobalStateManager.SetUIState(new Dictionary<string, object>
                {
                    { "isMouseRequired", isRequired }
                });
            }
            catch (Exception)
            {
            }
        }

        private void PollDisplayHotkeys()
        {
            if (this.settings == null)
            {
                return;
            }

            if (this.IsKeyPressed(this.settings.ToggleStatusBadgeKey))
            {
                this.settings.ShowStatusIndicator = !this.settings.ShowStatusIndicator;
                this.settings.Save();
                this.RefreshStatusIndicator();
            }

            if (this.IsKeyPressed(this.settings.ToggleReplayTimeKey))
            {
                this.settings.ShowPlaybackTimeline = !this.settings.ShowPlaybackTimeline;
                this.settings.Save();
                this.RefreshTimelineIndicator();
            }
        }

        private bool IsKeyPressed(KeyCode keyCode)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            Key key;
            if (!TryConvertKey(keyCode, out key))
            {
                return false;
            }

            return keyboard[key] != null && keyboard[key].wasPressedThisFrame;
        }

        private bool IsKeyHeld(KeyCode keyCode)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            Key key;
            if (!TryConvertKey(keyCode, out key))
            {
                return false;
            }

            return keyboard[key] != null && keyboard[key].isPressed;
        }

        private static bool TryConvertKey(KeyCode keyCode, out Key key)
        {
            string keyName = keyCode.ToString();
            if (keyName.StartsWith("Alpha", StringComparison.Ordinal))
            {
                keyName = "Digit" + keyName.Substring("Alpha".Length);
            }
            else if (keyName.EndsWith("Control", StringComparison.Ordinal))
            {
                keyName = keyName.Substring(0, keyName.Length - "Control".Length) + "Ctrl";
            }

            if (Enum.TryParse(keyName, true, out key))
            {
                return key != Key.None;
            }

            return false;
        }

        private void Event_HideManager(Dictionary<string, object> message)
        {
            this.HideManager();
            this.SyncMainMenuVisibilityFromUi();
            this.RefreshStatusIndicator();
        }

        private void Event_OnMainMenuShow(Dictionary<string, object> message)
        {
            this.isMainMenuVisible = true;
            this.RefreshStatusIndicator();
        }

        private void Event_OnMainMenuHide(Dictionary<string, object> message)
        {
            this.isMainMenuVisible = false;
            this.HideManager();
            this.RefreshStatusIndicator();
        }

        private void SyncMainMenuVisibilityFromUi()
        {
            UIManager uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            this.isMainMenuVisible = uiManager == null || uiManager.MainMenu == null || uiManager.MainMenu.IsVisible;
        }

        private string FormatPlaybackTime(int tick)
        {
            int tickRate = Math.Max(1, this.playback.TickRate);
            TimeSpan timeSpan = TimeSpan.FromSeconds((double)tick / tickRate);
            if (timeSpan.TotalHours >= 1.0)
            {
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
            }

            return string.Format("{0:D2}:{1:D2}", timeSpan.Minutes, timeSpan.Seconds);
        }
    }
}

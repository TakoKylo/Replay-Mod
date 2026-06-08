using System;
using UnityEngine;

namespace PuckReplayMod
{
    public enum ReplayOverlayPosition
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft
    }

    public enum ReplayIndicatorVisibility
    {
        Always,
        RecordingAndPlayback,
        RecordingOnly,
        ScoreboardOnly,
        Hidden
    }

    public enum ReplayPlaybackUiInputMode
    {
        Toggle,
        Hold
    }

    public enum ReplayPlaybackCameraMode
    {
        Free,
        FirstPerson,
        ThirdPerson
    }

    public enum ReplayRecordingMode
    {
        AutomaticSave,
        ManualOnly
    }

    public class ReplayModSettings
    {
        private const string Prefix = "PuckReplayMod.";

        public ReplayRecordingMode RecordingMode = ReplayRecordingMode.AutomaticSave;
        public bool AutoRecord = true;
        public bool EnableManualRecordingHotkey = true;
        public bool SaveOnDisconnect = true;
        public bool SplitRecordingsByGameEnd = false;
        public bool RecordOnlyDuringGames = true;
        public bool EnableServerSideRecording = true;
        public int CaptureTickRate = 30;
        public int MinimumPlayersToAutoRecord = 1;
        public int StorageLimitMb = 2048;
        public int MinimumReplayLengthSeconds = 10;
        public KeyCode ManualRecordingKey = KeyCode.F5;
        public KeyCode MarkerKey = KeyCode.F4;
        public KeyCode ToggleStatusBadgeKey = KeyCode.F7;
        public KeyCode ToggleReplayTimeKey = KeyCode.F8;
        public KeyCode PlaybackUiInputKey = KeyCode.LeftAlt;
        public KeyCode CaptureModeKey = KeyCode.F10;
        public ReplayPlaybackUiInputMode PlaybackUiInputMode = ReplayPlaybackUiInputMode.Toggle;
        public bool EnableDebugProfiling = false;
        public bool EnableKeyframeSeeking = true;
        public bool ShowStatusIndicator = true;
        public ReplayIndicatorVisibility StatusIndicatorVisibility = ReplayIndicatorVisibility.Always;
        public ReplayOverlayPosition StatusIndicatorPosition = ReplayOverlayPosition.BottomLeft;
        public bool ShowPlaybackTimeline = true;
        public ReplayOverlayPosition PlaybackTimelinePosition = ReplayOverlayPosition.BottomLeft;
        public bool ShowReplayChat = true;
        public bool ClearChatOnPlaybackStart = true;
        public float PlaybackThirdPersonCameraDistance = 4.25f;
        public float PlaybackFirstPersonFov = 90f;
        public bool EnableSlowMotionInterpolation = true;
        public bool EnableFirstPersonCameraSmoothing = true;
        public float FirstPersonCameraSmoothingSpeed = 18f;
        public float ManagerUiScale = 1f;
        public float ManagerWindowWidthPercent = 72f;
        public float ManagerWindowHeightPercent = 76f;
        public bool CaptureModeHidePlaybackControls = true;
        public bool CaptureModeHideReplayOverlays = true;
        public bool CaptureModeHideGameHud = true;
        public bool CaptureModeHideChat = true;
        public bool CaptureModeHideMinimap = true;
        public bool CaptureModeHidePlayerNames = true;

        public static ReplayModSettings Load()
        {
            bool legacyAutoRecord = PlayerPrefs.GetInt(Prefix + "AutoRecord", 1) == 1;
            ReplayRecordingMode loadedRecordingMode = PlayerPrefs.HasKey(Prefix + "RecordingMode")
                ? LoadEnum(Prefix + "RecordingMode", ReplayRecordingMode.AutomaticSave)
                : (legacyAutoRecord ? ReplayRecordingMode.AutomaticSave : ReplayRecordingMode.ManualOnly);
            bool splitRecordingsByGameEnd = PlayerPrefs.GetInt(Prefix + "SplitRecordingsByGameEnd", 0) == 1;
            bool legacyRequireSaveConfirmation = PlayerPrefs.GetInt(Prefix + "RequireSaveConfirmation", 0) == 1 && !splitRecordingsByGameEnd;
            bool saveOnDisconnect = PlayerPrefs.HasKey(Prefix + "SaveOnDisconnect")
                ? PlayerPrefs.GetInt(Prefix + "SaveOnDisconnect", 1) == 1
                : !legacyRequireSaveConfirmation;

            ReplayModSettings settings = new ReplayModSettings
            {
                RecordingMode = loadedRecordingMode,
                AutoRecord = loadedRecordingMode == ReplayRecordingMode.AutomaticSave,
                EnableManualRecordingHotkey = true,
                SaveOnDisconnect = saveOnDisconnect,
                SplitRecordingsByGameEnd = splitRecordingsByGameEnd,
                RecordOnlyDuringGames = PlayerPrefs.GetInt(Prefix + "RecordOnlyDuringGames", 1) == 1,
                EnableServerSideRecording = PlayerPrefs.GetInt(Prefix + "EnableServerSideRecording", 1) == 1,
                CaptureTickRate = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "CaptureTickRate", 30), 5, 120),
                MinimumPlayersToAutoRecord = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "MinimumPlayersToAutoRecord", 1), 1, 64),
                StorageLimitMb = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "StorageLimitMb", 2048)),
                MinimumReplayLengthSeconds = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "MinimumReplayLengthSeconds", 10)),
                EnableDebugProfiling = PlayerPrefs.GetInt(Prefix + "EnableDebugProfiling", 0) == 1,
                EnableKeyframeSeeking = PlayerPrefs.GetInt(Prefix + "EnableKeyframeSeeking", 1) == 1,
                StatusIndicatorVisibility = LoadEnum(Prefix + "StatusIndicatorVisibility", ReplayIndicatorVisibility.Always),
                StatusIndicatorPosition = LoadEnum(Prefix + "StatusIndicatorPosition", ReplayOverlayPosition.BottomLeft),
                PlaybackUiInputMode = LoadEnum(Prefix + "PlaybackUiInputMode", ReplayPlaybackUiInputMode.Toggle),
                ShowPlaybackTimeline = PlayerPrefs.GetInt(Prefix + "ShowPlaybackTimeline", 1) == 1,
                PlaybackTimelinePosition = LoadEnum(Prefix + "PlaybackTimelinePosition", ReplayOverlayPosition.BottomLeft),
                ShowReplayChat = PlayerPrefs.GetInt(Prefix + "ShowReplayChat", 1) == 1,
                ClearChatOnPlaybackStart = PlayerPrefs.GetInt(Prefix + "ClearChatOnPlaybackStart", 1) == 1,
                PlaybackThirdPersonCameraDistance = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "PlaybackThirdPersonCameraDistance", 4.25f), 1.5f, 12f),
                PlaybackFirstPersonFov = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "PlaybackFirstPersonFov", 90f), 60f, 120f),
                EnableSlowMotionInterpolation = PlayerPrefs.GetInt(Prefix + "EnableSlowMotionInterpolation", 1) == 1,
                EnableFirstPersonCameraSmoothing = PlayerPrefs.GetInt(Prefix + "EnableFirstPersonCameraSmoothing", 1) == 1,
                FirstPersonCameraSmoothingSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "FirstPersonCameraSmoothingSpeed", 18f), 1f, 60f),
                ManagerUiScale = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "ManagerUiScale", 1f), 0.85f, 1.3f),
                ManagerWindowWidthPercent = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "ManagerWindowWidthPercent", 72f), 58f, 94f),
                ManagerWindowHeightPercent = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "ManagerWindowHeightPercent", 76f), 58f, 92f),
                CaptureModeHidePlaybackControls = PlayerPrefs.GetInt(Prefix + "CaptureModeHidePlaybackControls", 1) == 1,
                CaptureModeHideReplayOverlays = PlayerPrefs.GetInt(Prefix + "CaptureModeHideReplayOverlays", 1) == 1,
                CaptureModeHideGameHud = PlayerPrefs.GetInt(Prefix + "CaptureModeHideGameHud", 1) == 1,
                CaptureModeHideChat = PlayerPrefs.GetInt(Prefix + "CaptureModeHideChat", 1) == 1,
                CaptureModeHideMinimap = PlayerPrefs.GetInt(Prefix + "CaptureModeHideMinimap", 1) == 1,
                CaptureModeHidePlayerNames = PlayerPrefs.GetInt(Prefix + "CaptureModeHidePlayerNames", 1) == 1
            };

            string manualRecordingKey = PlayerPrefs.GetString(Prefix + "ManualRecordingKey", KeyCode.F5.ToString());
            KeyCode manualParsed;
            if (Enum.TryParse(manualRecordingKey, true, out manualParsed))
            {
                settings.ManualRecordingKey = manualParsed;
            }

            string markerKey = PlayerPrefs.GetString(Prefix + "MarkerKey", KeyCode.F4.ToString());
            KeyCode parsed;
            if (Enum.TryParse(markerKey, true, out parsed))
            {
                settings.MarkerKey = parsed;
            }

            string toggleStatusBadgeKey = PlayerPrefs.GetString(Prefix + "ToggleStatusBadgeKey", KeyCode.F7.ToString());
            if (Enum.TryParse(toggleStatusBadgeKey, true, out parsed))
            {
                settings.ToggleStatusBadgeKey = parsed;
            }

            string toggleReplayTimeKey = PlayerPrefs.GetString(Prefix + "ToggleReplayTimeKey", KeyCode.F8.ToString());
            if (Enum.TryParse(toggleReplayTimeKey, true, out parsed))
            {
                settings.ToggleReplayTimeKey = parsed;
            }

            string playbackUiInputKey = PlayerPrefs.GetString(Prefix + "PlaybackUiInputKey", KeyCode.LeftAlt.ToString());
            if (Enum.TryParse(playbackUiInputKey, true, out parsed))
            {
                settings.PlaybackUiInputKey = parsed;
            }

            string captureModeKey = PlayerPrefs.GetString(Prefix + "CaptureModeKey", KeyCode.F10.ToString());
            if (Enum.TryParse(captureModeKey, true, out parsed))
            {
                settings.CaptureModeKey = parsed;
            }

            settings.ShowStatusIndicator = PlayerPrefs.GetInt(Prefix + "ShowStatusIndicator", 1) == 1;

            return settings;
        }

        public void Save()
        {
            this.AutoRecord = this.RecordingMode == ReplayRecordingMode.AutomaticSave;
            this.EnableManualRecordingHotkey = true;

            PlayerPrefs.SetString(Prefix + "RecordingMode", this.RecordingMode.ToString());
            PlayerPrefs.SetInt(Prefix + "AutoRecord", this.AutoRecord ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EnableManualRecordingHotkey", 1);
            PlayerPrefs.SetInt(Prefix + "SaveOnDisconnect", this.SaveOnDisconnect ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "SplitRecordingsByGameEnd", this.SplitRecordingsByGameEnd ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "RecordOnlyDuringGames", this.RecordOnlyDuringGames ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EnableServerSideRecording", this.EnableServerSideRecording ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureTickRate", Mathf.Clamp(this.CaptureTickRate, 5, 120));
            PlayerPrefs.SetInt(Prefix + "MinimumPlayersToAutoRecord", Mathf.Clamp(this.MinimumPlayersToAutoRecord, 1, 64));
            PlayerPrefs.SetInt(Prefix + "StorageLimitMb", Mathf.Max(0, this.StorageLimitMb));
            PlayerPrefs.SetInt(Prefix + "MinimumReplayLengthSeconds", Mathf.Max(0, this.MinimumReplayLengthSeconds));
            PlayerPrefs.SetString(Prefix + "ManualRecordingKey", this.ManualRecordingKey.ToString());
            PlayerPrefs.SetString(Prefix + "MarkerKey", this.MarkerKey.ToString());
            PlayerPrefs.SetString(Prefix + "ToggleStatusBadgeKey", this.ToggleStatusBadgeKey.ToString());
            PlayerPrefs.SetString(Prefix + "ToggleReplayTimeKey", this.ToggleReplayTimeKey.ToString());
            PlayerPrefs.SetString(Prefix + "PlaybackUiInputKey", this.PlaybackUiInputKey.ToString());
            PlayerPrefs.SetString(Prefix + "CaptureModeKey", this.CaptureModeKey.ToString());
            PlayerPrefs.SetString(Prefix + "PlaybackUiInputMode", this.PlaybackUiInputMode.ToString());
            PlayerPrefs.SetInt(Prefix + "EnableDebugProfiling", this.EnableDebugProfiling ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EnableKeyframeSeeking", this.EnableKeyframeSeeking ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ShowStatusIndicator", this.ShowStatusIndicator ? 1 : 0);
            PlayerPrefs.SetString(Prefix + "StatusIndicatorVisibility", this.StatusIndicatorVisibility.ToString());
            PlayerPrefs.SetString(Prefix + "StatusIndicatorPosition", this.StatusIndicatorPosition.ToString());
            PlayerPrefs.SetInt(Prefix + "ShowPlaybackTimeline", this.ShowPlaybackTimeline ? 1 : 0);
            PlayerPrefs.SetString(Prefix + "PlaybackTimelinePosition", this.PlaybackTimelinePosition.ToString());
            PlayerPrefs.SetInt(Prefix + "ShowReplayChat", this.ShowReplayChat ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ClearChatOnPlaybackStart", this.ClearChatOnPlaybackStart ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "PlaybackThirdPersonCameraDistance", Mathf.Clamp(this.PlaybackThirdPersonCameraDistance, 1.5f, 12f));
            PlayerPrefs.SetFloat(Prefix + "PlaybackFirstPersonFov", Mathf.Clamp(this.PlaybackFirstPersonFov, 60f, 120f));
            PlayerPrefs.SetInt(Prefix + "EnableSlowMotionInterpolation", this.EnableSlowMotionInterpolation ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EnableFirstPersonCameraSmoothing", this.EnableFirstPersonCameraSmoothing ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "FirstPersonCameraSmoothingSpeed", Mathf.Clamp(this.FirstPersonCameraSmoothingSpeed, 1f, 60f));
            PlayerPrefs.SetFloat(Prefix + "ManagerUiScale", Mathf.Clamp(this.ManagerUiScale, 0.85f, 1.3f));
            PlayerPrefs.SetFloat(Prefix + "ManagerWindowWidthPercent", Mathf.Clamp(this.ManagerWindowWidthPercent, 58f, 94f));
            PlayerPrefs.SetFloat(Prefix + "ManagerWindowHeightPercent", Mathf.Clamp(this.ManagerWindowHeightPercent, 58f, 92f));
            PlayerPrefs.SetInt(Prefix + "CaptureModeHidePlaybackControls", this.CaptureModeHidePlaybackControls ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureModeHideReplayOverlays", this.CaptureModeHideReplayOverlays ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureModeHideGameHud", this.CaptureModeHideGameHud ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureModeHideChat", this.CaptureModeHideChat ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureModeHideMinimap", this.CaptureModeHideMinimap ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "CaptureModeHidePlayerNames", this.CaptureModeHidePlayerNames ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static T LoadEnum<T>(string key, T fallback) where T : struct
        {
            string value = PlayerPrefs.GetString(key, fallback.ToString());
            T parsed;
            if (Enum.TryParse(value, true, out parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}

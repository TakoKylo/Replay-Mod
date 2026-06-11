using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuckReplayMod
{
    internal static class ReplayRecordingSettingsSection
    {
        public static void Create(ReplayModUiService ui, VisualElement parent)
        {
            parent.Add(ReplayUiTools.CreateSectionTitle("Recording"));
            parent.Add(ReplayUiTools.CreateNote("Choose what Replay Mod does by itself. Manual start/stop is always available from the Hotkeys section and can be used as an override while you are in a match."));

            parent.Add(ReplayUiTools.CreateHeader("Recording Mode"));

            parent.Add(ReplayUiTools.CreateDropdownRow(
                "Recording mode",
                "Automatic starts recording when the automatic rules are met. Manual only records only when you press the manual recording hotkey. Changing this does not stop an active recording.",
                FormatRecordingMode(ui.Settings.RecordingMode),
                GetRecordingModeChoices(),
                delegate(string value)
            {
                ui.Settings.RecordingMode = ParseRecordingMode(value);
                ui.Settings.AutoRecord = ui.Settings.RecordingMode == ReplayRecordingMode.AutomaticSave;
                ui.SaveSettings();
                ui.RefreshLibraryText();
                ui.RefreshStatusIndicator();
            }));

            parent.Add(ReplayUiTools.CreateSeparator());
            parent.Add(ReplayUiTools.CreateHeader("Save Behavior"));

            parent.Add(CreateToggleRow(
                "Save on disconnect",
                "When enabled, Replay Mod saves the current recording when you leave a server or the client stops. Turn this off if you only want to keep recordings you manually stop or mark to save with the recording hotkey.",
                ui.Settings.SaveOnDisconnect,
                out _,
                delegate(bool value)
            {
                ui.Settings.SaveOnDisconnect = value;
                ui.SaveSettings();
                ui.RefreshStatusIndicator();
            }));

            parent.Add(CreateToggleRow(
                "Save when game ends",
                "Saves the current replay file when a game ends. In Automatic mode, Replay Mod starts a new recording if the automatic rules are still met. In Manual only mode, the recording saves and stops.",
                ui.Settings.SplitRecordingsByGameEnd,
                out _,
                delegate(bool value)
            {
                ui.Settings.SplitRecordingsByGameEnd = value;
                ui.SaveSettings();
                ui.RefreshStatusIndicator();
            }));

            parent.Add(ReplayUiTools.CreateSeparator());
            parent.Add(ReplayUiTools.CreateHeader("Automatic Rules"));

            parent.Add(CreateToggleRow(
                "Only record during games",
                "When enabled, automatic recording waits for a match to be underway instead of starting during warmup or pre-game. Once started it records continuously, including intermissions. Turn this off to also record warmup. Manual recording ignores this rule.",
                ui.Settings.RecordOnlyDuringGames,
                out _,
                delegate(bool value)
            {
                ui.Settings.RecordOnlyDuringGames = value;
                ui.SaveSettings();
                ui.RefreshStatusIndicator();
            }));

            parent.Add(ReplayUiTools.CreateIntegerRow(
                "Minimum players to record",
                "Automatic recording starts once at least this many non-replay players are in the server. It does not stop if the player count later drops. Manual recording ignores this limit.",
                ui.Settings.MinimumPlayersToAutoRecord,
                delegate(int value)
            {
                ui.Settings.MinimumPlayersToAutoRecord = Mathf.Clamp(value, 1, 64);
                ui.SaveSettings();
            }));

            parent.Add(ReplayUiTools.CreateSeparator());
            parent.Add(ReplayUiTools.CreateHeader("Quality and File Size"));

            parent.Add(ReplayUiTools.CreateDropdownRow(
                "Record rate",
                GetCaptureRateTooltip(ui.Settings.CaptureTickRate),
                FormatCaptureRate(ui.Settings.CaptureTickRate),
                GetCaptureRateChoices(),
                delegate(string value)
            {
                int parsed = ParseCaptureRate(value);
                if (parsed <= 0)
                {
                    return;
                }

                if (ui.Recorder.IsRecording)
                {
                    ReplayModLog.Warning("Capture rate changes apply after the current recording stops.");
                    return;
                }

                ui.Settings.CaptureTickRate = Mathf.Clamp(parsed, 5, 120);
                ui.SaveSettings();
            }));
        }

        private static VisualElement CreateToggleRow(string labelText, string tooltip, bool value, out Toggle toggle, Action<bool> onChanged)
        {
            VisualElement row = ReplayUiTools.CreateConfigurationRow();
            ReplayUiTools.AttachHoverTooltip(row, tooltip);
            row.Add(ReplayUiTools.CreateConfigurationLabel(labelText, tooltip));
            toggle = ReplayUiTools.CreateToggle(value, onChanged);
            toggle.tooltip = tooltip ?? string.Empty;
            row.Add(toggle);
            return row;
        }

        private static List<string> GetRecordingModeChoices()
        {
            return new List<string>
            {
                "Automatic",
                "Manual only"
            };
        }

        private static string FormatRecordingMode(ReplayRecordingMode mode)
        {
            switch (mode)
            {
                case ReplayRecordingMode.ManualOnly:
                    return "Manual only";
                default:
                    return "Automatic";
            }
        }

        private static ReplayRecordingMode ParseRecordingMode(string value)
        {
            switch (value)
            {
                case "Manual only":
                    return ReplayRecordingMode.ManualOnly;
                case "Automatic save":
                    return ReplayRecordingMode.AutomaticSave;
                default:
                    return ReplayRecordingMode.AutomaticSave;
            }
        }

        private static List<string> GetCaptureRateChoices()
        {
            return new List<string>
            {
                "15 Hz - smaller files",
                "30 Hz - standard",
                "60 Hz - smoother"
            };
        }

        internal static string FormatCaptureRate(int tickRate)
        {
            if (tickRate <= 15)
            {
                return "15 Hz - smaller files";
            }

            if (tickRate >= 60)
            {
                return "60 Hz - smoother";
            }

            return "30 Hz - standard";
        }

        private static int ParseCaptureRate(string value)
        {
            switch (value)
            {
                case "15 Hz - smaller files":
                case "Low (smaller files)":
                    return 15;
                case "60 Hz - smoother":
                case "High (smoother)":
                    return 60;
                default:
                    return 30;
            }
        }

        private static string GetCaptureRateTooltip(int tickRate)
        {
            int normalizedRate = ParseCaptureRate(FormatCaptureRate(tickRate));
            return "Current: " + normalizedRate + " transform samples per second. " +
                "Estimated size for a typical match: 15 Hz ~" + EstimateMegabytesPerHour(15).ToString("0") +
                " MB/hour, 30 Hz ~" + EstimateMegabytesPerHour(30).ToString("0") +
                " MB/hour, 60 Hz ~" + EstimateMegabytesPerHour(60).ToString("0") +
                " MB/hour. Actual size depends on player count, puck count, and activity.";
        }

        private static float EstimateMegabytesPerHour(int tickRate)
        {
            return Mathf.Max(1, tickRate) * 2.9f;
        }

        internal static List<string> GetFunctionKeyChoices()
        {
            return new List<string>
            {
                "F1",
                "F2",
                "F3",
                "F4",
                "F5",
                "F6",
                "F7",
                "F8",
                "F9",
                "F10",
                "F11",
                "F12"
            };
        }
    }
}

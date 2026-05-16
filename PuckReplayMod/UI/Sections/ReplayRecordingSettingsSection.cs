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
            parent.Add(ReplayUiTools.CreateNote("Control when replay files are created. The default settings are a good fit for most players."));

            parent.Add(ReplayUiTools.CreateToggleRow(
                "Record games automatically",
                "Starts recording when you join a match and saves when you leave.",
                ui.Settings.AutoRecord,
                delegate(bool value)
            {
                ui.Settings.AutoRecord = value;
                if (!value && ui.Recorder.IsRecording)
                {
                    ui.Recorder.StopRecording(true, "auto-record disabled");
                }

                ui.SaveSettings();
            }));

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

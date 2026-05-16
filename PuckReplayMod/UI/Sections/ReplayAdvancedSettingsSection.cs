using UnityEngine.UIElements;

namespace PuckReplayMod
{
    internal static class ReplayAdvancedSettingsSection
    {
        public static void Create(ReplayModUiService ui, VisualElement parent)
        {
            parent.Add(ReplayUiTools.CreateSectionTitle("Advanced"));
            parent.Add(ReplayUiTools.CreateNote("Extra options for troubleshooting."));

            parent.Add(ReplayUiTools.CreateToggleRow("Detailed logging", ui.Settings.EnableDebugProfiling, delegate(bool value)
            {
                ui.Settings.EnableDebugProfiling = value;
                ui.SaveSettings();
            }));
        }
    }
}

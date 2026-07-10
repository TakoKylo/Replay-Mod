using System;
using HarmonyLib;
using UnityEngine;

namespace PuckReplayMod
{
    public class PuckReplayModInit : IPuckPlugin
    {
        private static readonly Harmony Harmony = new Harmony(ReplayModConstants.ModGuid);

        public bool OnEnable()
        {
            try
            {
                ReplayModLog.Info("Enabling...");
                ReplayModController.Create();

                // A dedicated (headless) server only records, which is entirely event-driven; every
                // Harmony patch this mod defines is playback- or UI-only and stays inert there (its
                // guard always runs the original). Skipping PatchAll on a dedicated server therefore
                // changes no behavior while removing detour overhead from hot methods (EventManager
                // .TriggerEvent, per-tick *.FixedUpdate) and dropping our patch-conflict surface with
                // other server mods to zero. Playback/UI still fully patch on client / player-hosted.
                if (ApplicationManager.IsDedicatedGameServer)
                {
                    ReplayModLog.Info("Dedicated server detected; skipping playback/UI Harmony patches (recording is event-driven).");
                }
                else
                {
                    Harmony.PatchAll();
                }

                ReplayModLog.Info("Enabled.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        public bool OnDisable()
        {
            try
            {
                ReplayModLog.Info("Disabling...");
                Harmony.UnpatchSelf();
                ReplayModController.DestroyInstance();
                ReplayModLog.Info("Disabled.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}

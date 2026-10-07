using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PeakOverheadStats
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log; internal static PluginConfig PluginConfig;

        private void Awake()
        {
            Log = Logger;
            PluginConfig = new PluginConfig(Config);

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded");
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.yls.peakoverheadstats";
        public const string PLUGIN_NAME = "PeakOverheadStats";
        public const string PLUGIN_VERSION = "1.0.2";
    }
}

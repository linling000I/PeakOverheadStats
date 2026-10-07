using BepInEx.Configuration;

namespace PeakOverheadStats
{
    public class PluginConfig
    {
        public ConfigEntry<bool> DisplayTeammateStaminaBars { get; }
        public ConfigEntry<float> TeammateStaminaBarProximity { get; }
        public ConfigEntry<int> TeammateStaminaBarLimit { get; }
        public ConfigEntry<float> TeammateStaminaBarScale { get; }
        public ConfigEntry<bool> ShowInventorySlots { get; }
        public ConfigEntry<bool> ShowStaminaInfo { get; }
        public ConfigEntry<bool> DisplayTimer { get; }
        public ConfigEntry<bool> DisplayHeight { get; }
        public ConfigEntry<bool> DisplayLevel { get; }
        public ConfigEntry<bool> DisplayBiomes { get; }
        public ConfigEntry<bool> DisplayDayNightCountdown { get; }
        public ConfigEntry<bool> DisplayFogStats { get; }
        public ConfigEntry<bool> DisplayLavaStats { get; }
        public ConfigEntry<bool> DisplayMapSeed { get; }

        public PluginConfig(ConfigFile config)
        {
            DisplayTeammateStaminaBars = config.Bind("General", "Display Teammate Stamina Bars", true, "Show overhead teammate stamina bars");
            TeammateStaminaBarProximity = config.Bind("General", "Teammate Stamina Bar Proximity", 30f, "Max distance to see teammate bars");
            TeammateStaminaBarLimit = config.Bind("General", "Teammate Stamina Bar Limit", 4, "Max visible teammate bars");
            TeammateStaminaBarScale = config.Bind("General", "Teammate Stamina Bar Scale", 0.72f, "Bar scale multiplier");
            ShowInventorySlots = config.Bind("General", "Show Inventory Slots", true, "Show teammate inventory slots");
            ShowStaminaInfo = config.Bind("General", "Show Stamina Info", true, "Show numeric values on stamina bars");
            DisplayTimer = config.Bind("Stats", "Display Timer", true, "Show climb timer");
            DisplayHeight = config.Bind("Stats", "Display Height", true, "Show height/distance");
            DisplayLevel = config.Bind("Stats", "Display Level", true, "Show current level");
            DisplayBiomes = config.Bind("Stats", "Display Biomes", true, "Show biome info");
            DisplayDayNightCountdown = config.Bind("Stats", "Display Day Night Countdown", true, "Show day/night countdown");
            DisplayFogStats = config.Bind("Stats", "Display Fog Stats", true, "Show fog stats");
            DisplayLavaStats = config.Bind("Stats", "Display Lava Stats", true, "Show lava/kiln stats");
            DisplayMapSeed = config.Bind("Stats", "Display Map Seed", true, "Show map seed");
        }
    }
}

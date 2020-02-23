using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region Fixed configuration
        const int NUMBER_OF_TENTICKS_BETWEEN_EACH_UPDATE = 10;
        const string CUSTOMDATA_INGOT_STATUS_CONFIG_PREFIX = "Ingot Status:";
        // We multiply the ConversionRatio by 2.0 since we use max efficiency refinery mods on our refineries -- otherwise this is a pain in the ass to calculate
        const float REFINERY_EFFICIENCY = 2.0f;

        struct IngotConfig
        {
            // The "Type" passed to MyItemType.MakeIngot and MyItemType.MakeOre
            public string Type;
            // If we have less than this, consider us at the "warning" level for this type
            public int WarningThreshold;
            // If we have less than this, consider us at the "alert" level for this type
            public int AlertThreshold;
            // What is the conversion ratio for ore-to-ingot?
            public float ConversionRatio;
        }

        static readonly IngotConfig[] INGOT_TYPES =
        {
            new IngotConfig { Type = "Iron",        WarningThreshold = 10000,   AlertThreshold = 1000,  ConversionRatio = 0.7f, },
            new IngotConfig { Type = "Silicon",     WarningThreshold = 10000,   AlertThreshold = 1000,  ConversionRatio = 0.7f, },
            new IngotConfig { Type = "Nickel",      WarningThreshold = 6000,    AlertThreshold = 600,   ConversionRatio = 0.4f, },
            new IngotConfig { Type = "Cobalt",      WarningThreshold = 5000,    AlertThreshold = 500,   ConversionRatio = 0.3f, },
            new IngotConfig { Type = "Silver",      WarningThreshold = 3000,    AlertThreshold = 300,   ConversionRatio = 0.1f, },
            new IngotConfig { Type = "Gold",        WarningThreshold = 2000,    AlertThreshold = 200,   ConversionRatio = 0.01f, },
            new IngotConfig { Type = "Uranium",     WarningThreshold = 1,       AlertThreshold = 0,     ConversionRatio = 0.01f, },
            new IngotConfig { Type = "Magnesium",   WarningThreshold = 1000,    AlertThreshold = 100,   ConversionRatio = 0.007f, },
            new IngotConfig { Type = "Platinum",    WarningThreshold = 1,       AlertThreshold = 0,     ConversionRatio = 0.005f, },
        };

        static readonly Color WARNING_FONT_COLOR = Color.Black;
        static readonly Color ALERT_FONT_COLOR = Color.Black;
        static readonly Color WARNING_BACKGROUND_COLOR = Color.DarkOrange;
        static readonly Color ALERT_BACKGROUND_COLOR = Color.Red;
        #endregion

        // A mapping of ingot type ("Iron", "Silicon", etc) to the index in the INGOT_TYPES array.
        private readonly Dictionary<string, int> ingotLookup = new Dictionary<string, int>(INGOT_TYPES.Length);
        // Reused List for all blocks with inventories
        private readonly List<IMyTerminalBlock> inventoryBlocks = new List<IMyTerminalBlock>();
        // Reused StringBuilder for setting status for each ingot type this tick
        private readonly StringBuilder displayBuilder = new StringBuilder();
        // Reused array for the counts of each ingot type this tick
        private readonly MyFixedPoint[] ingotCounts = new MyFixedPoint[INGOT_TYPES.Length];
        // Rused array for the counts of each ingot type's ore this tick
        private readonly MyFixedPoint[] oreCounts = new MyFixedPoint[INGOT_TYPES.Length];

        // A mapping from index in INGOT_TYPES to the text surfaces we're updating with a status, updated on first run
        private readonly List<IMyTextSurface>[] textSurfaces = new List<IMyTextSurface>[INGOT_TYPES.Length];
        // Cached list of inventories we're currently considering (updated every NUMBER_OF_TENTICKS_BETWEEN_EACH_UPDATE ten-ticks)
        private readonly List<IMyInventory> inventories = new List<IMyInventory>();
        // All the sound blocks we activate when we're in the alert state, updated on first run
        private readonly List<IMySoundBlock> alertSoundBlocks = new List<IMySoundBlock>();
        // All the light blocks we activate when we're in the alert or warning states, updated on the first run
        private readonly List<IMyLightingBlock> alertOrWarningLights = new List<IMyLightingBlock>();
        // How many Update10 ticks until we update the cached list of inventories 
        private int tenticksUntilNextUpdate = 0;

        enum IngotStatus
        {
            Alert,
            Warning,
            Normal,
        };
        
        // What status was the worst ingot type last tick?
        private IngotStatus previousIngotStatus = IngotStatus.Normal;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update10;

            for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
            {
                textSurfaces[ingotIndex] = new List<IMyTextSurface>();
                ingotLookup[INGOT_TYPES[ingotIndex].Type] = ingotIndex;
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            #region Initial setup
            // Do some initial setup on the first call
            if (updateSource.HasFlag(UpdateType.Once))
            {
                Me.GetSurface(0).WriteText("Storage Overview");

                // Enumerate all the LCD panels and find the ones that are configured for ingots
                var textSurfaceProviders = new List<IMyTextSurfaceProvider>();
                GridTerminalSystem.GetBlocksOfType(textSurfaceProviders);
                foreach (var textSurfaceProvider in textSurfaceProviders)
                {
                    string configData = ((IMyTerminalBlock)textSurfaceProvider).CustomData;
                    if (configData.StartsWith(CUSTOMDATA_INGOT_STATUS_CONFIG_PREFIX))
                    {
                        string ingotType = configData.Substring(CUSTOMDATA_INGOT_STATUS_CONFIG_PREFIX.Length);
                        int ingotIndex;
                        if (ingotLookup.TryGetValue(ingotType, out ingotIndex))
                        {
                            for (var surfaceIndex = 0; surfaceIndex < textSurfaceProvider.SurfaceCount; ++surfaceIndex)
                            {
                                var textSurface = textSurfaceProvider.GetSurface(surfaceIndex);
                                textSurface.Alignment = TextAlignment.CENTER;
                                textSurface.Font = "Monospace";
                                textSurface.FontSize = 2.4f;
                                textSurface.TextPadding = 8.0f;
                                textSurface.WriteText($"{ingotType}\nInitializing...");
                                textSurfaces[ingotIndex].Add(textSurface);
                            }
                        }
                    }
                }

                // Find all the lights and sound blocks configured for ingot
                GridTerminalSystem.GetBlocksOfType(alertSoundBlocks, soundBlock => soundBlock.CustomData.StartsWith(CUSTOMDATA_INGOT_STATUS_CONFIG_PREFIX));
                GridTerminalSystem.GetBlocksOfType(alertOrWarningLights, interiorLight => interiorLight.CustomData.StartsWith(CUSTOMDATA_INGOT_STATUS_CONFIG_PREFIX));
            }
            #endregion

            #region Update list of inventories
            // Only update the list of inventories we care about 
            if (tenticksUntilNextUpdate == 0)
            {
                inventories.Clear();
                GridTerminalSystem.GetBlocksOfType(inventoryBlocks, otherBlock => otherBlock.IsSameConstructAs(Me) && otherBlock.HasInventory);
                foreach (var inventoryBlock in inventoryBlocks)
                {
                    for (var inventoryIndex = 0; inventoryIndex < inventoryBlock.InventoryCount; ++inventoryIndex)
                    {
                        inventories.Add(inventoryBlock.GetInventory(inventoryIndex));
                    }
                }
                tenticksUntilNextUpdate = NUMBER_OF_TENTICKS_BETWEEN_EACH_UPDATE - 1;
                return;
            }
            #endregion

            tenticksUntilNextUpdate--;

            // Count the ingots & ores of each ingot type
            foreach (var inventory in inventories)
            {
                for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
                {
                    ingotCounts[ingotIndex] += inventory.GetItemAmount(MyItemType.MakeIngot(INGOT_TYPES[ingotIndex].Type));
                    oreCounts[ingotIndex] += inventory.GetItemAmount(MyItemType.MakeOre(INGOT_TYPES[ingotIndex].Type));
                }
            }

            #region Update each ingot type
            // Update the status of each ingot type
            var anyAlerts = false;
            var anyWarnings = false;
            for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
            {
                var ingotConfig = INGOT_TYPES[ingotIndex];
                var ingotCount = ingotCounts[ingotIndex].ToIntSafe();
                ingotCounts[ingotIndex].RawValue = 0;
                var oreCount = oreCounts[ingotIndex].ToIntSafe();
                oreCounts[ingotIndex].RawValue = 0;
                
                // Generate the text for the display
                displayBuilder.Clear();
                displayBuilder.AppendLine(ingotConfig.Type);
                displayBuilder.AppendLine();
                displayBuilder.AppendLine(ingotCount.ToString("N0"));
                displayBuilder.AppendLine("available");
                if ((oreCount * ingotConfig.ConversionRatio) >= 1)
                {
                    displayBuilder.AppendLine((oreCount * ingotConfig.ConversionRatio * REFINERY_EFFICIENCY).ToString("N0"));
                    displayBuilder.AppendLine("processing");
                }

                // Determine if there are any alerts or warnings, and pick the right font & background color
                var fontColor = Color.White;
                var backgroundColor = Color.Black;
                if (ingotCount < ingotConfig.AlertThreshold)
                {
                    anyAlerts = true;
                    fontColor = ALERT_FONT_COLOR;
                    backgroundColor = ALERT_BACKGROUND_COLOR;
                }
                else if (ingotCount < ingotConfig.WarningThreshold)
                {
                    anyWarnings = true;
                    fontColor = WARNING_FONT_COLOR;
                    backgroundColor = WARNING_BACKGROUND_COLOR;
                }

                foreach (var textSurface in textSurfaces[ingotIndex])
                {
                    textSurface.FontColor = fontColor;
                    textSurface.BackgroundColor = backgroundColor;
                    textSurface.WriteText(displayBuilder);
                }
            }
            #endregion

            #region Update global warning / alert systems
            // See if the worst status has changed and updated sound blocks & lights
            IngotStatus newIngotStatus = IngotStatus.Normal;
            if (anyAlerts)
            {
                newIngotStatus = IngotStatus.Alert;
            }
            else if (anyWarnings)
            {
                newIngotStatus = IngotStatus.Warning;
            }

            if (previousIngotStatus != newIngotStatus)
            {
                previousIngotStatus = newIngotStatus;
                bool playSound = false;
                Color? optionalLightColor = null;
                
                switch (newIngotStatus)
                {
                    case IngotStatus.Alert:
                        playSound = true;
                        optionalLightColor = ALERT_BACKGROUND_COLOR;
                        break;
                    case IngotStatus.Warning:
                        optionalLightColor = WARNING_BACKGROUND_COLOR;
                        break;
                }

                foreach (var soundBlock in alertSoundBlocks)
                {
                    if (playSound)
                    {
                        soundBlock.Play();
                    }
                    else
					{
                        soundBlock.Stop();
                    }
                }

                if (optionalLightColor.HasValue)
                {
                    Color lightColor = optionalLightColor.Value;
                    foreach (var interiorLight in alertOrWarningLights)
                    {
                        interiorLight.Enabled = true;
                        interiorLight.Color = lightColor;
                    }
                }
                else
                {
                    foreach (var interiorLight in alertOrWarningLights)
                    {
                        interiorLight.Enabled = false;
                    }
                }
            }
            #endregion
        }
    }
}

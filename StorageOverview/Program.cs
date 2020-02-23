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
        const int NUMBER_OF_TENTICKS_BETWEEN_EACH_UPDATE = 10;
        const string CUSTOMDATA_INGOT_STATUS_CONFIG_PREFIX = "Ingot Status:";

        struct IngotConfig
        {
            public string Type;
            public int WarningThreshold;
            public int AlertThreshold;
        }

        static readonly IngotConfig[] INGOT_TYPES =
        {
            new IngotConfig { Type = "Iron", WarningThreshold = 10000, AlertThreshold = 1000 },
            new IngotConfig { Type = "Nickel", WarningThreshold = 10000, AlertThreshold = 1000 },
            new IngotConfig { Type = "Silicon", WarningThreshold = 10000, AlertThreshold = 1000 },
            new IngotConfig { Type = "Silver", WarningThreshold = 5000, AlertThreshold = 500 },
            new IngotConfig { Type = "Gold", WarningThreshold = 5000, AlertThreshold = 500 },
            new IngotConfig { Type = "Magnesium", WarningThreshold = 5000, AlertThreshold = 500 },
            new IngotConfig { Type = "Cobalt", WarningThreshold = 10000, AlertThreshold = 1000 },
            new IngotConfig { Type = "Platinum", WarningThreshold = 1, AlertThreshold = 0 },
            new IngotConfig { Type = "Uranium", WarningThreshold = 1, AlertThreshold = 0 },
        };

        static readonly Color WARNING_FONT_COLOR = Color.Black;
        static readonly Color ALERT_FONT_COLOR = Color.Black;
        static readonly Color WARNING_BACKGROUND_COLOR = Color.DarkOrange;
        static readonly Color ALERT_BACKGROUND_COLOR = Color.Red;

        private readonly Dictionary<string, int> ingotLookup = new Dictionary<string, int>(INGOT_TYPES.Length);
        private readonly List<IMyTextSurface>[] textSurfaces = new List<IMyTextSurface>[INGOT_TYPES.Length];
        private readonly List<IMyTerminalBlock> inventoryBlocks = new List<IMyTerminalBlock>();
        private readonly List<IMyInventory> inventories = new List<IMyInventory>();
        private readonly StringBuilder displayBuilder = new StringBuilder();
        private readonly MyFixedPoint[] ingotCounts = new MyFixedPoint[INGOT_TYPES.Length];
        private readonly List<IMySoundBlock> alertSoundBlocks = new List<IMySoundBlock>();
        private readonly List<IMyLightingBlock> alertOrWarningLights = new List<IMyLightingBlock>();

        private int ticksUntilNextUpdate = 0;

        enum IngotStatus
        {
            Alert,
            Warning,
            Normal,
        };

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
                                textSurface.FontSize = 2.75f;
                                textSurface.TextPadding = 15.5f;
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

            if (ticksUntilNextUpdate == 0)
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
                ticksUntilNextUpdate = NUMBER_OF_TENTICKS_BETWEEN_EACH_UPDATE;
            }
            ticksUntilNextUpdate--;

            foreach (var inventory in inventories)
            {
                for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
                {
                    ingotCounts[ingotIndex] += inventory.GetItemAmount(MyItemType.MakeIngot(INGOT_TYPES[ingotIndex].Type));
                }
            }

            var anyAlerts = false;
            var anyWarnings = false;
            for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
            {
                var ingotConfig = INGOT_TYPES[ingotIndex];
                var ingotCount = ingotCounts[ingotIndex].ToIntSafe();
                ingotCounts[ingotIndex].RawValue = 0;

                displayBuilder.Clear();
                displayBuilder.AppendLine(ingotConfig.Type);
                displayBuilder.AppendLine();
                displayBuilder.AppendLine(ingotCount.ToString("N0"));
                displayBuilder.AppendLine("ingots");

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
                switch (newIngotStatus)
                {
                    case IngotStatus.Alert:
                        foreach (var soundBlock in alertSoundBlocks)
                        {
                            soundBlock.Play();
                        }
                        foreach (var interiorLight in alertOrWarningLights)
                        {
                            interiorLight.Enabled = true;
                            interiorLight.Color = ALERT_BACKGROUND_COLOR;
                        }
                        break;
                    case IngotStatus.Warning:
                        foreach (var soundBlock in alertSoundBlocks)
                        {
                            soundBlock.Stop();
                        }
                        foreach (var interiorLight in alertOrWarningLights)
                        {
                            interiorLight.Enabled = true;
                            interiorLight.Color = WARNING_BACKGROUND_COLOR;
                        }
                        break;
                    case IngotStatus.Normal:
                        foreach (var soundBlock in alertSoundBlocks)
                        {
                            soundBlock.Stop();
                        }
                        foreach (var interiorLight in alertOrWarningLights)
                        {
                            interiorLight.Enabled = false;
                        }
                        break;
                }
            }
        }
    }
}

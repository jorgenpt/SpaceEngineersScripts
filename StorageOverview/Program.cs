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
        private IMyTextSurfaceProvider textSurface;
        private List<IMyCargoContainer> containerBlocks = new List<IMyCargoContainer>();
        private List<IMyInventory> containerInventories = new List<IMyInventory>();
        private MyFixedPoint[] ingotCounts = new MyFixedPoint[INGOT_TYPES.Length];
        private StringBuilder displayBuilder = new StringBuilder();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update10 | UpdateFrequency.Update100;
        }

        static readonly string[] INGOT_TYPES = {
            "Iron",
            "Nickel",
            "Silicon",
            "Silver",
            "Gold",
            "Magnesium",
            "Cobalt",
            "Platinum",
            "Uranium",
        };

        public void Main(string argument, UpdateType updateSource)
        {
            bool updateContainers = updateSource.HasFlag(UpdateType.Update100);
            if (updateSource.HasFlag(UpdateType.Once))
            {
                // TODO: Look up a text surface?
                textSurface = Me;
                updateContainers = true;
            }

            if (updateContainers)
            {
                containerInventories.Clear();
                GridTerminalSystem.GetBlocksOfType(containerBlocks, containerBlock => containerBlock.IsSameConstructAs(Me));
                foreach (var containerBlock in containerBlocks)
                {
                    containerInventories.Add(containerBlock.GetInventory());
                }
            }

            foreach (var inventory in containerInventories)
            {
                for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
                {
                    ingotCounts[ingotIndex] += inventory.GetItemAmount(MyItemType.MakeIngot(INGOT_TYPES[ingotIndex]));
                }
            }

            var numIngots = 0;
            displayBuilder.Clear();
            displayBuilder.AppendLine("Ingot inventory:");
            for (var ingotIndex = 0; ingotIndex < INGOT_TYPES.Length; ++ingotIndex)
            {
                var ingotCount = ingotCounts[ingotIndex].ToIntSafe();
                if (ingotCount > 0)
                {
                    ++numIngots;
                    displayBuilder.AppendLine($"{ingotCount,7} {INGOT_TYPES[ingotIndex]}");
                }

                ingotCounts[ingotIndex].RawValue = 0;
            }

            if (numIngots == 0)
            {
                displayBuilder.AppendLine();
                displayBuilder.AppendLine("EMPTY!");
            }

            textSurface.GetSurface(0).WriteText(displayBuilder);
        }
    }
}

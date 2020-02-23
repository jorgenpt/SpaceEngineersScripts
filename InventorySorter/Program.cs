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
        const string CUSTOMDATA_CONFIG_PREFIX = "Inventory:";

        private IEnumerator<bool> Coroutine = null;
        private List<IMyInventory> OreInventories, ComponentInventories;

        private List<IMyAssembler> Assemblers = new List<IMyAssembler>();
        private List<IMyRefinery> Refineries = new List<IMyRefinery>();
        private List<IMyConveyorSorter> Conveyors = new List<IMyConveyorSorter>();
        private List<IMyShipConnector> Connectors = new List<IMyShipConnector>();
        private List<IMyCargoContainer> Containers = new List<IMyCargoContainer>();
        private List<MyInventoryItem> Items = new List<MyInventoryItem>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            Coroutine = RunProcess();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (OreInventories == null || ComponentInventories == null)
            {
                OreInventories = new List<IMyInventory>();
                ComponentInventories = new List<IMyInventory>();

                GridTerminalSystem.GetBlocksOfType(Containers, container => container.CustomData.StartsWith(CUSTOMDATA_CONFIG_PREFIX) && container.IsSameConstructAs(Me));

                bool encounteredError = false;
                foreach (var container in Containers)
                {
                    var inventoryType = container.CustomData.Substring(CUSTOMDATA_CONFIG_PREFIX.Length);
                    if (inventoryType == "Ores")
                    {
                        OreInventories.Add(container.GetInventory());
                    }
                    else if (inventoryType == "Components")
                    {
                        ComponentInventories.Add(container.GetInventory());
                    }
                    else
                    {
                        Echo($"Invalid inventory type on container {inventoryType}");
                        encounteredError = true;
                    }
                }

                if (OreInventories.Count == 0)
                {
                    Echo("Could not find any ore inventories");
                    encounteredError = true;
                }

                if (ComponentInventories.Count == 0)
                {
                    Echo("Could not find any component inventories");
                    encounteredError = true;
                }

                if (encounteredError)
                {
                    OreInventories = ComponentInventories = null;
                    Coroutine.Dispose();
                    Coroutine = null;
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    return;
                }
            }

            if (!Coroutine.MoveNext())
            {
                Coroutine.Dispose();
                Coroutine = RunProcess();
            }
        }

        private IEnumerator<bool> RunProcess()
        {
            var numIters = 0;
            GridTerminalSystem.GetBlocksOfType(Assemblers, block => block.IsSameConstructAs(Me));
            foreach (var assembler in Assemblers)
            {
                using (var transferEnumerator = TransferItems(assembler.OutputInventory))
                {
                    while (transferEnumerator.MoveNext())
                    {
                        if ((++numIters % 10) == 0)
                        {
                            yield return true;
                        }
                    }
                }
            }
            yield return true;

            GridTerminalSystem.GetBlocksOfType(Refineries, block => block.IsSameConstructAs(Me));
            foreach (var refinery in Refineries)
            {
                using (var transferEnumerator = TransferItems(refinery.OutputInventory))
                {
                    while (transferEnumerator.MoveNext())
                    {
                        if ((++numIters % 10) == 0)
                        {
                            yield return true;
                        }
                    }
                }
            }
            yield return true;

            GridTerminalSystem.GetBlocksOfType(Conveyors, block => block.IsSameConstructAs(Me));
            foreach (var conveyor in Conveyors)
            {
                using (var transferEnumerator = TransferItems(conveyor.GetInventory()))
                {
                    while (transferEnumerator.MoveNext())
                    {
                        if ((++numIters % 10) == 0)
                        {
                            yield return true;
                        }
                    }
                }
            }
            yield return true;

            GridTerminalSystem.GetBlocksOfType(Connectors, block => block.IsSameConstructAs(Me));
            foreach (var connector in Connectors)
            {
                using (var transferEnumerator = TransferItems(connector.GetInventory()))
                {
                    while (transferEnumerator.MoveNext())
                    {
                        if ((++numIters % 10) == 0)
                        {
                            yield return true;
                        }
                    }
                }
            }
            yield return true;

            GridTerminalSystem.GetBlocksOfType(Containers, container => container.IsSameConstructAs(Me));
            foreach (var container in Containers)
            {
                using (var transferEnumerator = TransferItems(container.GetInventory()))
                {
                    while (transferEnumerator.MoveNext())
                    {
                        if ((++numIters % 10) == 0)
                        {
                            yield return true;
                        }
                    }
                }
            }
        }

        private IEnumerator<bool> TransferItems(IMyInventory sourceInventory)
        {
            var isOreInventory = OreInventories.Contains(sourceInventory);
            var isComponentInventory = ComponentInventories.Contains(sourceInventory);

            sourceInventory.GetItems(Items);
            foreach (var item in Items)
            {
                if (item.Type.TypeId == "MyObjectBuilder_Ingot" || item.Type.TypeId == "MyObjectBuilder_Ore")
                {
                    if (!isOreInventory)
                    {
                        foreach (var oreInventory in OreInventories)
                        {
                            if (sourceInventory.CanTransferItemTo(oreInventory, item.Type))
                            {
                                if (sourceInventory.TransferItemTo(oreInventory, item))
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
                else if (!isComponentInventory)
                {
                    foreach (var componentInventory in ComponentInventories)
                    {
                        if (sourceInventory.CanTransferItemTo(componentInventory, item.Type))
                        {
                            if (sourceInventory.TransferItemTo(componentInventory, item))
                            {
                                break;
                            }
                        }
                    }
                }

                yield return true;
            }
        }
    }
}

using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace ToolCore
{
    /// <summary>
    /// Holds all grid specific data
    /// </summary>
    internal class GridComp
    {
        private ToolSession _session;

        internal MyCubeGrid Grid;
        internal Control ActiveControl;
        internal MyResourceDistributorComponent Distributor;
        internal MyResourceSinkComponent ElectricSink;
        internal MyConveyorSorter SinkBlock;

        internal readonly ConcurrentCachingList<InventoryData> Inventories = new ConcurrentCachingList<InventoryData>();
        internal readonly ConcurrentDictionary<MyCubeBlock, InventoryData> InventoryMap = new ConcurrentDictionary<MyCubeBlock, InventoryData>();

        internal readonly ConcurrentCachingList<ToolComp> UnassignedTools = new ConcurrentCachingList<ToolComp>();
        internal readonly ConcurrentCachingList<ConcurrentCachingList<ToolComp>> ToolGroups = new ConcurrentCachingList<ConcurrentCachingList<ToolComp>>();
        internal readonly ConcurrentCachingList<ConcurrentCachingList<ToolComp>> ToolGroupsSmall = new ConcurrentCachingList<ConcurrentCachingList<ToolComp>>();


        internal readonly ConcurrentDictionary<IMyShipController, Control> Controllers = new ConcurrentDictionary<IMyShipController, Control>();

        internal readonly List<ToolComp> ToolComps = new List<ToolComp>();

        internal MyPlanet ClosestPlanet;

        internal float Power;

        internal long CompTick60;
        internal long CompTick20;

        internal bool UnderControl;
        internal bool Dirty;
        internal bool ConveyorsDirty;

        internal IMyShipWelder Welder;
        internal IMyProjector Projector;

        internal void Init(MyCubeGrid grid, ToolSession session)
        {
            _session = session;

            Grid = grid;

            CompTick20 = Grid.EntityId % 20;
            CompTick60 = Grid.EntityId % 60;

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            Grid.OnFatBlockAdded += FatBlockAdded;
            Grid.OnFatBlockRemoved += FatBlockRemoved;

            foreach (var block in Grid.GetFatBlocks())
            {
                if (block.HasInventory)
                    FatBlockAdded(block);

                if (block is IMyProjector)
                    FatBlockAdded(block);
            }

        }

        internal void Clean()
        {
            Grid.OnBlockAdded -= BlockAdded;
            Grid.OnBlockRemoved -= BlockRemoved;

            Grid.OnFatBlockAdded -= FatBlockAdded;
            Grid.OnFatBlockRemoved -= FatBlockRemoved;

            Grid = null;
            ActiveControl = null;
            Distributor = null;
            ElectricSink = null;
            SinkBlock = null;
            ClosestPlanet = null;

            Inventories.ClearImmediate();
            InventoryMap.Clear();
            Controllers.Clear();
            ToolComps.Clear();
        }

        internal void FatBlockAdded(MyCubeBlock block)
        {

            if (block is MyConveyor || block is MyConveyorConnector)
            {
                block.IsWorkingChanged += ConveyorWorkingChanged;
            }

            if (block is IMyShipWelder)
                Welder = (IMyShipWelder)block;

            if (block is IMyProjector)
                Projector = (IMyProjector)block;

            MyInventory inventory;
            if (block.HasInventory && block.TryGetInventory(out inventory))
            {
                var assembler = block as IMyAssembler;
                if (assembler != null)
                    inventory = assembler.GetInventory(1) as MyInventory;

                if (inventory != null)
                {
                    var data = new InventoryData(inventory, _session);
                    if (InventoryMap.TryAdd(block, data))
                    {
                        Inventories.Add(data);
                        Inventories.ApplyAdditions();
                    }
                    else Logs.WriteLine("FatBlockAdded() - Failed to add inventory to map");

                    block.IsWorkingChanged += (cube) => ConveyorsDirty = true;
                }

                if (block is IMyConveyorSorter)
                {
                    ToolComp comp;
                    if (_session.ToolMap.TryGetValue(block.EntityId, out comp) && !ToolComps.Contains(comp))
                    {
                        ToolComps.Add(comp);

                        if (ToolComps.Count == 1)
                        {
                            var toolGroup = new ConcurrentCachingList<ToolComp>();
                            comp.ToolGroup = toolGroup;
                            toolGroup.Add(comp);
                            ToolGroups.Add(toolGroup);
                        }
                        else
                        {
                            for (int i = 0; i < ToolGroups.Count; i++)
                            {
                                var group = ToolGroups[i];
                                var firstTool = group[0];
                                if ((inventory as IMyInventory).CanTransferItemTo(firstTool.Inventory, _session.SteelPlate))
                                {
                                    comp.ToolGroup = group;
                                    group.Add(comp);
                                    break;
                                }
                        }
                        }

                    }
                }
            }
        }

        private void FatBlockRemoved(MyCubeBlock block)
        {
            if (block is IMyConveyorSorter)
            {
                ToolComp comp;
                if (_session.ToolMap.TryGetValue(block.EntityId, out comp))
                {
                    ToolComps.Remove(comp);
                }
            }

            if (block is MyConveyor || block is MyConveyorConnector)
            {
                block.IsWorkingChanged -= ConveyorWorkingChanged;
            }

            MyInventory inventory;
            if (block.HasInventory && block.TryGetInventory(out inventory))
            {
                var assembler = block as IMyAssembler;
                if (assembler != null)
                    inventory = assembler.GetInventory(1) as MyInventory;

                if (inventory != null)
                {
                    InventoryData data;
                    if (InventoryMap.TryRemove(block, out data))
                    {
                        data.Close();
                        Inventories.Remove(data);
                        Inventories.ApplyRemovals();
                    }
                    else Logs.WriteLine("FatBlockRemoved() - Failed to remove inventory from map");

                    block.IsWorkingChanged -= ConveyorWorkingChanged;
                }
            }
        }

        private void BlockAdded(IMySlimBlock slim)
        {
            Dirty = true;
        }

        private void BlockRemoved(IMySlimBlock slim)
        {
            Dirty = true;
        }

        private void ConveyorWorkingChanged(MyCubeBlock block)
        {
            ConveyorsDirty = true;
        }

        internal void SinkInit(MyConveyorSorter sinkBlock)
        {
            var sinkInfo = new MyResourceSinkInfo()
            {
                MaxRequiredInput = 0,
                RequiredInputFunc = PowerFunc,
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId
            };

            if (SinkBlock != null && ElectricSink != null)
            {
                ElectricSink.SetRequiredInputFuncByType(sinkInfo.ResourceTypeId, () => 0f);
            }

            SinkBlock = sinkBlock;

            ElectricSink = SinkBlock.Components?.Get<MyResourceSinkComponent>();
            if (ElectricSink != null)
            {
                //ElectricSink.RemoveType(ref sinkInfo.ResourceTypeId);
                //ElectricSink.AddType(ref sinkInfo);
                ElectricSink.SetRequiredInputFuncByType(sinkInfo.ResourceTypeId, PowerFunc);
                Logs.WriteLine("sink found");
            }
            else
            {
                ElectricSink = new MyResourceSinkComponent();
                ElectricSink.Init(MyStringHash.GetOrCompute("Thrust"), sinkInfo);
                SinkBlock.Components.Add(ElectricSink);
                Logs.WriteLine("sink added");
            }

            Distributor = (Grid as IMyCubeGrid).ResourceDistributor as MyResourceDistributorComponent;
            if (Distributor != null)
                Distributor.AddSink(ElectricSink);
            else
                Logs.WriteLine($"GridComp.SinkInit() - Distributor null");

            ElectricSink.Update();
        }

        private float PowerFunc()
        {
            return Power;
        }

    }

}

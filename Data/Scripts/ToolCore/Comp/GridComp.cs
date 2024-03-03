using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ToolCore.Session;
using ToolCore.Utils;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace ToolCore.Comp
{
    /// <summary>
    /// Holds all grid specific data
    /// </summary>
    internal class GridComp
    {
        internal ToolSession Session;

        internal MyCubeGrid Grid;

        internal readonly List<ToolComp> ToolComps = new List<ToolComp>();

        internal long CompTick60;
        internal long CompTick20;

        internal bool UnderControl;
        internal bool Dirty;

        internal void Init(MyCubeGrid grid, ToolSession session)
        {
            Session = session;

            Grid = grid;

            CompTick20 = Grid.EntityId % 20;
            CompTick60 = Grid.EntityId % 60;

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockClosed += BlockClosed;

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
            Grid.OnBlockClosed -= BlockClosed;

            Grid.OnFatBlockAdded -= FatBlockAdded;
            Grid.OnFatBlockRemoved -= FatBlockRemoved;

            Grid = null;

            ToolComps.Clear();
        }

        internal void FatBlockAdded(MyCubeBlock block)
        {
            if (block is IMyConveyorSorter)
            {
                ToolComp comp;
                if (Session.ToolMap.TryGetValue(block.EntityId, out comp) && !ToolComps.Contains(comp))
                {
                    ToolComps.Add(comp);
                    ((IMyCubeGrid)Grid).WeaponSystem.Register(comp.GunBase);
                }
            }
        }

        private void FatBlockRemoved(MyCubeBlock block)
        {
            if (block is IMyConveyorSorter)
            {
                ToolComp comp;
                if (Session.ToolMap.TryGetValue(block.EntityId, out comp) && comp?.GunBase != null && ToolComps.Remove(comp))
                {
                    if (!block.CubeGrid.MarkedForClose && !block.CubeGrid.Closed)
                        ((IMyCubeGrid)Grid).WeaponSystem.Unregister(comp.GunBase);
                }
            }
        }

        private void BlockAdded(IMySlimBlock slim)
        {
            //Dirty = true;
            //Session.SlimList.Add(slim);
        }

        private void BlockClosed(IMySlimBlock slim)
        {
            //Dirty = true;
            //Session.SlimList.Remove(slim);
        }

    }

}

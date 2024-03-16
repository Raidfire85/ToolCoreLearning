using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using ToolCore.Comp;
using ToolCore.Utils;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using IMyControllableEntity = VRage.Game.ModAPI.Interfaces.IMyControllableEntity;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        private void OnEntityCreate(MyEntity entity)
        {
            if (!Inited) lock (InitObj) Init();

            var grid = entity as MyCubeGrid;
            if (grid != null)
            {
                grid.AddedToScene += addToStart => _startGrids.Add(grid);

                if (BlockLimits.TrackPCU)
                {
                    grid.OnBlockAdded += slim => SlimList.Add(slim);
                    grid.OnBlockClosed += slim => SlimList.Remove(slim);
                }
                return;
            }

            if (BlockLimits.TrackPCU && entity is MyCubeBlock)
            {
                var block = entity as MyCubeBlock;
                SlimList.Add(block.SlimBlock);
                block.OnClose += ent => SlimList.Remove(((MyCubeBlock)ent).SlimBlock);

            }

            var sorter = entity as IMyConveyorSorter;
            var handTool = entity as IMyHandheldGunObject<MyDeviceBase>;
            if (sorter != null || handTool != null)
            {
                entity.AddedToScene += addToStart => _startComps.Add(entity);

                if (sorter == null)
                    return;

                lock (InitObj)
                {
                    if (ControlsInited)
                        return;

                    ControlsInited = true;
                    MyAPIGateway.Utilities.InvokeOnGameThread(() => Controls.CreateTerminalControls<IMyConveyorSorter>());
                }
            }

            var controller = entity as MyShipController;
            if (controller != null)
            {
                controller.AddedToScene += addToStart => _startComps.Add(controller);
            }

        }

        internal void Init()
        {
            if (Inited) return;
            Inited = true;

            MyAPIGateway.GridGroups.OnGridGroupCreated += GridGroupsOnOnGridGroupCreated;
            MyAPIGateway.GridGroups.OnGridGroupDestroyed += GridGroupsOnOnGridGroupDestroyed;
        }

        private void OnGridClose(IMyEntity entity)
        {
            var grid = entity as MyCubeGrid;

            GridComp comp;
            if (GridMap.TryRemove(grid, out comp))
            {
                GridList.Remove(comp);

                comp.Clean();
                GridCompPool.Push(comp);
            }
        }

        private void OnCloseAll()
        {
            try
            {
                var list = new List<IMyGridGroupData>(GridGroupMap.Keys);
                foreach (var value in list)
                {
                    GridGroupsOnOnGridGroupDestroyed(value);
                }

                MyAPIGateway.GridGroups.OnGridGroupDestroyed -= GridGroupsOnOnGridGroupDestroyed;
                MyAPIGateway.GridGroups.OnGridGroupCreated -= GridGroupsOnOnGridGroupCreated;

                GridGroupMap.Clear();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

        }

        private void GridGroupsOnOnGridGroupCreated(IMyGridGroupData groupData)
        {
            if (groupData.LinkType != GridLinkTypeEnum.Logical)
                return;

            var map = GroupMapPool.Count > 0 ? GroupMapPool.Pop() : new GroupMap();
            map.Init(groupData);

            //groupData.OnReleased += map.OnReleased;
            groupData.OnGridAdded += map.OnGridAdded;
            groupData.OnGridRemoved += map.OnGridRemoved;
            GridGroupMap[groupData] = map;
        }

        private void GridGroupsOnOnGridGroupDestroyed(IMyGridGroupData groupData)
        {
            if (groupData.LinkType != GridLinkTypeEnum.Logical)
                return;

            GroupMap map;
            if (GridGroupMap.TryGetValue(groupData, out map))
            {
                //groupData.OnReleased -= map.OnReleased;
                groupData.OnGridAdded -= map.OnGridAdded;
                groupData.OnGridRemoved -= map.OnGridRemoved;

                GridGroupMap.Remove(groupData);
                map.Clean();
                GroupMapPool.Push(map);

                return;
            }

            Logs.WriteLine($"GridGroupsOnOnGridGroupDestroyed() - could not find map");
        }

        private void PlayerConnected(long id)
        {
            try
            {
                Logs.WriteLine($"PlayerConnected() - {id}");
                if (PlayerMap.ContainsKey(id)) return;
                MyAPIGateway.Multiplayer.Players.GetPlayers(null, myPlayer => FindPlayer(myPlayer, id));
            }
            catch (Exception ex) { Logs.LogException(ex); }
        }

        private void PlayerDisconnected(long id)
        {
            try
            {
                Logs.WriteLine($"PlayerDisconnected() - {id}");
                IMyPlayer player;
                if (PlayerMap.TryRemove(id, out player))
                {
                    player.Controller.ControlledEntityChanged -= OnPlayerController;
                }
            }
            catch (Exception ex) { Logs.LogException(ex); }
        }

        private bool FindPlayer(IMyPlayer player, long id)
        {
            if (player.IdentityId == id)
            {
                PlayerMap.TryAdd(id, player);

                var controller = player.Controller;
                if (controller != null)
                {
                    controller.ControlledEntityChanged += OnPlayerController;
                    OnPlayerController(null, controller.ControlledEntity);
                }

                if (IsDedicated || IsServer && player != MyAPIGateway.Session.LocalHumanPlayer)
                {
                    Networking.SendServerConfig(player.SteamUserId);
                }
            }
            return false;
        }

        private void OnPlayerController(IMyControllableEntity exitController, IMyControllableEntity enterController)
        {
            try
            {
                GridComp gridComp;
                var exitEntity = exitController as MyEntity;
                if (exitEntity != null && enterController?.ControllerInfo != null)
                {
                    var controller = exitEntity as IMyShipController;
                    if (controller != null)
                    {
                        if (GridMap.TryGetValue(controller.CubeGrid, out gridComp))
                        {
                            gridComp.UnderControl = false;
                        }
                    }
                }

                var enterEntity = enterController as MyEntity;
                if (enterEntity != null && enterController.ControllerInfo != null)
                {
                    var controller = enterEntity as IMyShipController;

                    if (controller != null)
                    {
                        if (GridMap.TryGetValue(controller.CubeGrid, out gridComp))
                        {
                            gridComp.UnderControl = true;
                            //gridComp.ActiveControl = gridComp.Controllers[controller];
                        }
                    }
                }
            }
            catch (Exception ex) { Logs.LogException(ex); }
        }

    }
}

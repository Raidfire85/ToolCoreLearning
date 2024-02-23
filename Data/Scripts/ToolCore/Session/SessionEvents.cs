using ParallelTasks;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.ComponentModel;
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
            //if (!Inited) lock (InitObj) Init();

            var grid = entity as MyCubeGrid;
            if (grid != null)
            {
                grid.AddedToScene += addToStart => _startGrids.Add(grid);
                return;
            }

            if (entity is MyCubeBlock)
            {
                var block = entity as MyCubeBlock;
                _slimList.Add(block.SlimBlock);
                block.OnClose += ent => _slimList.Remove(((MyCubeBlock)ent).SlimBlock);

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
        }

        private void OnGridClose(IMyEntity entity)
        {
            var grid = entity as MyCubeGrid;

            GridComp comp;
            if (GridMap.TryRemove(grid, out comp))
            {
                GridList.Remove(comp);

                comp.Clean();
                _gridCompPool.Push(comp);
            }
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

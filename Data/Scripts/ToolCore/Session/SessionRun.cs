using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
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

namespace ToolCore.Session
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
    internal partial class ToolSession : MySessionComponentBase
    {
        internal static int Tick;
        internal int TickMod20;
        internal int TickMod60;
        internal int TickMod120;
        internal bool Tick10;
        internal bool Tick20;
        internal bool Tick60;
        internal bool Tick120;
        internal bool Tick600;
        internal bool IsServer;
        internal bool IsClient;
        internal bool IsDedicated;

        private bool FirstRun = true;

        public override void LoadData()
        {
            IsServer = MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Session.IsServer;
            IsClient = MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Session.IsServer;
            IsDedicated = MyAPIGateway.Utilities.IsDedicated;

            MyEntities.OnEntityCreate += OnEntityCreate;

            Logs.InitLogs();
            Controls.CreateTerminalControls<IMyConveyorSorter>();

            LoadDefinitions();
            LoadToolCoreDefs();

            MyAPIGateway.TerminalControls.CustomActionGetter += Controls.CustomActionGetter;
            MyAPIGateway.TerminalControls.CustomControlGetter += Controls.CustomControlGetter;
        }

        public override void BeforeStart()
        {
            Settings.LoadConfig();
            LoadVoxelMaterials();

            MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
            MyVisualScriptLogicProvider.PlayerRespawnRequest += PlayerConnected;
        }

        public override void UpdateBeforeSimulation()
        {
            Tick++;

            TickMod20 = Tick % 20;
            TickMod60 = Tick % 60;
            TickMod120 = Tick % 120;

            Tick10 = Tick % 10 == 0;
            Tick20 = TickMod20 == 0;
            Tick60 = TickMod60 == 0;
            Tick120 = Tick % 120 == 0;
            Tick600 = Tick % 600 == 0;

            try
            {
                CompLoop();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

            if (!_startBlocks.IsEmpty || !_startGrids.IsEmpty)
                StartComps();

            if (FirstRun)
            {
                if (!IsDedicated)
                    InitPlayers();
                FirstRun = false;
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (IsDedicated) return;
            try
            {
                AvLoop();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }
        }

        protected override void UnloadData()
        {
            MyEntities.OnEntityCreate -= OnEntityCreate;

            MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
            MyVisualScriptLogicProvider.PlayerRespawnRequest -= PlayerConnected;

            MyAPIGateway.TerminalControls.CustomActionGetter -= Controls.CustomActionGetter;
            MyAPIGateway.TerminalControls.CustomControlGetter -= Controls.CustomControlGetter;

            Controls.Clean();
            Settings.Clean();

            Logs.Close();
            Clean();

        }

    }
}

using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace ToolCore
{
    /// <summary>
    /// Stores data from ship controllers
    /// </summary>
    internal class Control
    {
        internal readonly IMyShipController Controller;

        internal MyCubeGrid Grid;

        public Control(IMyShipController controller)
        {
            Controller = controller;

            Grid = controller.CubeGrid as MyCubeGrid;
        }

        internal void Init()
        {

        }

        internal void Close()
        {


            Clean();
        }

        internal void Clean()
        {
            Grid = null;
        }

    }
}

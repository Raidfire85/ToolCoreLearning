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
using ToolCore.Session;

namespace ToolCore
{
    internal partial class ToolSession
    {
        private const string PATH = "Data\\ToolCoreFiles.txt";

        private readonly MyDefinitionId _electricity = MyResourceDistributorComponent.ElectricityId;
        internal readonly MyDefinitionId SteelPlate = new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate");

        private readonly HashSet<MyObjectBuilderType> _toolTypes = new HashSet<MyObjectBuilderType>()
        {
            new MyObjectBuilderType(typeof(MyObjectBuilder_Drill)),
            new MyObjectBuilderType(typeof(MyObjectBuilder_ShipGrinder)),
            new MyObjectBuilderType(typeof(MyObjectBuilder_ShipWelder)),
            new MyObjectBuilderType(typeof(MyObjectBuilder_ConveyorSorter)),
        };

        private readonly Stack<GridComp> _gridCompPool = new Stack<GridComp>(128);
        internal readonly MyConcurrentPool<InventoryItem> InventoryItemPool = new MyConcurrentPool<InventoryItem>(4096);


        private readonly HashSet<MyCubeGrid> _controlledGrids = new HashSet<MyCubeGrid>();

        private readonly ConcurrentCachingList<MyCubeBlock> _startBlocks = new ConcurrentCachingList<MyCubeBlock>();
        private readonly ConcurrentCachingList<MyCubeGrid> _startGrids = new ConcurrentCachingList<MyCubeGrid>();

        internal readonly Dictionary<SerializableDefinitionId, ToolDefinition> DefinitionMap = new Dictionary<SerializableDefinitionId, ToolDefinition>();
        internal readonly Dictionary<MyStringHash, Dictionary<MyStringHash, string>> ParticleMap = new Dictionary<MyStringHash, Dictionary<MyStringHash, string>>();
        internal readonly Dictionary<MyStringHash, Dictionary<MyStringHash, MySoundPair>> SoundMap = new Dictionary<MyStringHash, Dictionary<MyStringHash, MySoundPair>>();
        internal readonly ConcurrentDictionary<long, IMyPlayer> PlayerMap = new ConcurrentDictionary<long, IMyPlayer>();

        internal readonly List<GridComp> GridList = new List<GridComp>();
        internal readonly ConcurrentDictionary<IMyCubeGrid, GridComp> GridMap = new ConcurrentDictionary<IMyCubeGrid, GridComp>();

        internal readonly ConcurrentCachingList<ToolComp> AvComps = new ConcurrentCachingList<ToolComp>();
        internal readonly Dictionary<long, ToolComp> ToolMap = new Dictionary<long, ToolComp>();
        internal readonly Dictionary<IMySlimBlock, float> WorkMap = new Dictionary<IMySlimBlock, float>();

        private readonly List<MyEntity> _entities = new List<MyEntity>();
        private readonly List<MyLineSegmentOverlapResult<MyEntity>> _lineOverlaps = new List<MyLineSegmentOverlapResult<MyEntity>>();

        private readonly ConcurrentCachingList<IMySlimBlock> _hitBlocks = new ConcurrentCachingList<IMySlimBlock>();

        private readonly HashSet<IMySlimBlock> _debugBlocks = new HashSet<IMySlimBlock>();

        private readonly Dictionary<string, int> _missingComponents = new Dictionary<string, int>();

        internal readonly GridUtils GridUtils = new GridUtils();
        internal readonly VoxelUtils VoxelUtils = new VoxelUtils();
        internal readonly DSUtils DsUtil;
        internal readonly DSUtils DsUtil2;
        internal readonly Controls Controls;


        public ToolSession()
        {
            DsUtil = new DSUtils(this);
            DsUtil2 = new DSUtils(this);
            Controls = new Controls(this);
        }

        private void Clean()
        {
            _gridCompPool.Clear();
            _controlledGrids.Clear();
            _startBlocks.ClearImmediate();
            _startGrids.ClearImmediate();

            DefinitionMap.Clear();
            PlayerMap.Clear();

            GridList.Clear();
            GridMap.Clear();
            ToolMap.Clear();

        }

    }
}

using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ToolCore.API;
using ToolCore.Comp;
using ToolCore.Definitions;
using ToolCore.Utils;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        private const string PATH = "Data\\ToolCoreFiles.txt";
        internal readonly Guid CompDataGuid = new Guid("75BBB4F5-4FB9-4230-AAAA-BB79C9811618");
        internal readonly float VoxelHarvestRatio = 0.009f;

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

        private readonly ConcurrentCachingList<MyEntity> _startBlocks = new ConcurrentCachingList<MyEntity>();
        private readonly ConcurrentCachingList<MyCubeGrid> _startGrids = new ConcurrentCachingList<MyCubeGrid>();

        internal readonly Dictionary<SerializableDefinitionId, ToolDefinition> DefinitionMap = new Dictionary<SerializableDefinitionId, ToolDefinition>();
        internal readonly Dictionary<string, Dictionary<MyStringHash, string>> ParticleMap = new Dictionary<string, Dictionary<MyStringHash, string>>();
        internal readonly Dictionary<string, Dictionary<MyStringHash, MySoundPair>> SoundMap = new Dictionary<string, Dictionary<MyStringHash, MySoundPair>>();
        internal readonly Dictionary<MyVoxelMaterialDefinition, float> MaterialModifiers = new Dictionary<MyVoxelMaterialDefinition, float>();

        internal readonly Dictionary<string, List<MyVoxelMaterialDefinition>> MaterialCategoryMap = new Dictionary<string, List<MyVoxelMaterialDefinition>>();

        internal readonly List<GridComp> GridList = new List<GridComp>();
        internal readonly ConcurrentDictionary<IMyCubeGrid, GridComp> GridMap = new ConcurrentDictionary<IMyCubeGrid, GridComp>();

        internal readonly ConcurrentCachingList<ToolComp> AvComps = new ConcurrentCachingList<ToolComp>();
        internal readonly Dictionary<long, ToolComp> ToolMap = new Dictionary<long, ToolComp>();
        internal readonly Dictionary<IMySlimBlock, float> WorkMap = new Dictionary<IMySlimBlock, float>();
        internal readonly ConcurrentDictionary<long, IMyPlayer> PlayerMap = new ConcurrentDictionary<long, IMyPlayer>();

        private readonly List<MyEntity> _entities = new List<MyEntity>();
        private readonly List<MyLineSegmentOverlapResult<MyEntity>> _lineOverlaps = new List<MyLineSegmentOverlapResult<MyEntity>>();

        private readonly ConcurrentCachingList<IMySlimBlock> _hitBlocks = new ConcurrentCachingList<IMySlimBlock>();

        private readonly HashSet<IMySlimBlock> _debugBlocks = new HashSet<IMySlimBlock>();
        internal readonly ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>> DrawBoxes = new ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>>();


        private readonly Dictionary<string, int> _missingComponents = new Dictionary<string, int>();

        internal readonly GridUtils GridUtils = new GridUtils();
        internal readonly Settings Settings = new Settings();
        internal readonly DSUtils DsUtil;
        internal readonly DSUtils DsUtil2;
        internal readonly Controls Controls;
        internal readonly Networking Networking;
        internal readonly APIBackend API;
        internal readonly APIServer APIServer;

        internal bool PbApiInited;


        public ToolSession()
        {
            DsUtil = new DSUtils(this);
            DsUtil2 = new DSUtils(this);
            Controls = new Controls(this);
            Networking = new Networking(this);
            API = new APIBackend(this);
            APIServer = new APIServer(this);
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

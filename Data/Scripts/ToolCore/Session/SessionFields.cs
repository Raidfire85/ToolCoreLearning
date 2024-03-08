using DefenseShields;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ToolCore.API;
using ToolCore.Comp;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRage.Library.Threading;
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
        internal ParallelTasks.Task AggregatorTask = new ParallelTasks.Task();
        
        internal readonly MyDefinitionId SteelPlate = new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate");

        internal readonly Stack<GridComp> GridCompPool = new Stack<GridComp>(64);
        internal readonly Stack<DrillData> DrillDataPool = new Stack<DrillData>(64);
        internal readonly Stack<ToolComp.ToolData> ToolDataPool = new Stack<ToolComp.ToolData>(64);

        internal readonly FastLookupCachingList<IMySlimBlock> SlimList = new FastLookupCachingList<IMySlimBlock>();

        internal readonly Dictionary<SerializableDefinitionId, List<ToolDefinition>> DefinitionMap = new Dictionary<SerializableDefinitionId, List<ToolDefinition>>();
        internal readonly Dictionary<string, Dictionary<MyStringHash, string>> ParticleMap = new Dictionary<string, Dictionary<MyStringHash, string>>();
        internal readonly Dictionary<string, Dictionary<MyStringHash, MySoundPair>> SoundMap = new Dictionary<string, Dictionary<MyStringHash, MySoundPair>>();
        internal readonly Dictionary<MyVoxelMaterialDefinition, float> MaterialModifiers = new Dictionary<MyVoxelMaterialDefinition, float>();
        internal readonly Dictionary<string, List<MyVoxelMaterialDefinition>> MaterialCategoryMap = new Dictionary<string, List<MyVoxelMaterialDefinition>>();
        internal readonly Dictionary<long, ToolComp> ToolMap = new Dictionary<long, ToolComp>();
        internal readonly Dictionary<IMySlimBlock, float> WorkMap = new Dictionary<IMySlimBlock, float>();
        internal readonly Dictionary<string, int> MissingComponents = new Dictionary<string, int>();
        internal readonly ConcurrentDictionary<IMyCubeGrid, GridComp> GridMap = new ConcurrentDictionary<IMyCubeGrid, GridComp>();
        internal readonly ConcurrentDictionary<long, IMyPlayer> PlayerMap = new ConcurrentDictionary<long, IMyPlayer>();

        internal readonly List<Trigger> Triggers = new List<Trigger>((Trigger[])Enum.GetValues(typeof(Trigger)));
        internal readonly List<ToolComp> HandTools = new List<ToolComp>();
        internal readonly List<GridComp> GridList = new List<GridComp>();
        internal readonly ConcurrentCachingList<ToolComp> AvComps = new ConcurrentCachingList<ToolComp>();

        private readonly HashSet<MyCubeGrid> _controlledGrids = new HashSet<MyCubeGrid>();
        private readonly List<MyEntity> _entities = new List<MyEntity>();
        private readonly List<MyLineSegmentOverlapResult<MyEntity>> _lineOverlaps = new List<MyLineSegmentOverlapResult<MyEntity>>();

        private readonly ConcurrentCachingList<MyEntity> _startComps = new ConcurrentCachingList<MyEntity>();
        private readonly ConcurrentCachingList<MyCubeGrid> _startGrids = new ConcurrentCachingList<MyCubeGrid>();


        internal readonly Settings Settings = new Settings();
        internal readonly ShieldApi DSAPI = new ShieldApi();
        internal readonly DSUtils DsUtil;
        internal readonly DSUtils DsUtil2;
        internal readonly Controls Controls;
        internal readonly Networking Networking;
        internal readonly APIBackend API;
        internal readonly APIServer APIServer;
        internal object InitObj = new object();

        internal readonly BlockLimitInfo BlockLimits = new BlockLimitInfo();

        internal bool DSAPIReady;

        internal volatile bool Inited;
        internal volatile bool ControlsInited;
        internal volatile bool PbApiInited;

        internal static ToolSession Instance;


        public ToolSession()
        {
            Instance = this;

            DsUtil = new DSUtils(this);
            DsUtil2 = new DSUtils(this);
            Controls = new Controls(this);
            Networking = new Networking(this);
            API = new APIBackend(this);
            APIServer = new APIServer(this);
        }

        private void Clean()
        {
            GridCompPool.Clear();
            DrillDataPool.Clear();
            ToolDataPool.Clear();

            SlimList.Clear();

            DefinitionMap.Clear();
            ParticleMap.Clear();
            SoundMap.Clear();
            MaterialModifiers.Clear();
            MaterialCategoryMap.Clear();
            ToolMap.Clear();
            WorkMap.Clear();
            MissingComponents.Clear();
            GridMap.Clear();
            PlayerMap.Clear();

            HandTools.Clear();
            GridList.Clear();
            AvComps.ClearImmediate();

            _controlledGrids.Clear();
            _entities.Clear();
            _lineOverlaps.Clear();

            _startComps.ClearImmediate();
            _startGrids.ClearImmediate();

        }

    }
}

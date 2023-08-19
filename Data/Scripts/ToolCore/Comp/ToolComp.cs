using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using static ToolCore.Draw;
using static ToolCore.Definitions.ToolDefinition;
using ToolCore.Comp;

namespace ToolCore
{
    /// <summary>
    /// Holds all thrust block data
    /// </summary>
    internal partial class ToolComp : MyEntityComponentBase
    {
        internal readonly ToolSession Session;

        internal readonly GunCore ToolGun;
        internal readonly MyInventory Inventory;

        internal ToolDefinition Definition;
        internal IMyConveyorSorter Tool;
        internal IMyModelDummy Muzzle;
        internal MyEntity MuzzlePart;
        internal MyResourceSinkComponent Sink;
        internal MyOrientedBoundingBoxD Obb;
        internal MyEntity3DSoundEmitter SoundEmitter;

        internal ConcurrentCachingList<ToolComp> ToolGroup;

        internal MyCubeGrid Grid;
        internal GridComp GridComp;
        internal ToolRepo Repo;

        internal IMyTerminalControlOnOffSwitch ShowInToolbarSwitch;

        internal ToolMode Mode;
        internal Trigger State;
        internal Drills DrillData;

        internal readonly Dictionary<Trigger, Effects> EventEffects = new Dictionary<Trigger, Effects>();
        internal readonly List<Effects> ActiveEffects = new List<Effects>();
        internal readonly Dictionary<int, List<PositionData>> WorkLayers = new Dictionary<int, List<PositionData>>();
        internal readonly List<byte> MaxContent = new List<byte>();
        internal readonly List<StorageInfo> StorageDatas = new List<StorageInfo>();
        internal readonly ConcurrentDictionary<MyObjectBuilder_Ore, float> Yields = new ConcurrentDictionary<MyObjectBuilder_Ore, float>();

        internal readonly HashSet<Vector3I> PreviousPositions = new HashSet<Vector3I>();
        internal readonly ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>> DrawBoxes = new ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>>();

        internal bool Enabled;
        internal bool Functional;
        internal bool Powered;
        internal bool Dirty;
        internal bool AvActive;
        internal bool UpdatePower;
        internal bool LastPushSucceeded;

        internal bool Hitting;
        internal bool WasHitting;
        internal readonly Hit HitInfo = new Hit();
        internal MyStringHash HitMaterial = MyStringHash.GetOrCompute("Metal");

        internal bool Activated;

        internal bool NoEmitter;
        internal bool Draw = true;
        internal bool Debug = true;

        internal long CompTick20;
        internal long CompTick60;
        internal long CompTick120;
        internal int LastPushTick;
        internal int ActiveDrillThreads;

        internal enum ToolMode
        {
            Drill = 0,
            Grind = 1,
            Weld = 2,
        }

        internal class Hit
        {
            internal Vector3D Position;
            internal MyStringHash Material;

            internal void Update(Vector3D pos, MyStringHash material)
            {
                Position = pos;
                Material = material;
            }
        }

        internal class Effects
        {
            internal readonly bool HasAnimations;
            internal readonly bool HasParticles;
            internal readonly bool HasSound;
            internal readonly List<Animation> Animations;
            internal readonly List<ParticleEffect> ParticleEffects;
            internal readonly SoundDef SoundDef;

            internal bool Active;
            internal bool Expired;
            internal bool Dirty;
            internal bool Restart;
            internal int LastActiveTick;

            internal Effects(List<AnimationDef> animationDefs, List<ParticleEffectDef> particleEffectDefs, SoundDef soundDef, ToolComp comp)
            {
                if (animationDefs?.Count > 0)
                {
                    Animations = new List<Animation>();
                    foreach (var aDef in animationDefs)
                    {
                        var entity = (MyEntity)comp.Tool;
                        MyEntitySubpart subpart;
                        if (!entity.TryGetSubpartRecursive(aDef.Subpart, out subpart))
                        {
                            Logs.WriteLine($"Subpart '{aDef.Subpart}' not found!");
                            continue;
                        }

                        var anim = new Animation(aDef, subpart);
                        Animations.Add(anim);
                    }
                    HasAnimations = Animations.Count > 0;
                }

                if (particleEffectDefs?.Count > 0)
                {
                    ParticleEffects = new List<ParticleEffect>();
                    foreach (var pDef in particleEffectDefs)
                    {
                        var entity = (MyEntity)comp.Tool;
                        IMyModelDummy dummy;
                        MyEntity parent;
                        if (!entity.TryGetDummy(pDef.Dummy, out dummy, out parent))
                        {
                            Logs.WriteLine($"Dummy '{pDef.Dummy}' not found!");
                            continue;
                        }

                        var effect = new ParticleEffect(pDef, dummy, parent);
                        ParticleEffects.Add(effect);
                    }
                    HasParticles = ParticleEffects.Count > 0;
                }

                HasSound = (SoundDef = soundDef) != null;
            }

            internal void Clean()
            {
                Active = false;
                Expired = false;
                Dirty = false;
                Restart = false;
                LastActiveTick = 0;
            }

            internal class Animation
            {
                internal readonly AnimationDef Definition;
                internal MyEntitySubpart Subpart;

                internal bool Starting;
                internal int TransitionState;

                public Animation(AnimationDef def, MyEntitySubpart subpart)
                {
                    Definition = def;
                    Subpart = subpart;
                }
            }

            internal class ParticleEffect
            {
                internal IMyModelDummy Dummy;
                internal MyEntity Parent;

                internal ParticleEffectDef Definition;

                internal MyParticleEffect Particle;
                internal bool Looping;

                public ParticleEffect(ParticleEffectDef def, IMyModelDummy dummy, MyEntity parent)
                {
                    Dummy = dummy;
                    Parent = parent;
                    Definition = def;
                }
            }
        }

        internal class Drills
        {
            internal IMyVoxelBase Voxel;
            internal Vector3I Min;
            internal Vector3I Max;
            internal Vector3D Origin;
            internal Vector3D Direction;
        }

        internal class PositionData
        {
            internal int Index;
            internal float Distance;
            internal float SecondaryDistanceSqr;

            public PositionData(int index, float distance, float secondaryDistSqr)
            {
                Index = index;
                Distance = distance;
                SecondaryDistanceSqr = secondaryDistSqr;
            }
        }

        internal class StorageInfo
        {
            internal Vector3I Min;
            internal Vector3I Max;

            public StorageInfo(Vector3I min, Vector3I max)
            {
                Min = min;
                Max = max;
            }
        }

        internal ToolComp(IMyConveyorSorter block, ToolDefinition def, ToolSession session)
        {
            Session = session;

            Definition = def;
            Tool = block;
            ToolGun = new GunCore(this);
            Inventory = (MyInventory)(Tool as MyEntity).GetInventoryBase();

            Grid = Tool.CubeGrid as MyCubeGrid;

            if (Definition.EffectShape == EffectShape.Cuboid)
            {
                Obb = new MyOrientedBoundingBoxD();
                Obb.HalfExtent = Definition.HalfExtent * Grid.GridSizeR;
            }

            var type = (int)Definition.ToolType;
            Mode = type < 2 ? ToolMode.Drill : type < 4 ? ToolMode.Grind : ToolMode.Weld;
            if ((type & 1) > 0)
                DrillData = new Drills();

            var hasSound = false;
            foreach (var pair in def.EventEffectDefs)
            {
                var myTuple = pair.Value;
                EventEffects[pair.Key] = new Effects(myTuple.Item1, myTuple.Item2, myTuple.Item3, this);
                hasSound = myTuple.Item3 != null;
            }

            if (hasSound)
                SoundEmitter = new MyEntity3DSoundEmitter(Tool as MyEntity);

            CompTick20 = Tool.EntityId % 20;
            CompTick60 = Tool.EntityId % 60;
            CompTick120 = Tool.EntityId % 120;

            Tool.Enabled = false;
            Tool.EnabledChanged += EnabledChanged;
            Tool.IsWorkingChanged += IsWorkingChanged;

            Enabled = Tool.Enabled;
            Functional = Tool.IsFunctional;

        }

        internal void UpdateState1(Trigger state, bool add)
        {
            if (add)
                State |= state;
            else
            {
                state &= State;
                State ^= state;
            }
            //Logs.WriteLine($"{state} : {add} : result = {State}");

            Effects effects;
            foreach (var trigger in (Trigger[])Enum.GetValues(typeof(Trigger)))
            {
                if ((state & trigger) == 0)
                    continue;

                if (!add && (State & trigger) > 0)
                    continue;

                if (!EventEffects.TryGetValue(trigger, out effects))
                    continue;

                //var text = add ? "adding" : "removing";
                //Logs.WriteLine($"Valid effect: {trigger} : {text} : active: {effects.Active} : expired: {effects.Expired}");

                if (!add)
                {
                    effects.Expired = effects.Active;
                    continue;
                }

                if (!effects.Active)
                {
                    ActiveEffects.Add(effects);
                    effects.Active = true;
                    continue;
                }

                if (effects.Expired)
                {
                    effects.Expired = false;
                    effects.Restart = true;
                }
            }
        }

        internal void UpdateState(Trigger state, bool add, bool force = false)
        {
            var isActive = (State & state) > 0;
            //Logs.WriteLine($"UpdateState : {state} : {add} : {isActive}");

            if (!force)
            {
                if (add == isActive)
                    return;

                if (add)
                    State |= state;
                else
                {
                    state &= State;
                    State ^= state;
                }
            }

            switch (state)
            {
                case Trigger.Functional:
                    UpdateEffects(Trigger.Functional, add);
                    if (add && !IsPowered()) break;
                    UpdateState(Trigger.Powered, add);
                    break;
                case Trigger.Powered:
                    UpdateEffects(Trigger.Powered, add);
                    if (add && !Enabled) break;
                    UpdateState(Trigger.Enabled, add);
                    break;
                case Trigger.LeftClick:
                case Trigger.RightClick:
                    UpdateEffects(state, add);
                    UpdateState(Trigger.Click, add, true);
                    break;
                case Trigger.Enabled:
                case Trigger.Click:
                    UpdateEffects(state, add);
                    if (!add && (State & Trigger.Active) > 0) break;
                    UpdateState(Trigger.Active, add, true);
                    break;
                case Trigger.Active:
                    UpdateEffects(Trigger.Active, add);
                    if (add) break;
                    UpdateState(Trigger.Hit, false);
                    break;
                case Trigger.Hit:
                    UpdateEffects(Trigger.Hit, add);
                    break;
                default:
                    break;
            }
        }

        internal void UpdateEffects(Trigger state, bool add)
        {
            Effects effects;
            if (!EventEffects.TryGetValue(state, out effects))
                return;

            if (!add)
            {
                effects.Expired = effects.Active;
                return;
            }

            if (!effects.Active)
            {
                ActiveEffects.Add(effects);
                effects.Active = true;
                return;
            }

            if (effects.Expired)
            {
                effects.Expired = false;
                effects.Restart = true;
            }
        }

        internal bool IsPowered(bool log = false)
        {
            Sink.Update();
            var required = RequiredInput();
            var elec = MyResourceDistributorComponent.ElectricityId;
            var distributor = (MyResourceDistributorComponent)((IMyCubeGrid)Grid).ResourceDistributor;
            Powered = required > 0 && Sink.IsPoweredByType(elec) && (Sink.ResourceAvailableByType(elec) >= required || distributor.MaxAvailableResourceByType(elec) >= required);
            return Powered;
        }

        private void EnabledChanged(IMyTerminalBlock block)
        {
            Enabled = (block as IMyFunctionalBlock).Enabled;

            Sink.Update();
            UpdatePower = true;
            if (!Powered) return;

            //if (!Enabled)
            //{
            //    Logs.WriteLine("read: " + Session.DsUtil.GetValue("read").ToString());
            //    Logs.WriteLine("sort: " + Session.DsUtil.GetValue("sort").ToString());
            //    Logs.WriteLine("calc: " + Session.DsUtil.GetValue("calc").ToString());
            //    Logs.WriteLine("write: " + Session.DsUtil.GetValue("write").ToString());
            //    Logs.WriteLine("notify: " + Session.DsUtil.GetValue("notify").ToString());
            //    Session.DsUtil.Clean();
            //}

            UpdateState(Trigger.Enabled, Enabled);
        }

        private void IsWorkingChanged(IMyCubeBlock block)
        {
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();

            Close();
        }

        public override bool IsSerialized()
        {
            if (Tool.Storage == null || Repo == null) return false;

            Repo.Sync(this);

            Tool.Storage[Session.CompDataGuid] = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Repo));

            return false;
        }

        internal void Init()
        {
            Tool.Components.Add(this);

            SinkInit();
            StorageInit();
            SubpartsInit();

            if (!Session.IsDedicated)
                GetShowInToolbarSwitch();
        }

        private void SinkInit()
        {
            var sinkInfo = new MyResourceSinkInfo()
            {
                MaxRequiredInput = Definition.ActivePower,
                RequiredInputFunc = RequiredInput,
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId
            };

            Sink = Tool.Components?.Get<MyResourceSinkComponent>();
            if (Sink != null)
            {
                Logs.WriteLine("Sink found on init, setting input func...");
                Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, RequiredInput);
            }
            else
            {
                Logs.WriteLine("No sink found on init, creating...");
                Sink = new MyResourceSinkComponent();
                Sink.Init(MyStringHash.GetOrCompute("Defense"), sinkInfo);
                Tool.Components.Add(Sink);
            }

            var distributor = (MyResourceDistributorComponent)Tool.CubeGrid.ResourceDistributor;
            if (distributor == null)
            {
                Logs.WriteLine("Grid distributor null on sink init!");
                return;
            }

            distributor.AddSink(Sink);
            Sink.Update();
        }

        private float RequiredInput()
        {
            if (!Functional)
                return 0f;

            if (Enabled || ToolGun.WantsToShoot)
                return Definition.ActivePower;

            return Definition.IdlePower;
        }

        internal void SubpartsInit()
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>() { Tool };
            Tool.Hierarchy.GetChildrenRecursive(entities);

            Dictionary<string, IMyModelDummy> dummies = new Dictionary<string, IMyModelDummy>();
            foreach (var entity in entities)
            {
                if (entity != Tool)
                    entity.OnClose += (ent) => Dirty = true;

                entity.Model.GetDummies(dummies);

                foreach (var dummy in dummies)
                {
                    if (dummy.Key == Definition.EmitterName)
                    {
                        Muzzle = dummy.Value;
                        MuzzlePart = (MyEntity)entity;
                        Logs.WriteLine("SubpartsInit() : Emitter found");
                    }
                }

                dummies.Clear();
            }

            if (Muzzle == null)
            {
                NoEmitter = true;
                Logs.WriteLine($"Failed to find emitter dummy '{Definition.EmitterName}'!");
            }

            Dirty = false;
        }

        private void StorageInit()
        {
            string rawData;
            ToolRepo loadRepo = null;
            if (Tool.Storage == null)
            {
                Tool.Storage = new MyModStorageComponent();
            }
            else if (Tool.Storage.TryGetValue(Session.CompDataGuid, out rawData))
            {
                try
                {
                    var base64 = Convert.FromBase64String(rawData);
                    loadRepo = MyAPIGateway.Utilities.SerializeFromBinary<ToolRepo>(base64);
                }
                catch (Exception ex)
                {
                    Logs.WriteLine($"DriveComp - Exception at StorageInit() - {ex}");
                }
            }

            if (loadRepo != null)
            {
                Sync(loadRepo);
            }
            else
            {
                Repo = new ToolRepo();
            }
        }

        private void Sync(ToolRepo repo)
        {
            Repo = repo;

            Activated = repo.Activated;
            Draw = repo.Draw;
            Mode = (ToolMode)repo.Mode;
        }

        internal void DrillSphere()
        {
            DrawBoxes.ClearImmediate();

            var centre = DrillData.Origin;
            var forward = DrillData.Direction;
            var radius = Definition.Radius;
            var radiusSqr = radius * radius;
            var extendedRadius = radius + 0.5f;
            var extRadiusSqr = extendedRadius * extendedRadius;

            var voxel = DrillData.Voxel;
            var min = DrillData.Min;
            var max = DrillData.Max;

            var reduction = (int)(Definition.Speed * 255);
            using ((voxel as MyVoxelBase).Pin())
            {
                var data = new MyStorageData();
                data.Resize(min, max);

                Session.DsUtil.Start("read");
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);
                Session.DsUtil.Complete("read", true);

                MyFixedPoint amount = 0;
                Vector3I testPos = new Vector3I();

                int content;
                byte material;

                Session.DsUtil.Start("sort");
                var maxLayer = 0;
                var foundContent = false;
                for (int i = min.X; i <= max.X; i++)
                {
                    testPos.X = i;
                    for (int j = min.Y; j <= max.Y; j++)
                    {
                        testPos.Y = j;
                        for (int k = min.Z; k <= max.Z; k++)
                        {
                            testPos.Z = k;

                            var relativePos = testPos - min;
                            var index = data.ComputeLinear(ref relativePos);
                            if (index < 0 || index > data.SizeLinear)
                                continue;

                            content = data.Content(index);
                            if (content == 0)
                                continue;

                            var offset = (Vector3D)testPos - centre;
                            var distSqr = offset.LengthSquared();
                            if (distSqr > extRadiusSqr)
                                continue;

                            foundContent = true;

                            var dist = 0f;
                            var secondaryDistSqr = 0f;
                            switch (Definition.Pattern)
                            {
                                case WorkOrder.InsideOut:
                                    dist = (float)offset.Length();
                                    break;
                                case WorkOrder.OutsideIn:
                                    dist = radius - (float)offset.Length();
                                    break;
                                case WorkOrder.Forward:
                                    var displacement = Vector3D.ProjectOnVector(ref offset, ref forward);
                                    dist = radius + (float)displacement.Length() * Math.Sign(Vector3D.Dot(offset, forward));
                                    break;
                                case WorkOrder.Backward:
                                    displacement = Vector3D.ProjectOnVector(ref offset, ref forward);
                                    dist = radius - (float)displacement.Length() * Math.Sign(Vector3D.Dot(offset, forward));
                                    break;
                                default:
                                    break;
                            }

                            var posData = new PositionData(index, dist, secondaryDistSqr);

                            var roundDist = MathHelper.RoundToInt(dist);
                            if (roundDist > maxLayer) maxLayer = roundDist;

                            List<PositionData> layer;
                            if (WorkLayers.TryGetValue(roundDist, out layer))
                                layer.Add(posData);
                            else WorkLayers[roundDist] = new List<PositionData>() { posData };

                        }
                    }
                }
                if (foundContent) StorageDatas.Add(new StorageInfo(min, max));
                Session.DsUtil.Complete("sort", true);

                Session.DsUtil.Start("calc");
                var hit = false;
                //MyAPIGateway.Utilities.ShowNotification($"{WorkLayers.Count} layers", 160);
                for (int i = 0; i <= maxLayer; i++)
                {
                    List<PositionData> layer;
                    if (!WorkLayers.TryGetValue(i, out layer))
                        continue;

                    var maxContent = 0;
                    //MyAPIGateway.Utilities.ShowNotification($"{layer.Count} items", 160);
                    for (int j = 0; j < layer.Count; j++)
                    {
                        var positionData = layer[j];
                        var index = positionData.Index;
                        var distance = positionData.Distance;
                        var secondaryDistSqr = positionData.SecondaryDistanceSqr;

                        var overlap = radius + 0.5f - distance;
                        if (overlap <= 0f)
                            continue;

                        content = data.Content(index);
                        material = data.Material(index);

                        var removal = Math.Min(reduction, content);

                        if (overlap < 1f)
                        {
                            //if (Debug)
                            //{
                            //    var matrix = voxel.PositionComp.WorldMatrixRef;
                            //    matrix.Translation = voxel.PositionLeftBottomCorner;
                            //    Vector3I pos;
                            //    data.ComputePosition(index, out pos);
                            //    pos += min;
                            //    var lowerHalf = (Vector3D)pos - 0.475;
                            //    var upperHalf = (Vector3D)pos + 0.475;
                            //    var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                            //    var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                            //    var color = (Color)Vector4.Lerp(Color.Red, Color.Green, overlap);
                            //    DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(obb, color));
                            //}

                            overlap *= 255;
                            var excluded = 255 - MathHelper.FloorToInt(overlap);
                            var excess = content - excluded;
                            if (excess <= 0f)
                                continue;

                            removal = Math.Min(removal, excess);
                        }

                        var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        var hardness = (def != null) ? Session.Settings.MaterialModifiers[def] : 1f;
                        var effectiveContent = MathHelper.CeilToInt(content * hardness);
                        maxContent = Math.Max(maxContent, effectiveContent);

                        if (!hit)
                        {
                            data.ComputePosition(index, out testPos);
                            var localPos = (Vector3D)testPos + min;
                            var voxelMatrix = voxel.PositionComp.WorldMatrixRef;
                            voxelMatrix.Translation = voxel.PositionLeftBottomCorner;
                            Vector3D worldPos;
                            Vector3D.Transform(ref localPos, ref voxelMatrix, out worldPos);
                            HitInfo.Update(worldPos, def.MaterialTypeNameHash);

                            hit = true;
                            Hitting = true;
                        }

                        if (def != null && def.CanBeHarvested && !string.IsNullOrEmpty(def.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
                            oreOb.MaterialTypeName = def.Id.SubtypeId;
                            var yield = (removal / 255f) * def.MinedOreRatio * Definition.HarvestRatio * Session.VoxelHarvestRatio;

                            if (!Yields.TryAdd(oreOb, yield))
                                Yields[oreOb] += yield;
                        }

                        var newContent = content - removal;
                        data.Content(index, (byte)newContent);
                        if (newContent == 0)
                            data.Material(index, byte.MaxValue);
                    }

                    reduction -= maxContent;
                    if (reduction <= 0)
                        break;
                }
                Session.DsUtil.Complete("calc", true);

                Session.DsUtil.Start("write");
                voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);
                Session.DsUtil.Complete("write", true);

            }
            WorkLayers.Clear();

        }

        internal void DrillCylinder()
        {
            var centre = DrillData.Origin;
            var forward = DrillData.Direction;
            var radius = Definition.Radius;
            var length = Definition.Length;
            var endOffset = forward * (length / 2f);
            var endOffsetAbs = Vector3D.Abs(endOffset);

            var voxel = DrillData.Voxel;
            var min = DrillData.Min;
            var max = DrillData.Max;

            var halfLenSqr = Math.Pow(length / 2f, 2);
            var radiusSqr = (float)Math.Pow(radius, 2);
            var radiusMinusOneSqr = (float)Math.Pow(Math.Max(radius - 1f, 0), 2);
            var reduction = (int)(Definition.Speed * 255);
            using ((voxel as MyVoxelBase).Pin())
            {
                var data = new MyStorageData();
                data.Resize(min, max);

                Session.DsUtil.Start("read");
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);
                Session.DsUtil.Complete("read", true);

                MyFixedPoint amount = 0;
                Vector3I testPos = new Vector3I();

                byte content;
                byte material;

                Session.DsUtil.Start("sort");
                var maxLayer = 0;
                for (int i = min.X; i <= max.X; i++)
                {
                    testPos.X = i;
                    for (int j = min.Y; j <= max.Y; j++)
                    {
                        testPos.Y = j;
                        for (int k = min.Z; k <= max.Z; k++)
                        {
                            testPos.Z = k;

                            var relativePos = testPos - min;
                            var index = data.ComputeLinear(ref relativePos);
                            if (index < 0 || index > data.SizeLinear)
                                continue;

                            content = data.Content(index);
                            if (content == 0)
                                continue;

                            var offset = (Vector3D)testPos - centre;
                            var radial = Vector3D.ProjectOnPlane(ref offset, ref forward);
                            var radialDistSqr = (float)radial.LengthSquared();
                            if (radialDistSqr > radiusSqr)
                                continue;
                            var axial = Vector3D.ProjectOnVector(ref offset, ref forward);
                            var axialDistSqr = (float)axial.LengthSquared();
                            if (axialDistSqr > halfLenSqr)
                                continue;

                            var dist = 0f;
                            var secondaryDistSqr = 0f;
                            switch (Definition.Pattern)
                            {
                                case WorkOrder.InsideOut:
                                    dist = (float)radial.Length();
                                    secondaryDistSqr = axialDistSqr;
                                    break;
                                case WorkOrder.OutsideIn:
                                    dist = radius - (float)radial.Length();
                                    secondaryDistSqr = axialDistSqr;
                                    break;
                                case WorkOrder.Forward:
                                    dist = length / 2f + (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, forward));
                                    secondaryDistSqr = radialDistSqr;
                                    break;
                                case WorkOrder.Backward:
                                    dist = length / 2f - (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, forward));
                                    secondaryDistSqr = radialDistSqr;
                                    break;
                                default:
                                    break;
                            }

                            //if (Debug)
                            //{
                            //    var matrix = voxel.PositionComp.WorldMatrixRef;
                            //    matrix.Translation = voxel.PositionLeftBottomCorner;
                            //    var lowerHalf = (Vector3D)testPos - 0.5;
                            //    var upperHalf = (Vector3D)testPos + 0.5;
                            //    var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                            //    var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                            //    MyAPIGateway.Utilities.InvokeOnGameThread(() => DrawBox(obb, Color.BlueViolet, false, 1, 0.01f));
                            //}

                            var posData = new PositionData(index, dist, secondaryDistSqr);

                            var roundDist = MathHelper.RoundToInt(dist);
                            if (roundDist > maxLayer) maxLayer = roundDist;

                            List<PositionData> layer;
                            if (WorkLayers.TryGetValue(roundDist, out layer))
                                layer.Add(posData);
                            else WorkLayers[roundDist] = new List<PositionData>() { posData };

                        }
                    }
                }
                Session.DsUtil.Complete("sort", true);

                Session.DsUtil.Start("calc");
                //MyAPIGateway.Utilities.ShowNotification($"{WorkLayers.Count} layers", 160);
                for (int i = 0; i <= maxLayer; i++)
                {
                    List<PositionData> layer;
                    if (!WorkLayers.TryGetValue(i, out layer))
                        continue;

                    var maxContent = 0;
                    //MyAPIGateway.Utilities.ShowNotification($"{layer.Count} items", 160);
                    for (int j = 0; j < layer.Count; j++)
                    {
                        var positionData = layer[j];
                        var index = positionData.Index;
                        var distance = positionData.Distance;
                        var secondaryDistSqr = positionData.SecondaryDistanceSqr;

                        content = data.Content(index);
                        material = data.Material(index);

                        if (content > maxContent) maxContent = content;

                        var removal = Math.Min(reduction, 255);

                        var limit = 1f;
                        switch (Definition.Pattern)
                        {
                            case WorkOrder.InsideOut:
                                limit = radius - distance;
                                break;
                            case WorkOrder.OutsideIn:
                                limit = distance;
                                break;
                            case WorkOrder.Forward:
                                limit = length - distance;
                                break;
                            default:
                                break;
                        }
                        if (limit < 0.5f)
                        {
                            var density = MathHelper.Clamp(limit, -1, 1) * 0.5 + 0.5;
                            removal = (int)(removal * density);
                        }
                        else if (distance > i + 0.5f)
                        {
                            var edgeDist = i - distance;
                            var density = MathHelper.Clamp(edgeDist, -1, 1) * 0.5 + 0.5;
                            var leftover = reduction - removal;
                            removal = (int)(removal * density) + leftover;
                        }
                        Hitting |= removal > 0;
                        var newContent = removal >= content ? 0 : content - removal;

                        var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        if (def != null && def.CanBeHarvested && !string.IsNullOrEmpty(def.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
                            oreOb.MaterialTypeName = def.Id.SubtypeId;
                            var yield = (content - newContent) / 255f * def.MinedOreRatio * Definition.HarvestRatio * Session.VoxelHarvestRatio;

                            if (!Yields.TryAdd(oreOb, yield))
                                Yields[oreOb] += yield;
                        }

                        data.Content(index, (byte)newContent);
                        if (newContent == 0)
                            data.Material(index, byte.MaxValue);
                    }

                    reduction -= (byte)maxContent;
                    if (reduction <= 0)
                        break;
                }
                Session.DsUtil.Complete("calc", true);

                Session.DsUtil.Start("write");
                voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);
                Session.DsUtil.Complete("write", true);

            }
            WorkLayers.Clear();

        }

        internal void DrillLine()
        {
            //Session.DsUtil2.Start("total");
            var origin = DrillData.Origin;
            var worldForward = DrillData.Direction;
            var radius = Definition.Radius;
            var radiusSqr = radius * radius;
            var halfLenSqr = radiusSqr;
            var length = Definition.Length;
            var reduction = (int)(Definition.Speed * 255);

            var voxel = DrillData.Voxel;
            var size = voxel.Storage.Size;


            var totalLen = 0f;
            var segmentLen = 2f * radius;

            DrawBoxes.ClearImmediate();

            Vector3I testPos = new Vector3I();

            byte content;
            byte material;

            Vector3D localForward;
            var matrixNI = voxel.PositionComp.WorldMatrixNormalizedInv;
            Vector3D.TransformNormal(ref worldForward, ref matrixNI, out localForward);
            var voxelWorldExtent = Vector3D.TransformNormal((voxel as MyVoxelBase).SizeInMetresHalf, voxel.WorldMatrix);

            var maxLayer = 0;
            using ((voxel as MyVoxelBase).Pin())
            {
                while (totalLen < Definition.Length)
                {
                    totalLen += segmentLen;
                    var centreLen = totalLen - radius;

                    var worldCentre = origin + worldForward * centreLen;
                    var localCentre = Vector3D.Transform(worldCentre + voxelWorldExtent, voxel.WorldMatrixNormalizedInv);

                    var minExtent = Vector3I.Round(localCentre - radius);
                    var maxExtent = Vector3I.Round(localCentre + radius);

                    var min = Vector3I.Max(minExtent, Vector3I.Zero);
                    var max = Vector3I.Min(maxExtent, size);

                    var data = new MyStorageData();
                    data.Resize(min, max);
                    voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);

                    var foundContent = false;
                    //Session.DsUtil.Start("sort");
                    for (int i = min.X; i <= max.X; i++)
                    {
                        testPos.X = i;
                        for (int j = min.Y; j <= max.Y; j++)
                        {
                            testPos.Y = j;
                            for (int k = min.Z; k <= max.Z; k++)
                            {
                                testPos.Z = k;

                                var relativePos = testPos - min;
                                var index = data.ComputeLinear(ref relativePos);
                                if (index < 0 || index > data.SizeLinear)
                                    continue;

                                content = data.Content(index);
                                if (content == 0)
                                    continue;

                                var offset = (Vector3D)testPos - localCentre;
                                var radial = Vector3D.ProjectOnPlane(ref offset, ref localForward);
                                var radialDistSqr = (float)radial.LengthSquared();
                                if (radialDistSqr > radiusSqr)
                                    continue;
                                var axial = Vector3D.ProjectOnVector(ref offset, ref localForward);
                                var axialDistSqr = (float)axial.LengthSquared();
                                if (axialDistSqr > halfLenSqr)
                                    continue;

                                foundContent = true;
                                var dist = 0f;
                                var secondaryDistSqr = 0f;
                                switch (Definition.Pattern)
                                {
                                    case WorkOrder.InsideOut:
                                        dist = (float)radial.Length();
                                        secondaryDistSqr = axialDistSqr;
                                        break;
                                    case WorkOrder.OutsideIn:
                                        dist = radius - (float)radial.Length();
                                        secondaryDistSqr = axialDistSqr;
                                        break;
                                    case WorkOrder.Forward:
                                        dist = centreLen + (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, localForward));
                                        secondaryDistSqr = radialDistSqr;
                                        break;
                                    case WorkOrder.Backward:
                                        dist = centreLen - (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, localForward));
                                        secondaryDistSqr = radialDistSqr;
                                        break;
                                    default:
                                        break;
                                }

                                var posData = new PositionData(index, dist, secondaryDistSqr);

                                var roundDist = MathHelper.RoundToInt(dist);
                                if (roundDist > maxLayer) maxLayer = roundDist;

                                List<PositionData> layer;
                                if (WorkLayers.TryGetValue(roundDist, out layer))
                                    layer.Add(posData);
                                else
                                    WorkLayers[roundDist] = new List<PositionData>() { posData };

                                //if (Debug)
                                //{
                                //    var matrix = voxel.PositionComp.WorldMatrixRef;
                                //    matrix.Translation = voxel.PositionLeftBottomCorner;
                                //    var lowerHalf = (Vector3D)testPos - 0.5;
                                //    var upperHalf = (Vector3D)testPos + 0.5;
                                //    var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                                //    var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                                //    DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, int>(obb, StorageDatas.Count % 4));
                                //}

                            }
                        }
                    }
                    if (foundContent)
                        StorageDatas.Add(new StorageInfo(min, max));

                    //Session.DsUtil.Complete("sort", true, true);

                    //Session.DsUtil.Start("calc");
                    if ((int)Definition.Pattern <= 2)
                        reduction = (int)(Definition.Speed * 255);

                    for (int i = 0; i <= maxLayer; i++)
                    {
                        List<PositionData> layer;
                        if (!WorkLayers.TryGetValue(i, out layer))
                            continue;

                        var maxContent = 0;
                        for (int j = 0; j < layer.Count; j++)
                        {
                            var posData = layer[j];
                            var index = posData.Index;
                            var distance = posData.Distance;
                            var secondaryDistSqr = posData.SecondaryDistanceSqr;
                            //var data = posData.StorageData;

                            content = data.Content(index);
                            material = data.Material(index);

                            var removal = Math.Min(reduction, 255);

                            var limit = 1f;
                            switch (Definition.Pattern)
                            {
                                case WorkOrder.InsideOut:
                                    limit = radius - distance;
                                    break;
                                case WorkOrder.OutsideIn:
                                    limit = distance;
                                    break;
                                case WorkOrder.Forward:
                                    limit = length - distance;
                                    break;
                                default:
                                    break;
                            }
                            if (limit < 0.5f)
                            {
                                var density = MathHelper.Clamp(limit, -1, 1) * 0.5 + 0.5;
                                removal = (int)(removal * density);
                            }
                            else if (distance > i + 0.5f)
                            {
                                var edgeDist = i - distance;
                                var density = MathHelper.Clamp(edgeDist, -1, 1) * 0.5 + 0.5;
                                var leftover = reduction - removal;
                                removal = (int)(removal * density) + leftover;
                            }
                            removal = MathHelper.Clamp(removal, 0, content);
                            Hitting |= removal > 0;
                            var newContent = content - removal;
                            if (removal > maxContent) maxContent = removal;

                            var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                            if (def != null && def.CanBeHarvested && !string.IsNullOrEmpty(def.MinedOre))
                            {
                                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
                                oreOb.MaterialTypeName = def.Id.SubtypeId;
                                var yield = removal / 255f * def.MinedOreRatio * Definition.HarvestRatio * Session.VoxelHarvestRatio;

                                if (!Yields.TryAdd(oreOb, yield))
                                    Yields[oreOb] += yield;
                            }

                            data.Content(index, (byte)newContent);
                            if (newContent == 0)
                                data.Material(index, byte.MaxValue);
                        }

                        reduction -= maxContent;
                        if (reduction <= 0)
                            break;
                    }
                    WorkLayers.Clear();
                    //Session.DsUtil.Complete("calc", true, true);

                    voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);

                    if (reduction <= 0 && (int)Definition.Pattern > 2)
                        break;
                }


            }

            //Session.DsUtil2.Complete("total", true, true);

        }


        internal void OnDrillComplete()
        {
            Session.DsUtil.Start("notify");
            for (int i = StorageDatas.Count - 1; i >= 0; i--)
            {
                var info = StorageDatas[i];
                DrillData.Voxel.Storage.NotifyRangeChanged(ref info.Min, ref info.Max, MyStorageDataTypeFlags.ContentAndMaterial);
            }
            StorageDatas.Clear();
            ActiveDrillThreads--;
            Session.DsUtil.Complete("notify", true);
        }
        
        internal void ManageInventory()
        {
            var tryPush = LastPushSucceeded || ToolSession.Tick - LastPushTick > 1200;
            foreach (var ore in Yields.Keys)
            {
                var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(ore);
                var amount = (MyFixedPoint)(Yields[ore] / itemDef.Volume);
                if (tryPush)
                {
                    MyFixedPoint transferred;
                    LastPushSucceeded = Grid.ConveyorSystem.PushGenerateItem(itemDef.Id, amount, out transferred, Tool, false);
                    if (LastPushSucceeded)
                        continue;

                    amount -= transferred;
                    tryPush = false;
                }

                Inventory.AddItems(amount, ore);
            }
            Yields.Clear();

            if (tryPush && !Inventory.Empty())
            {
                var items = new List<MyPhysicalInventoryItem>(Inventory.GetItems());
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    MyFixedPoint transferred;
                    LastPushSucceeded = Grid.ConveyorSystem.PushGenerateItem(item.Content.GetId(), item.Amount, out transferred, Tool, false);
                    Inventory.RemoveItems(item.ItemId, transferred);
                    if (!LastPushSucceeded)
                        break;
                }
            }

        }

        private void GetShowInToolbarSwitch()
        {
            List<IMyTerminalControl> items;
            MyAPIGateway.TerminalControls.GetControls<IMyUpgradeModule>(out items);

            foreach (var item in items)
            {

                if (item.Id == "ShowInToolbarConfig")
                {
                    ShowInToolbarSwitch = (IMyTerminalControlOnOffSwitch)item;
                    break;
                }
            }
        }

        internal void RefreshTerminal()
        {
            Tool.RefreshCustomInfo();

            if (ShowInToolbarSwitch != null)
            {
                var originalSetting = ShowInToolbarSwitch.Getter(Tool);
                ShowInToolbarSwitch.Setter(Tool, !originalSetting);
                ShowInToolbarSwitch.Setter(Tool, originalSetting);
            }
        }

        internal void UpdateConnections()
        {

        }

        internal void Close()
        {
            Tool.EnabledChanged -= EnabledChanged;
            Tool.IsWorkingChanged -= IsWorkingChanged;

            Clean();
        }

        internal void Clean()
        {
            Grid = null;
            GridComp = null;

            ShowInToolbarSwitch = null;
        }

        public override string ComponentTypeDebugString => "ToolCore";
    }
}

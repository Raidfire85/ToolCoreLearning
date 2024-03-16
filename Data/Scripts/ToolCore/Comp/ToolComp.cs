using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Session;
using ToolCore.Utils;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using static ToolCore.Definitions.ToolDefinition;
using static ToolCore.Utils.Draw;

namespace ToolCore.Comp
{
    /// <summary>
    /// Holds all thrust block data
    /// </summary>
    internal partial class ToolComp : MyEntityComponentBase
    {
        internal readonly CoreGun GunBase;
        internal readonly MyInventory Inventory;

        internal MyEntity ToolEntity;
        internal MyEntity Parent;
        internal IMyConveyorSorter BlockTool;
        internal IMyHandheldGunObject<MyDeviceBase> HandTool;
        internal MyResourceSinkComponent Sink;
        internal MyOrientedBoundingBoxD Obb;
        internal MyEntity3DSoundEmitter SoundEmitter;
        internal MyCubeGrid Grid;
        internal GridComp GridComp;
        internal ToolRepo Repo;

        internal IMyTerminalControlOnOffSwitch ShowInToolbarSwitch;

        internal ToolMode Mode;
        internal ToolAction Action;
        internal Trigger State;
        internal Trigger AvState;
        internal TargetTypes Targets = TargetTypes.All;

        internal readonly ConcurrentDictionary<int, ConcurrentCachingList<IMySlimBlock>> HitBlockLayers = new ConcurrentDictionary<int, ConcurrentCachingList<IMySlimBlock>>();
        internal readonly ConcurrentDictionary<string, float> Yields = new ConcurrentDictionary<string, float>();
        internal readonly Dictionary<MyCubeGrid, Vector3I> ClientWorkSet = new Dictionary<MyCubeGrid, Vector3I>();

        internal readonly Dictionary<ToolMode, ModeSpecificData> ModeMap = new Dictionary<ToolMode, ModeSpecificData>();

        internal readonly Dictionary<string, IMyModelDummy> Dummies = new Dictionary<string, IMyModelDummy>();
        internal readonly Dictionary<string, MyEntitySubpart> Subparts = new Dictionary<string, MyEntitySubpart>();
        internal readonly Dictionary<IMyModelDummy, MyEntity> DummyMap = new Dictionary<IMyModelDummy, MyEntity>();

        internal readonly ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>> DrawBoxes = new ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>>();
        internal readonly List<Effects> ActiveEffects = new List<Effects>();
        internal readonly List<Action<int, bool>> EventMonitors = new List<Action<int, bool>>();
        internal readonly List<ulong> ReplicatedClients = new List<ulong>();
        internal readonly List<IMySlimBlock> WorkSet = new List<IMySlimBlock>();

        internal readonly HashSet<string> FailedPulls = new HashSet<string>();

        internal readonly bool IsBlock;
        internal readonly bool HasTargetControls;

        internal bool Enabled = true;
        internal bool Functional = true;
        internal bool Powered = true;
        internal bool FullInit;
        internal bool Dirty;
        internal bool AvActive;
        internal bool UpdatePower;
        internal bool LastPushSucceeded;

        internal bool Draw;
        internal bool UseWorkColour;
        internal bool Working;
        internal bool WasHitting;
        internal readonly Hit HitInfo = new Hit();
        internal MyStringHash HitMaterial = MyStringHash.GetOrCompute("Metal");

        internal int CompTick10;
        internal int CompTick20;
        internal int CompTick60;
        internal int CompTick120;
        internal int LastPushTick;
        internal int ActiveThreads;

        internal volatile int MaxLayer;

        internal uint WorkColourPacked;

        private Vector3 _workColour;
        internal Vector3 WorkColour
        {
            get
            {
                return _workColour;
            }
            set
            { 
                _workColour = value;

                WorkColourPacked = _workColour.PackHSVToUint();
            }
        }

        private bool _activated;

        internal bool Activated
        {
            get { return _activated; }
            set
            {
                if (_activated == value)
                    return;

                if (value && !(Functional && Powered && Enabled))
                    return;

                _activated = value;

                UpdateAvState(Trigger.Activated, value);
                if (!value)
                {
                    //WasHitting = false;
                    UpdateHitInfo(false);
                }
            }
        }

        internal ActionDefinition Values
        {
            get
            {
                var action = GunBase.Shooting ? GunBase.GunAction : Action;
                var modeData = ModeMap[Mode];
                return modeData.Definition.ActionMap[action];
            }
        }

        internal ModeSpecificData ModeData
        {
            get
            {
                return ModeMap[Mode];
            }
        }

        internal ToolComp(MyEntity tool, List<ToolDefinition> defs)
        {
            ToolEntity = tool;
            BlockTool = tool as IMyConveyorSorter;
            HandTool = tool as IMyHandheldGunObject<MyDeviceBase>;
            GunBase = new CoreGun(this);

            var debug = false;
            foreach (var def in defs)
            {
                var workTick = (int)(ToolEntity.EntityId % def.UpdateInterval);
                var data = new ModeSpecificData(def, workTick);

                foreach (var mode in def.ToolModes)
                {
                    ModeMap[mode] = data;
                }

                if (def.EffectShape == EffectShape.Cuboid)
                    Obb = new MyOrientedBoundingBoxD();

                HasTargetControls |= def.ShowTargetControls ?? (def.IsTurret || def.EffectSphere.Radius >= 50);
                debug |= def.Debug;
            }

            Mode = ModeMap.Keys.FirstOrDefault();


            CompTick10 = (int)(ToolEntity.EntityId % 10);
            CompTick20 = (int)(ToolEntity.EntityId % 20);
            CompTick60 = (int)(ToolEntity.EntityId % 60);
            CompTick120 = (int)(ToolEntity.EntityId % 120);

            if (!MyAPIGateway.Session.IsServer)
                ToolSession.Instance.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = true, PacketType = (byte)PacketType.Replicate });

            IsBlock = BlockTool != null;
            if (!IsBlock)
            {
                Parent = MyEntities.GetEntityById(HandTool.OwnerId);
                if (Parent == null)
                {
                    Logs.WriteLine("Hand tool owner null on init");
                    return;
                }

                Inventory = Parent.GetInventory(0);
                if (Inventory == null)
                    Logs.WriteLine("Hand tool owner inventory null on init");

                Draw = debug;

                return;
            }

            SinkInit();
            StorageInit();
            Inventory = (MyInventory)ToolEntity.GetInventoryBase();
            Grid = BlockTool.CubeGrid as MyCubeGrid;
            Parent = Grid;

            BlockTool.EnabledChanged += EnabledChanged;
            BlockTool.IsWorkingChanged += IsWorkingChanged;

            Enabled = BlockTool.Enabled;
            Functional = BlockTool.IsFunctional;

            if (!ToolSession.Instance.IsDedicated)
                GetShowInToolbarSwitch();
        }

        internal void FunctionalInit()
        {
            FullInit = true;

            var hasSound = false;
            foreach (var item in ModeMap)
            {
                var data = item.Value;
                if (data.FullInit)
                    continue;

                data.FullInit = true;
                var def = data.Definition;

                var effectsMap = data.EffectsMap;
                foreach (var pair in def.EventEffectDefs)
                {
                    var trigger = pair.Key;
                    var effectLists = pair.Value;

                    var effects = new Effects(effectLists.Item1, effectLists.Item2, effectLists.Item3, effectLists.Item4, this);
                    effectsMap[trigger] = effects;

                    hasSound |= effects.HasSound;
                }

                if (!def.IsTurret)
                    continue;

                data.Turret = new TurretComp(def, this, data);
            }

            LoadModels(true);

            if (hasSound)
                SoundEmitter = new MyEntity3DSoundEmitter(ToolEntity);

            UpdateAvState(Trigger.Functional, true);
        }

        internal void LoadModels(bool init = false)
        {
            GetDummiesAndSubpartsRecursive(ToolEntity);

            var functional = !IsBlock || BlockTool.IsFunctional;
            foreach (var entity in Subparts.Values)
            {
                entity.OnClose += (ent) => Dirty = true;
                entity.NeedsWorldMatrix = true;
            }
            ToolEntity.NeedsWorldMatrix = true;

            foreach (var mode in ModeMap.Values)
            {
                mode.UpdateModelData(this, init);
            }

            Dirty = false;
            Subparts.Clear();
            Dummies.Clear();
            DummyMap.Clear();
        }

        private void GetDummiesAndSubpartsRecursive(MyEntity entity)
        {
            try
            {
                ((IMyEntity)entity).Model.GetDummies(Dummies);
            }
            catch (Exception ex)
            {
                var modelName = ((IMyEntity)entity).Model.AssetName;
                var lastSlash = modelName.LastIndexOf('\\');
                Logs.WriteLine($"Failed to get dummies from {modelName.Substring(lastSlash + 1)} - probably a duplicate empty name in another scene!");
                Logs.LogException(ex);
            }

            foreach (var dummy in Dummies.Values)
            {
                if (DummyMap.ContainsKey(dummy))
                    continue;

                DummyMap.Add(dummy, entity);
            }

            var subparts = entity.Subparts;
            if (subparts == null || subparts.Count == 0)
                return;

            foreach (var item in subparts)
            {
                Subparts.Add(item.Key, item.Value);

                GetDummiesAndSubpartsRecursive(item.Value);
            }
        }

        internal void ChangeGrid()
        {
            Grid = (MyCubeGrid)BlockTool.CubeGrid;
        }

        internal class TurretComp
        {
            internal readonly List<IMySlimBlock> Targets = new List<IMySlimBlock>();

            internal readonly ToolComp Comp;
            internal readonly TurretDefinition Definition;
            internal readonly TurretPart Part1;
            internal readonly TurretPart Part2;

            internal readonly int UpdateTick;
            internal readonly bool HasTwoParts = true;

            internal IMySlimBlock ActiveTarget;
            internal bool HasTarget;
            internal bool IsValid;

            internal void UpdateModelData(ToolComp comp)
            {
                IsValid = Part1.UpdateModelData(comp) && (!HasTwoParts || Part2.UpdateModelData(comp));
            }

            internal TurretComp(ToolDefinition def, ToolComp comp, ModeSpecificData data)
            {
                Comp = comp;
                Definition = def.Turret;
                var halfInterval = (int)(0.5f * def.UpdateInterval);
                UpdateTick = (int)((Comp.ToolEntity.EntityId + halfInterval) % def.UpdateInterval);

                var subparts = def.Turret.Subparts;
                var partDefA = subparts[0];
                TurretPart partA;
                var partAValid = SetupPart(partDefA, out partA);
                if (subparts.Count == 1)
                {
                    Part1 = partA;
                    IsValid = partAValid;
                    HasTwoParts = false;
                    return;
                }

                var partDefB = subparts[1];
                TurretPart partB;
                var partBValid = SetupPart(partDefB, out partB);
                if (!partAValid || !partBValid)
                {
                    IsValid = false;
                    return;
                }

                MyEntitySubpart _;

                if (partA.Subpart.TryGetSubpartRecursive(partB.Definition.Name, out _))
                {
                    Part1 = partA;
                    Part2 = partB;
                    IsValid = true;
                    return;
                }

                if (partB.Subpart.TryGetSubpartRecursive(partA.Definition.Name, out _))
                {
                    subparts.Move(1, 0);

                    Part1 = partB;
                    Part2 = partA;
                    IsValid = true;
                    return;
                }

                Logs.WriteLine("Neither specified turret subpart is a child of the other!");
                IsValid = false;

            }

            private bool SetupPart(TurretDefinition.TurretPartDef partDef, out TurretPart part)
            {
                part = null;
                MyEntitySubpart subpart;
                if (!Comp.ToolEntity.TryGetSubpartRecursive(partDef.Name, out subpart))
                {
                    Logs.WriteLine($"Failed to find turret subpart {partDef.Name}");
                    return false;
                }

                part = new TurretPart(partDef);
                part.Subpart = subpart;
                part.Parent = subpart.Parent;
                MyEntity _;
                part.Parent.TryGetDummy("subpart_" + partDef.Name, out part.Empty, out _);
                part.Axis = part.Empty.Matrix.Forward;

                return true;
            }

            internal bool TrackTarget()
            {
                var target = ActiveTarget;
                var targetWorld = target.CubeGrid.GridIntegerToWorld(target.Position);

                Vector3D targetLocal;
                var parentMatrixNI = Part1.Parent.PositionComp.WorldMatrixNormalizedInv;
                Vector3D.Transform(ref targetWorld, ref parentMatrixNI, out targetLocal);
                var targetVector = targetLocal - Part1.Subpart.PositionComp.LocalMatrixRef.Translation;

                var desiredFacing = (Vector3)Vector3D.ProjectOnPlane(ref targetVector, ref Part1.Axis);
                Part1.DesiredFacing = desiredFacing;
                var desiredAngle1 = (float)Vector3.Angle(desiredFacing, Part1.Facing) * Math.Sign(Vector3.Dot(desiredFacing, Part1.Normal));
                if (Part1.Definition.RotationCapped && (desiredAngle1 > Part1.Definition.MaxRotation || desiredAngle1 < Part1.Definition.MinRotation))
                {
                    Logs.WriteLine("Tracking false");
                    return false;
                }

                if (HasTwoParts)
                {
                    parentMatrixNI = Part2.Parent.PositionComp.WorldMatrixNormalizedInv;
                    Vector3D.Transform(ref targetWorld, ref parentMatrixNI, out targetLocal);
                    targetVector = targetLocal - Part2.Subpart.PositionComp.LocalMatrixRef.Translation;

                    desiredFacing = (Vector3)Vector3D.ProjectOnPlane(ref targetVector, ref Part2.Axis);
                    Part2.DesiredFacing = desiredFacing;
                    var desiredAngle2 = (float)Vector3.Angle(desiredFacing, Part2.Facing) * Math.Sign(Vector3.Dot(desiredFacing, Part2.Normal));
                    if (Part2.Definition.RotationCapped && (desiredAngle2 > Part2.Definition.MaxRotation || desiredAngle2 < Part2.Definition.MinRotation))
                    {
                        Logs.WriteLine("Tracking false");
                        return false;
                    }

                    Part2.DesiredRotation = desiredAngle2;
                }

                Part1.DesiredRotation = desiredAngle1;

                //Logs.WriteLine("Tracking true");
                return true;
            }

            internal void SelectNewTarget(Vector3D worldPos)
            {
                Logs.WriteLine($"Selecting from {Targets.Count} targets");
                for (int i = Targets.Count - 1; i >= 0; i--)
                {
                    var next = Targets[i];
                    var projector = ((MyCubeGrid)next.CubeGrid).Projector as IMyProjector;

                    var closing = next.CubeGrid.MarkedForClose || next.FatBlock != null && next.FatBlock.MarkedForClose;
                    var finished = next.IsFullyDismounted || Comp.Mode == ToolMode.Weld && projector == null && next.IsFullIntegrity && !next.HasDeformation;
                    var outOfRange = Vector3D.DistanceSquared(next.CubeGrid.GridIntegerToWorld(next.Position), worldPos) > Definition.TargetRadiusSqr;
                    if (closing || finished || outOfRange || projector?.CanBuild(next, true) != BuildCheckResult.OK)
                    {
                        Targets.RemoveAtFast(i);
                        continue;
                    }

                    HasTarget = true;
                    ActiveTarget = next;
                    break;
                }
                Logs.WriteLine($"{Targets.Count} targets remaining");
            }

            internal void RefreshTargetList(ToolDefinition def, Vector3D worldPos)
            {
                var ownerId = Comp.IsBlock ? Comp.BlockTool.OwnerId : Comp.HandTool.OwnerIdentityId;
                var toolFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                def.EffectSphere.Center = worldPos;

                var session = ToolSession.Instance;
                var entities = session.Entities;
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref def.EffectSphere, entities);
                foreach (var entity in entities)
                {
                    if (!(entity is MyCubeGrid))
                        continue;

                    var grid = entity as MyCubeGrid;

                    if (Comp.IsBlock && !def.AffectOwnGrid && grid == Comp.Grid || !grid.Editable)
                        continue;

                    if (Comp.HasTargetControls)
                    {
                        var relation = Comp.GetRelationToGrid(grid, toolFaction);
                        if ((relation & Comp.Targets) == TargetTypes.None)
                            continue;
                    }

                    if (Comp.Mode != ToolMode.Weld && (grid.Immune || !grid.DestructibleBlocks || grid.Projector != null || grid.Physics == null || !grid.Physics.Enabled))
                        continue;

                    var toolData = session.ToolDataPool.Count > 0 ? session.ToolDataPool.Pop() : new ToolData();
                    toolData.Entity = entity;
                    toolData.Position = worldPos;

                    Comp.ActiveThreads++;
                    MyAPIGateway.Parallel.Start(Comp.GetBlockTargets, Comp.OnGetBlockTargetsComplete, toolData);
                }

                entities.Clear();
            }

            internal class TurretPart
            {
                internal readonly TurretDefinition.TurretPartDef Definition;

                internal Func<float, Matrix> RotationFactory;

                internal MyEntitySubpart Subpart;
                internal MyEntity Parent;
                internal IMyModelDummy Empty;
                internal Vector3 Position;
                internal Vector3D Axis;
                internal Vector3 Facing;
                internal Vector3 Normal;

                internal Vector3 DesiredFacing;

                internal float CurrentRotation;
                internal float DesiredRotation;

                internal TurretPart(TurretDefinition.TurretPartDef def)
                {
                    Definition = def;
                }

                internal bool UpdateModelData(ToolComp comp)
                {
                    if (!comp.Subparts.TryGetValue(Definition.Name, out Subpart))
                        return false;

                    if (!comp.Dummies.TryGetValue("subpart_" + Definition.Name, out Empty))
                        return false;

                    Parent = comp.DummyMap[Empty];

                    Position = Empty.Matrix.Translation;

                    switch (Definition.RotationAxis)
                    {
                        case Direction.Up:
                            RotationFactory = Matrix.CreateRotationY;
                            Axis = Subpart.PositionComp.LocalMatrixRef.Up;
                            Facing = Subpart.PositionComp.LocalMatrixRef.Forward;
                            Normal = Subpart.PositionComp.LocalMatrixRef.Left;
                            break;
                        case Direction.Forward:
                            RotationFactory = Matrix.CreateRotationZ;
                            Axis = Subpart.PositionComp.LocalMatrixRef.Forward;
                            Facing = Subpart.PositionComp.LocalMatrixRef.Up;
                            Normal = Subpart.PositionComp.LocalMatrixRef.Right;
                            break;
                        case Direction.Right:
                            RotationFactory = Matrix.CreateRotationX;
                            Axis = Subpart.PositionComp.LocalMatrixRef.Right;
                            Facing = Subpart.PositionComp.LocalMatrixRef.Forward;
                            Normal = Subpart.PositionComp.LocalMatrixRef.Up;
                            break;
                    }

                    DesiredRotation = 0;
                    CurrentRotation = 0;

                    Logs.WriteLine("Loaded turret models");

                    return true;
                }
            }
        }

        internal class ModeSpecificData
        {
            internal readonly ToolDefinition Definition;
            internal readonly Dictionary<Trigger, Effects> EffectsMap = new Dictionary<Trigger, Effects>();

            internal readonly int WorkTick;

            internal TurretComp Turret;
            internal MyEntity MuzzlePart;
            internal IMyModelDummy Muzzle;

            internal bool FullInit;
            internal bool HasEmitter;

            internal ModeSpecificData(ToolDefinition def, int workTick)
            {
                Definition = def;
                WorkTick = workTick;
            }

            internal void UpdateModelData(ToolComp comp, bool init = false)
            {
                if (!init)
                {
                    if (!ToolSession.Instance.IsDedicated)
                    {
                        foreach (var effects in EffectsMap.Values)
                        {
                            effects.UpdateModelData(comp);
                        }
                    }
                }

                if (Definition.IsTurret)
                    Turret.UpdateModelData(comp);

                if (string.IsNullOrEmpty(Definition.EmitterName) || !comp.Dummies.TryGetValue(Definition.EmitterName, out Muzzle))
                    return;

                HasEmitter = true;
                MuzzlePart = comp.DummyMap[Muzzle];

                var functional = !comp.IsBlock || comp.BlockTool.IsFunctional;
                if (!HasEmitter && functional && Definition.Location == Location.Emitter)
                    Definition.Location = Location.Centre;
            }
        }

        internal class ToolData : WorkData
        {
            internal MyEntity Entity;
            internal Vector3D Position;
            internal Vector3D Forward;
            internal Vector3D Up;
            internal float RayLength;

            internal readonly HashSet<IMySlimBlock> HitBlocksHash = new HashSet<IMySlimBlock>();

            internal void Clean()
            {
                Entity = null;
                HitBlocksHash.Clear();
            }
        }

        internal class PositionData
        {
            internal int Index;
            internal float Distance;
            internal float Distance2;
            internal bool Contained;
            internal Vector3D Position;
            internal StorageInfo StorageInfo;

            public PositionData(int index, float distance, StorageInfo info)
            {
                Index = index;
                Distance = distance;
                StorageInfo = info;
            }

            public PositionData(int index, float distance, float distance2)
            {
                Index = index;
                Distance = distance;
                Distance2 = distance2;
            }

            public PositionData(int index, float distance, float distance2, StorageInfo info)
            {
                Index = index;
                Distance = distance;
                Distance2 = distance2;
                StorageInfo = info;
            }

            public PositionData(int index, float distance, Vector3D position, bool contained)
            {
                Index = index;
                Distance = distance;
                Position = position;
                Contained = contained;
            }
        }

        internal class StorageInfo
        {
            internal Vector3I Min;
            internal Vector3I Max;
            internal bool Dirty;

            public StorageInfo(Vector3I min, Vector3I max, bool dirty = false)
            {
                Min = min;
                Max = max;
                Dirty = dirty;
            }
        }

        internal enum ToolMode
        {
            Drill = 4,
            Weld = 8,
            Grind = 16,
        }

        internal enum ToolAction
        {
            Primary = 0,
            Secondary = 1,
            Tertiary = 2,
        }

        [Flags]
        internal enum TargetTypes : byte
        {
            None = 0,
            Own = 1,
            Friendly = 2,
            Neutral = 4,
            Hostile = 8,
            All = 15,
        }

        internal class Hit
        {
            internal Vector3D Position;
            internal MyStringHash Material;
            internal bool IsValid;
        }

        internal void UpdateHitInfo(bool valid, Vector3D? pos = null, MyStringHash? material = null)
        {
            if (valid)
            {
                HitInfo.Position = pos.Value;
                HitInfo.Material = material.Value;

                if (HitInfo.IsValid)
                    return;

                UpdateAvState(Trigger.RayHit, true);
                HitInfo.IsValid = true;
                return;
            }

            if (!HitInfo.IsValid)
                return;

            UpdateAvState(Trigger.RayHit, false);
            HitInfo.IsValid = false;
        }

        internal class Effects
        {
            internal readonly bool HasAnimations;
            internal readonly bool HasParticles;
            internal readonly bool HasBeams;
            internal readonly bool HasSound;
            internal readonly List<Animation> Animations;
            internal readonly List<ParticleEffect> ParticleEffects;
            internal readonly List<Beam> Beams;
            internal readonly SoundDef SoundDef;

            internal bool Active;
            internal bool Expired;
            internal bool Dirty;
            internal bool Restart;
            internal bool SoundStopped;
            internal int LastActiveTick;

            internal Effects(List<AnimationDef> animationDefs, List<ParticleEffectDef> particleEffectDefs, List<BeamDef> beamDefs, SoundDef soundDef, ToolComp comp)
            {
                var tool = comp.ToolEntity;

                if (animationDefs?.Count > 0)
                {
                    Animations = new List<Animation>();
                    foreach (var aDef in animationDefs)
                    {
                        MyEntitySubpart subpart = null;
                        if (!tool.TryGetSubpartRecursive(aDef.Subpart, out subpart))
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
                        IMyModelDummy dummy = null;
                        MyEntity parent = tool;
                        if (pDef.Location == Location.Emitter && !tool.TryGetDummy(pDef.Dummy, out dummy, out parent))
                        {
                            Logs.WriteLine($"Dummy '{pDef.Dummy}' not found!");
                            continue;
                        }

                        var effect = new ParticleEffect(pDef, dummy, parent);
                        ParticleEffects.Add(effect);
                    }
                    HasParticles = ParticleEffects.Count > 0;
                }

                if (beamDefs?.Count > 0)
                {
                    Beams = new List<Beam>();
                    foreach (var beamDef in beamDefs)
                    {
                        IMyModelDummy start = null;
                        MyEntity startParent = null;
                        if (!tool.TryGetDummy(beamDef.Start, out start, out startParent))
                        {
                            Logs.WriteLine($"Dummy '{beamDef.Start}' not found!");
                            continue;
                        }

                        IMyModelDummy end = null;
                        MyEntity endParent = null;
                        if (beamDef.EndLocation == Location.Emitter && !tool.TryGetDummy(beamDef.End, out end, out endParent))
                        {
                            Logs.WriteLine($"Dummy '{beamDef.End}' not found!");
                            continue;
                        }

                        var beam = new Beam(beamDef, start, end, startParent, endParent);
                        Beams.Add(beam);
                    }
                    HasBeams = Beams.Count > 0;
                }

                HasSound = (SoundDef = soundDef) != null;
            }

            internal void UpdateModelData(ToolComp comp)
            {
                if (HasAnimations)
                {
                    foreach (var anim in Animations)
                    {
                        MyEntitySubpart subpart;
                        if (comp.Subparts.TryGetValue(anim.Definition.Subpart, out subpart))
                        {
                            anim.Subpart = subpart;
                        }
                    }
                }

                if (HasParticles)
                {
                    foreach (var particle in ParticleEffects)
                    {
                        IMyModelDummy dummy;
                        if (particle.Definition.Location == Location.Emitter && comp.Dummies.TryGetValue(particle.Definition.Dummy, out dummy))
                        {
                            particle.Dummy = dummy;
                            particle.Parent = comp.DummyMap[dummy];
                        }
                    }
                }

                if (HasBeams)
                {
                    foreach (var beam in Beams)
                    {
                        IMyModelDummy start;
                        if (comp.Dummies.TryGetValue(beam.Definition.Start, out start))
                        {
                            beam.Start = start;
                            beam.StartParent = comp.DummyMap[start];
                        }

                        if (beam.Definition.EndLocation != Location.Emitter)
                            continue;

                        IMyModelDummy end;
                        if (comp.Dummies.TryGetValue(beam.Definition.End, out end))
                        {
                            beam.End = end;
                            beam.EndParent = comp.DummyMap[end];
                        }
                    }
                }
            }

            internal void Clean()
            {
                Active = false;
                Expired = false;
                Dirty = false;
                Restart = false;
                SoundStopped = false;
                LastActiveTick = 0;
            }

            internal class Animation
            {
                internal readonly AnimationDef Definition;

                internal MyEntitySubpart Subpart;

                internal bool Starting;
                internal bool Running;
                internal bool Ending;
                internal int RemainingDuration;
                internal int TransitionState;

                public Animation(AnimationDef def, MyEntitySubpart subpart)
                {
                    Definition = def;
                    Subpart = subpart;
                }
            }

            internal class ParticleEffect
            {
                internal readonly ParticleEffectDef Definition;

                internal IMyModelDummy Dummy;
                internal MyEntity Parent;
                internal MyParticleEffect Particle;

                public ParticleEffect(ParticleEffectDef def, IMyModelDummy dummy, MyEntity parent)
                {
                    Dummy = dummy;
                    Parent = parent;
                    Definition = def;
                }
            }

            internal class Beam
            {
                internal readonly BeamDef Definition;

                internal IMyModelDummy Start;
                internal IMyModelDummy End;
                internal MyEntity StartParent;
                internal MyEntity EndParent;

                public Beam(BeamDef def, IMyModelDummy start, IMyModelDummy end, MyEntity startParent, MyEntity endParent)
                {
                    Definition = def;
                    Start = start;
                    End = end;
                    StartParent = startParent;
                    EndParent = endParent;
                }

            }
        }

        internal TargetTypes GetRelationToGrid(MyCubeGrid grid, IMyFaction toolFaction)
        {
            var ownerId = IsBlock ? BlockTool.OwnerId : HandTool.OwnerIdentityId;
            var targetOwner = grid.Projector?.OwnerId ?? grid.BigOwners.FirstOrDefault();
            if (ownerId == targetOwner)
            {
                return TargetTypes.Own;
            }

            if (ownerId == 0 || targetOwner == 0)
            {
                return TargetTypes.Neutral;
            }

            if (toolFaction == null)
            {
                return TargetTypes.Hostile;
            }

            var targetFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(targetOwner);
            if (targetFaction == null)
            {
                return TargetTypes.Hostile;
            }

            if (toolFaction == targetFaction)
            {
                return TargetTypes.Friendly;
            }

            var factionRelation = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(toolFaction.FactionId, targetFaction.FactionId);
            if (factionRelation == MyRelationsBetweenFactions.Enemies)
            {
                return TargetTypes.Hostile;
            }

            if (factionRelation == MyRelationsBetweenFactions.Friends)
            {
                return TargetTypes.Friendly;
            }

            return TargetTypes.Neutral;
        }

        internal void SetMode(ToolMode newMode)
        {
            var oldData = ModeMap[Mode];
            var newData = ModeMap[newMode];

            Mode = newMode;

            if (oldData == newData)
                return;

            foreach (var effects in oldData.EffectsMap.Values)
            {
                effects.Expired = effects.Active;
            }

            foreach (var map in newData.EffectsMap)
            {
                var trigger = map.Key;
                if ((trigger & AvState) == 0)
                    continue;

                var effects = map.Value;
                if (!effects.Active)
                {
                    ActiveEffects.Add(effects);
                    effects.Active = true;
                    continue;
                }

                if (effects.Expired)
                {
                    effects.Expired = false;
                    effects.SoundStopped = false;
                    effects.Restart = true;
                }
            }
        }

        internal void UpdateAvState(Trigger state, bool add)
        {
            //Logs.WriteLine($"UpdateAvState() {state} {add} {force}");
            var data = ModeData;

            var keepFiring = !add && (Activated || GunBase.Shooting) && (state & Trigger.Firing) > 0;

            foreach (var flag in ToolSession.Instance.Triggers)
            {
                if (add && (flag & state) == 0)
                    continue;

                if (keepFiring || flag < state)
                    continue;

                if (add) State |= flag;
                else State &= ~flag;

                if ((flag & data.Definition.EventFlags) == 0)
                    continue;

                if (add) AvState |= flag;
                else AvState &= ~flag;

                foreach (var monitor in EventMonitors)
                    monitor.Invoke((int)state, add);

                UpdateEffects(flag, add);

                if (!add) // maybe remove this later :|
                {
                    if (flag == Trigger.Hit) WasHitting = false;
                    if (flag == Trigger.RayHit) HitInfo.IsValid = false;
                }
            }

            //foreach (var flag in ModeData.Definition.Triggers)
            //{
            //    //Logs.WriteLine($"Checking flag {flag}");
            //    if (add && (flag & state) == 0)
            //        continue;

            //    if (keepFiring || flag < state)
            //        continue;

            //    //Logs.WriteLine($"Current state: {AvState}");

            //    if (add) AvState |= flag;
            //    else AvState &= ~flag;

            //    //Logs.WriteLine($"New state: {AvState}");

            //    foreach (var monitor in EventMonitors)
            //        monitor.Invoke((int)state, add);

            //    //Logs.WriteLine($"UpdateEffects() {flag} {add}");
            //    UpdateEffects(flag, add);

            //    if (!add) // maybe remove this later :|
            //    {
            //        if (flag == Trigger.Hit) WasHitting = false;
            //        if (flag == Trigger.RayHit) HitInfo.IsValid = false;
            //    }
            //}
        }

        internal void UpdateEffects(Trigger state, bool add)
        {
            if (ToolSession.Instance.IsDedicated) return; //TEMPORARY!!! or not?

            Effects effects;
            if (!ModeData.EffectsMap.TryGetValue(state, out effects))
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
                effects.SoundStopped = false;
                effects.Restart = true;
            }
        }

        internal bool IsPowered()
        {
            if (Sink == null)
            {
                return Powered = false;
            }

            Sink.Update();
            var required = RequiredInput();
            var elec = MyResourceDistributorComponent.ElectricityId;
            var distributor = (MyResourceDistributorComponent)(BlockTool.CubeGrid).ResourceDistributor;
            var isPowered = MyUtils.IsEqual(required, 0f) || Sink.IsPoweredByType(elec) && (Sink.ResourceAvailableByType(elec) >= required || distributor != null && distributor.MaxAvailableResourceByType(elec) >= required);

            return Powered = isPowered;
        }

        private void EnabledChanged(IMyTerminalBlock block)
        {
            Enabled = (block as IMyFunctionalBlock).Enabled;

            Sink.Update();
            UpdatePower = true;

            if (!Enabled)
            {
                WasHitting = false;
                UpdateHitInfo(false);
            }

            if (!Powered) return;

            UpdateAvState(Trigger.Enabled, Enabled);
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

            if (!MyAPIGateway.Session.IsServer)
                ToolSession.Instance.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = true, PacketType = (byte)PacketType.Replicate });
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();

            Close();
        }

        public override bool IsSerialized()
        {
            if (ToolEntity.Storage == null || Repo == null) return false;

            Repo.Sync(this);
            ToolEntity.Storage[ToolSession.Instance.CompDataGuid] = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Repo));

            return false;
        }

        private void SinkInit()
        {
            var sinkInfo = new MyResourceSinkInfo()
            {
                MaxRequiredInput = ModeData.Definition.ActivePower,
                RequiredInputFunc = RequiredInput,
                ResourceTypeId = MyResourceDistributorComponent.ElectricityId
            };

            Sink = ToolEntity.Components?.Get<MyResourceSinkComponent>();
            if (Sink != null)
            {
                Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, RequiredInput);
            }
            else
            {
                Logs.WriteLine("No sink found on init, creating!");
                Sink = new MyResourceSinkComponent();
                Sink.Init(MyStringHash.GetOrCompute("Defense"), sinkInfo);
                ToolEntity.Components.Add(Sink);
            }

            var distributor = (MyResourceDistributorComponent)BlockTool.CubeGrid.ResourceDistributor;
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
            if (!Functional || !Enabled)
                return 0f;

            if (Activated || GunBase.WantsToShoot)
                return ModeData.Definition.ActivePower;

            return ModeData.Definition.IdlePower;
        }

        private void StorageInit()
        {
            string rawData;
            ToolRepo loadRepo = null;
            if (ToolEntity.Storage == null)
            {
                ToolEntity.Storage = new MyModStorageComponent();
            }
            else if (ToolEntity.Storage.TryGetValue(ToolSession.Instance.CompDataGuid, out rawData))
            {
                try
                {
                    var base64 = Convert.FromBase64String(rawData);
                    loadRepo = MyAPIGateway.Utilities.SerializeFromBinary<ToolRepo>(base64);
                }
                catch (Exception ex)
                {
                    Logs.LogException(ex);
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
            Action = (ToolAction)repo.Action;
            Targets = (TargetTypes)repo.Targets;
            UseWorkColour = repo.UseWorkColour;
            WorkColour = repo.WorkColour;
        }

        internal void OnDrillComplete(WorkData data)
        {
            var session = ToolSession.Instance;
            session.DsUtil.Start("notify");
            var drillData = (DrillData)data;
            var storageDatas = drillData.StorageDatas;
            if (drillData?.Voxel?.Storage == null)
            {
                Logs.WriteLine($"Null reference in OnDrillComplete - DrillData null: {drillData == null} - Voxel null: {drillData?.Voxel == null}");
            }
            for (int i = storageDatas.Count - 1; i >= 0; i--)
            {
                var info = storageDatas[i];
                if (!info.Dirty)
                    continue;

                drillData?.Voxel?.Storage?.NotifyRangeChanged(ref info.Min, ref info.Max, MyStorageDataTypeFlags.ContentAndMaterial);
            }

            drillData.Clean();
            session.DrillDataPool.Push(drillData);

            session.DsUtil.Complete("notify", true);

            ActiveThreads--;
            if (ActiveThreads > 0) return;

            var isHitting = Functional && Powered && Enabled && Working && (Activated || GunBase.Shooting);
            if (isHitting != WasHitting)
            {
                UpdateAvState(Trigger.Hit, isHitting);
                WasHitting = isHitting;

                if (ModeData.Definition.Debug && !isHitting)
                {
                    Logs.WriteLine("read: " + session.DsUtil.GetValue("read").ToString());
                    Logs.WriteLine("sort: " + session.DsUtil.GetValue("sort").ToString());
                    Logs.WriteLine("calc: " + session.DsUtil.GetValue("calc").ToString());
                    Logs.WriteLine("write: " + session.DsUtil.GetValue("write").ToString());
                    Logs.WriteLine("notify: " + session.DsUtil.GetValue("notify").ToString());
                    session.DsUtil.Clean();
                }
            }
            Working = false;
        }

        internal void ManageInventory()
        {
            var tryPush = IsBlock && (LastPushSucceeded || ToolSession.Tick - LastPushTick > 1200);

            foreach (var ore in Yields.Keys)
            {
                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(ore);
                var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(oreOb);
                var amount = (MyFixedPoint)(Yields[ore] / itemDef.Volume);
                if (tryPush)
                {
                    MyFixedPoint transferred;
                    LastPushSucceeded = Grid.ConveyorSystem.PushGenerateItem(itemDef.Id, amount, out transferred, BlockTool, false);
                    if (LastPushSucceeded)
                        continue;

                    amount -= transferred;
                    tryPush = false;
                }

                Inventory.AddItems(amount, oreOb);
            }
            Yields.Clear();

            if (tryPush && !Inventory.Empty())
            {
                var items = new List<MyPhysicalInventoryItem>(Inventory.GetItems());
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    MyFixedPoint transferred;
                    LastPushSucceeded = Grid.ConveyorSystem.PushGenerateItem(item.Content.GetId(), item.Amount, out transferred, BlockTool, false);
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
            BlockTool.RefreshCustomInfo();

            if (ShowInToolbarSwitch != null)
            {
                var originalSetting = ShowInToolbarSwitch.Getter(BlockTool);
                ShowInToolbarSwitch.Setter(BlockTool, !originalSetting);
                ShowInToolbarSwitch.Setter(BlockTool, originalSetting);
            }
        }

        internal void Close()
        {
            if (!MyAPIGateway.Session.IsServer)
                ToolSession.Instance.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = false, PacketType = (byte)PacketType.Replicate });

            Clean();

            if (IsBlock)
            {
                BlockTool.EnabledChanged -= EnabledChanged;
                BlockTool.IsWorkingChanged -= IsWorkingChanged;

                return;
            }

            ToolSession.Instance.HandTools.Remove(this);
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

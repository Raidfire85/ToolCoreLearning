using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using static ToolCore.Definitions.ToolDefinition;
using static VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GameDefinition;

namespace ToolCore.Comp
{
    /// <summary>
    /// Holds all thrust block data
    /// </summary>
    internal partial class ToolComp : MyEntityComponentBase
    {
        internal readonly ToolSession Session;

        internal readonly CoreGun GunBase;
        internal readonly MyInventory Inventory;

        internal ToolDefinition Definition;
        internal MyEntity ToolEntity;
        internal MyEntity Parent;
        internal IMyConveyorSorter BlockTool;
        internal IMyHandheldGunObject<MyDeviceBase> HandTool;
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
        internal ToolAction Action;
        internal Trigger AvState;

        internal readonly Dictionary<Trigger, Effects> EventEffects = new Dictionary<Trigger, Effects>();
        internal readonly List<Effects> ActiveEffects = new List<Effects>();
        internal readonly ConcurrentDictionary<MyObjectBuilder_Ore, float> Yields = new ConcurrentDictionary<MyObjectBuilder_Ore, float>();
        internal readonly List<ulong> ReplicatedClients = new List<ulong>();
        internal readonly List<Action<int, bool>> EventMonitors = new List<Action<int, bool>>();

        internal readonly HashSet<Vector3I> PreviousPositions = new HashSet<Vector3I>();
        internal readonly ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>> DrawBoxes = new ConcurrentCachingList<MyTuple<MyOrientedBoundingBoxD, Color>>();
        internal readonly ConcurrentDictionary<IMySlimBlock, float> HitBlocks = new ConcurrentDictionary<IMySlimBlock, float>();
        internal readonly List<IMySlimBlock> HitBlocksSorted = new List<IMySlimBlock>();
        internal readonly HashSet<IMySlimBlock> WorkSet = new HashSet<IMySlimBlock>();

        internal bool Enabled = true;
        internal bool Functional = true;
        internal bool Powered = true;
        internal bool Dirty;
        internal bool AvActive;
        internal bool UpdatePower;
        internal bool LastPushSucceeded;

        internal bool Working;
        internal bool WasHitting;
        internal readonly Hit HitInfo = new Hit();
        internal MyStringHash HitMaterial = MyStringHash.GetOrCompute("Metal");

        internal bool IsBlock;
        internal bool HasEmitter;
        internal bool Draw;

        internal int WorkTick;
        internal int CompTick10;
        internal int CompTick20;
        internal int CompTick60;
        internal int CompTick120;
        internal int LastPushTick;
        internal int ActiveThreads;

        internal ToolComp(MyEntity tool, ToolDefinition def, ToolSession session)
        {
            Session = session;

            Definition = def;
            ToolEntity = tool;
            BlockTool = tool as IMyConveyorSorter;
            HandTool = tool as IMyHandheldGunObject<MyDeviceBase>;
            GunBase = new CoreGun(this);

            if (Definition.EffectShape == EffectShape.Cuboid)
                Obb = new MyOrientedBoundingBoxD();

            var type = (int)Definition.ToolType;
            Mode = type < 2 ? ToolMode.Drill : type < 4 ? ToolMode.Grind : ToolMode.Weld;

            var hasSound = false;
            foreach (var pair in def.EventEffectDefs)
            {
                var myTuple = pair.Value;
                EventEffects[pair.Key] = new Effects(myTuple.Item1, myTuple.Item2, myTuple.Item3, myTuple.Item4, this);
                hasSound = myTuple.Item3 != null;
            }

            if (hasSound)
                SoundEmitter = new MyEntity3DSoundEmitter(ToolEntity);

            WorkTick = (int)(ToolEntity.EntityId % def.UpdateInterval);
            CompTick10 = (int)(ToolEntity.EntityId % 10);
            CompTick20 = (int)(ToolEntity.EntityId % 20);
            CompTick60 = (int)(ToolEntity.EntityId % 60);
            CompTick120 = (int)(ToolEntity.EntityId % 120);

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

                Draw = def.Debug;

                return;
            }

            Inventory = (MyInventory)ToolEntity.GetInventoryBase();
            Grid = BlockTool.CubeGrid as MyCubeGrid;
            Parent = Grid;

            BlockTool.EnabledChanged += EnabledChanged;
            BlockTool.IsWorkingChanged += IsWorkingChanged;

            Enabled = BlockTool.Enabled;
            Functional = BlockTool.IsFunctional;

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

            public PositionData(int index, float distance, float distance2 = 0f)
            {
                Index = index;
                Distance = distance;
                Distance2 = distance2;
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

            public StorageInfo(Vector3I min, Vector3I max)
            {
                Min = min;
                Max = max;
            }
        }

        internal ActionDefinition Values
        {
            get 
            {
                var action = GunBase.Shooting ? GunBase.GunAction : Action;
                return Definition.ActionMap[action];
            }
        }

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

                UpdateState(Trigger.Activated, value);
                if (!value)
                {
                    //WasHitting = false;
                    UpdateHitInfo(false);
                }
            }
        }

        private bool _activated;

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

                UpdateState(Trigger.RayHit, true);
                HitInfo.IsValid = true;
                return;
            }

            if (!HitInfo.IsValid)
                return;

            UpdateState(Trigger.RayHit, false);
            HitInfo.IsValid = false;
        }

        internal void ReloadModels()
        {
            foreach (var effect in EventEffects.Values)
            {
                if (effect.HasAnimations)
                {
                    foreach (var anim in effect.Animations)
                    {
                        MyEntitySubpart subpart;
                        if (ToolEntity.TryGetSubpartRecursive(anim.Definition.Subpart, out subpart))
                            anim.Subpart = subpart;
                    }
                }

                if (effect.HasParticles)
                {
                    foreach (var particle in effect.ParticleEffects)
                    {
                        IMyModelDummy dummy;
                        MyEntity parent;
                        if (ToolEntity.TryGetDummy(particle.Definition.Dummy, out dummy, out parent))
                        {
                            particle.Dummy = dummy;
                            particle.Parent = parent;
                        }
                    }
                }

                if (effect.HasBeams)
                {
                    foreach (var beam in effect.Beams)
                    {
                        IMyModelDummy start;
                        MyEntity startParent;
                        if (ToolEntity.TryGetDummy(beam.Definition.Start, out start, out startParent))
                        {
                            beam.Start = start;
                            beam.StartParent = startParent;
                        }

                        if (beam.Definition.EndLocation != Location.Emitter)
                            continue;

                        IMyModelDummy end;
                        MyEntity endParent;
                        if (ToolEntity.TryGetDummy(beam.Definition.End, out end, out endParent))
                        {
                            beam.End = end;
                            beam.EndParent = endParent;
                        }
                    }
                }
            }
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
            internal int LastActiveTick;

            internal Effects(List<AnimationDef> animationDefs, List<ParticleEffectDef> particleEffectDefs, List<BeamDef> beamDefs, SoundDef soundDef, ToolComp comp)
            {
                var tool = comp.ToolEntity;
                var functional = comp.IsBlock ? ((MyCubeBlock)comp.BlockTool).IsFunctional : true;

                if (animationDefs?.Count > 0)
                {
                    Animations = new List<Animation>();
                    foreach (var aDef in animationDefs)
                    {
                        MyEntitySubpart subpart = null;
                        if (functional && !tool.TryGetSubpartRecursive(aDef.Subpart, out subpart))
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
                        if (pDef.Location == Location.Emitter && functional && !tool.TryGetDummy(pDef.Dummy, out dummy, out parent))
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
                        if (functional && !tool.TryGetDummy(beamDef.Start, out start, out startParent))
                        {
                            Logs.WriteLine($"Dummy '{beamDef.Start}' not found!");
                            continue;
                        }

                        IMyModelDummy end = null;
                        MyEntity endParent = null;
                        if (beamDef.EndLocation == Location.Emitter && functional && !tool.TryGetDummy(beamDef.End, out end, out endParent))
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

        internal void UpdateState(Trigger state, bool add)
        {
            var keepFiring = !add && (Activated || GunBase.Shooting) && (state & Trigger.Firing) > 0;
            
            var valid = false;
            foreach (var flag in Definition.Triggers)
            {
                if (flag >= state)
                    valid = true;

                if (!valid || keepFiring || (add && (flag & state) == 0))
                    continue;

                if (add) AvState |= state;
                else AvState ^= state;

                foreach (var monitor in EventMonitors)
                    monitor.Invoke((int)state, add);

                UpdateEffects(flag, add);

                //Logs.WriteLine($"UpdateState() - {flag} - {add}");

                if (!add) // maybe remove this later :|
                {
                    if (flag == Trigger.Hit) WasHitting = false;
                    if (flag == Trigger.RayHit) HitInfo.IsValid = false;
                }
            }
        }

        internal void UpdateEffects(Trigger state, bool add)
        {
            if (Session.IsDedicated) return; //TEMPORARY!!! or not?

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

            if (!MyAPIGateway.Session.IsServer)
                Session.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = true, PacketType = (byte)PacketType.Replicate });
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
            ToolEntity.Storage[Session.CompDataGuid] = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Repo));

            return false;
        }

        internal void Init()
        {
            ToolEntity.Components.Add(this);

            if (IsBlock)
            {
                SinkInit();
            }

            StorageInit();
            SubpartsInit();

            if (!IsBlock || BlockTool.IsFunctional)
                UpdateState(Trigger.Functional, true);

            if (!Session.IsDedicated)
                GetShowInToolbarSwitch();

            if (!MyAPIGateway.Session.IsServer)
                Session.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = true, PacketType = (byte)PacketType.Replicate });
        }

        private void SinkInit()
        {
            var sinkInfo = new MyResourceSinkInfo()
            {
                MaxRequiredInput = Definition.ActivePower,
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
                return Definition.ActivePower;

            return Definition.IdlePower;
        }

        internal void SubpartsInit()
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>() { ToolEntity };
            ToolEntity.Hierarchy.GetChildrenRecursive(entities);

            var noEmitter = string.IsNullOrEmpty(Definition.EmitterName);
            Dictionary<string, IMyModelDummy> dummies = new Dictionary<string, IMyModelDummy>();
            foreach (var entity in entities)
            {
                if (entity != ToolEntity)
                {
                    entity.OnClose += (ent) => Dirty = true;
                }
                entity.NeedsWorldMatrix = true;

                if (noEmitter)
                    continue;

                entity.Model.GetDummies(dummies);

                foreach (var dummy in dummies)
                {
                    if (dummy.Key == Definition.EmitterName)
                    {
                        Muzzle = dummy.Value;
                        MuzzlePart = (MyEntity)entity;
                    }
                }
                dummies.Clear();
            }

            HasEmitter = Muzzle != null;

            var functional = !IsBlock || BlockTool.IsFunctional;
            if (functional && !HasEmitter && Definition.Location == Location.Emitter)
                Definition.Location = Location.Centre;

            Dirty = false;
        }

        private void StorageInit()
        {
            string rawData;
            ToolRepo loadRepo = null;
            if (ToolEntity.Storage == null)
            {
                ToolEntity.Storage = new MyModStorageComponent();
            }
            else if (ToolEntity.Storage.TryGetValue(Session.CompDataGuid, out rawData))
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
        }

        internal void OnDrillComplete(WorkData data)
        {
            Session.DsUtil.Start("notify");
            var drillData = (DrillData)data;
            var storageDatas = drillData.StorageDatas;
            if (drillData?.Voxel?.Storage == null)
            {
                Logs.WriteLine($"Null reference in OnDrillComplete - DrillData null: {drillData == null} - Voxel null: {drillData?.Voxel == null}");
            }
            for (int i = storageDatas.Count - 1; i >= 0; i--)
            {
                var info = storageDatas[i];
                drillData?.Voxel?.Storage?.NotifyRangeChanged(ref info.Min, ref info.Max, MyStorageDataTypeFlags.ContentAndMaterial);
            }

            drillData.Clean();
            Session.DrillDataPool.Push(drillData);

            Session.DsUtil.Complete("notify", true);

            ActiveThreads--;
            if (ActiveThreads > 0) return;

            var isHitting = Functional && Powered && Enabled && Working && (Activated || GunBase.Shooting);
            if (isHitting != WasHitting)
            {
                UpdateState(Trigger.Hit, isHitting);
                WasHitting = isHitting;

                if (Definition.Debug && !isHitting)
                {
                    Logs.WriteLine("read: " + Session.DsUtil.GetValue("read").ToString());
                    Logs.WriteLine("sort: " + Session.DsUtil.GetValue("sort").ToString());
                    Logs.WriteLine("calc: " + Session.DsUtil.GetValue("calc").ToString());
                    Logs.WriteLine("write: " + Session.DsUtil.GetValue("write").ToString());
                    Logs.WriteLine("notify: " + Session.DsUtil.GetValue("notify").ToString());
                    Session.DsUtil.Clean();
                }
            }
            Working = false;
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
                    LastPushSucceeded = Grid.ConveyorSystem.PushGenerateItem(itemDef.Id, amount, out transferred, BlockTool, false);
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

        internal void UpdateConnections()
        {

        }

        internal void Close()
        {
            if (!MyAPIGateway.Session.IsServer)
                Session.Networking.SendPacketToServer(new ReplicationPacket { EntityId = ToolEntity.EntityId, Add = false, PacketType = (byte)PacketType.Replicate });

            Clean();

            if (IsBlock)
            {
                BlockTool.EnabledChanged -= EnabledChanged;
                BlockTool.IsWorkingChanged -= IsWorkingChanged;

                return;
            }

            Session.HandTools.Remove(this);
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

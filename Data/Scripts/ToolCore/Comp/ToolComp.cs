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
using static ToolCore.Definitions.ToolDefinition;
using ToolCore.Session;
using ToolCore.Utils;

namespace ToolCore.Comp
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
        internal ToolAction Action;
        internal Trigger State;
        internal Drills DrillData;

        internal readonly Dictionary<Trigger, Effects> EventEffects = new Dictionary<Trigger, Effects>();
        internal readonly List<Effects> ActiveEffects = new List<Effects>();
        internal readonly Dictionary<int, List<PositionData>> WorkLayers = new Dictionary<int, List<PositionData>>();
        internal readonly List<byte> MaxContent = new List<byte>();
        internal readonly List<StorageInfo> StorageDatas = new List<StorageInfo>();
        internal readonly ConcurrentDictionary<MyObjectBuilder_Ore, float> Yields = new ConcurrentDictionary<MyObjectBuilder_Ore, float>();
        internal List<ulong> ReplicatedClients = new List<ulong>();

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

        internal bool NoEmitter;
        internal bool Draw = true;
        internal bool Debug = true;

        internal long CompTick20;
        internal long CompTick60;
        internal long CompTick120;
        internal int LastPushTick;
        internal int ActiveDrillThreads;

        internal bool Activated
        {
            get { return _activated; }
            set
            {
                _activated = value;
                if (Functional && Powered && Enabled)
                    UpdateState(Trigger.Activated, value);
            }
        }

        private bool _activated;

        internal enum ToolMode
        {
            Drill = 0,
            Grind = 1,
            Weld = 2,
        }

        internal enum ToolAction
        {
            Primary = 0,
            Secondary = 1,
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

            UpdateState(Trigger.RayHit, false);
            HitInfo.IsValid = false;
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
                case Trigger.Enabled:
                    UpdateEffects(Trigger.Enabled, add);
                    if (add && !Activated) break;
                    UpdateState(Trigger.Activated, add);
                    break;
                case Trigger.LeftClick:
                case Trigger.RightClick:
                    UpdateEffects(state, add);
                    UpdateState(Trigger.Click, add, true);
                    break;
                case Trigger.Activated:
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
                case Trigger.RayHit:
                    UpdateEffects(Trigger.RayHit, add);
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

            if (!MyAPIGateway.Session.IsServer)
                Session.Networking.SendPacketToServer(new ReplicationPacket { EntityId = Tool.EntityId, Add = true, Type = (byte)PacketType.Replicate });
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

            if (!MyAPIGateway.Session.IsServer)
                Session.Networking.SendPacketToServer(new ReplicationPacket { EntityId = Tool.EntityId, Add = true, Type = (byte)PacketType.Replicate });
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

            if (!MyAPIGateway.Session.IsServer)
                Session.Networking.SendPacketToServer(new ReplicationPacket { EntityId = Tool.EntityId, Add = false, Type = (byte)PacketType.Replicate });

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

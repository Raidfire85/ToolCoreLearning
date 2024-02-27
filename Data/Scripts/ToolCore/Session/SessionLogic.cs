using ParallelTasks;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using System;
using System.Net;
using System.Security.Cryptography;
using ToolCore.Comp;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender.Voxels;
using static ToolCore.Utils.Draw;
using static ToolCore.Utils.Utils;
using static VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GameDefinition;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        internal void CompLoop()
        {

            for (int i = 0; i < GridList.Count; i++)
            {
                var gridComp = GridList[i];

                for (int j = 0; j < gridComp.ToolComps.Count; j++)
                {
                    UpdateComp(gridComp.ToolComps[j]);
                } //Tools loop

            } //Grids loop

            for (int i = 0; i < HandTools.Count; i++)
            {
                UpdateComp(HandTools[i]);
            }//Handtools loop

        }

        private void UpdateComp(ToolComp comp)
        {
            var def = comp.Definition;

            UpdateTool(comp);

            var avState = comp.AvState & def.EventFlags;
            if (!comp.AvActive && avState > 0)
            {
                AvComps.Add(comp);
                comp.AvActive = true;
            }

            if (comp.Mode != ToolComp.ToolMode.Drill && comp.WorkTick == Tick % def.UpdateInterval)
                UpdateHitState(comp);

            if (!IsDedicated && comp.Draw)
                DrawComp(comp);

            if (!IsDedicated && comp.Definition.Debug)
            {
                DrawDebug(comp);
            }
        }

        private void DrawComp(ToolComp comp)
        {
            Vector3D worldPos, worldForward, worldUp;
            CalculateWorldVectors(comp, out worldPos, out worldForward, out worldUp);

            var toolValues = comp.Values;

            MatrixD drawMatrix;
            switch (comp.Definition.EffectShape)
            {
                case EffectShape.Sphere:
                    drawMatrix = MatrixD.CreateWorld(worldPos, worldForward, worldUp);
                    DrawSphere(drawMatrix, toolValues.Radius, Color.LawnGreen, false, 20, 0.01f);
                    break;
                case EffectShape.Cylinder:
                    drawMatrix = MatrixD.CreateWorld(worldPos, worldUp, worldForward);
                    DrawCylinder(drawMatrix, toolValues.Radius, toolValues.Length, Color.LawnGreen);
                    break;
                case EffectShape.Cuboid:
                    var quat = Quaternion.CreateFromForwardUp(worldForward, worldUp);
                    var obb = new MyOrientedBoundingBoxD(worldPos, toolValues.HalfExtent, quat);
                    DrawBox(obb, Color.LawnGreen, false, 5, 0.005f);
                    break;
                case EffectShape.Line:
                    DrawLine(worldPos, worldForward, Color.LawnGreen, 0.025f, toolValues.Length);
                    break;
                case EffectShape.Ray:
                    DrawLine(worldPos, worldForward, Color.LawnGreen, 0.025f, toolValues.Length);
                    break;
                default:
                    return;
            }
        }

        private void DrawDebug(ToolComp comp)
        {
            comp.DrawBoxes.ApplyAdditions();
            foreach (var tuple in comp.DrawBoxes)
            {
                //DrawLine(tuple.Item1.Center, Vector3D.One, tuple.Item2, 0.02f, 0.1f);
                DrawBox(tuple.Item1, tuple.Item2, false, 2, 0.01f);
            }
        }

        private void UpdateHitState(ToolComp comp)
        {
            var operational = comp.Functional && comp.Powered && comp.Enabled;
            var firing = comp.Activated || comp.GunBase.Shooting;

            var hitting = comp.Working && operational && firing;
            if (hitting != comp.WasHitting)
            {
                comp.WasHitting = hitting;
                comp.UpdateAvState(Trigger.Hit, hitting);
            }

            comp.Working = false;
        }

        private void CalculateWorldVectors(ToolComp comp, out Vector3D worldPos, out Vector3D worldForward, out Vector3D worldUp)
        {
            var def = comp.Definition;
            var pos = comp.ToolEntity.PositionComp;

            switch (def.Location)
            {
                case Location.Emitter:
                    var partMatrix = comp.MuzzlePart.PositionComp.WorldMatrixRef;
                    var muzzleMatrix = (MatrixD)comp.Muzzle.Matrix;

                    var localPos = muzzleMatrix.Translation;
                    var muzzleForward = Vector3D.Normalize(muzzleMatrix.Forward);
                    var muzzleUp = Vector3D.Normalize(muzzleMatrix.Up);

                    if (!Vector3D.IsZero(def.Offset))
                    {
                        localPos += def.Offset;
                    }

                    Vector3D.Transform(ref localPos, ref partMatrix, out worldPos);
                    Vector3D.TransformNormal(ref muzzleForward, ref partMatrix, out worldForward);
                    Vector3D.TransformNormal(ref muzzleUp, ref partMatrix, out worldUp);
                    break;
                case Location.Parent:
                    var parentMatrix = comp.IsBlock ? comp.Parent.PositionComp.WorldMatrixRef : ((IMyCharacter)comp.Parent).GetHeadMatrix(true);
                    worldPos = comp.IsBlock ? comp.Parent.PositionComp.WorldAABB.Center : ((IMyCharacter)comp.Parent).GetHeadMatrix(true).Translation;
                    Vector3D offset;
                    Vector3D.Rotate(ref def.Offset, ref parentMatrix, out offset);
                    worldPos += offset;

                    worldForward = parentMatrix.Forward;
                    worldUp = parentMatrix.Up;
                    break;
                case Location.Centre:
                    var toolMatrix = pos.WorldMatrixRef;
                    worldPos = pos.WorldAABB.Center;
                    Vector3D.Rotate(ref def.Offset, ref toolMatrix, out offset);
                    worldPos += offset;

                    worldForward = toolMatrix.Forward;
                    worldUp = toolMatrix.Up;
                    break;
                default:
                    worldPos = Vector3D.Zero;
                    worldForward = Vector3D.Forward;
                    worldUp = Vector3D.Up;
                    break;
            }
        }

        private void UpdateTool(ToolComp comp)
        {
            var def = comp.Definition;
            var workTick = comp.WorkTick == Tick % def.UpdateInterval;

            var tool = comp.ToolEntity;
            var block = comp.BlockTool;
            var handTool = comp.HandTool;
            var isBlock = comp.IsBlock;

            if (isBlock && comp.Functional != block.IsFunctional)
            {
                comp.Functional = block.IsFunctional;
                comp.UpdateAvState(Trigger.Functional, block.IsFunctional);
                comp.Dirty = true;
            }

            if (!comp.Functional)
                return;

            if (isBlock && (comp.UpdatePower || comp.CompTick20 == TickMod20))
            {
                var wasPowered = comp.Powered;
                var isPowered = comp.IsPowered();
                //if (comp.UpdatePower) Logs.WriteLine($"UpdatePower: {wasPowered} : {isPowered}");
                if (wasPowered != isPowered)
                {
                    comp.UpdateAvState(Trigger.Powered, comp.Powered);

                    if (!isPowered)
                    {
                        comp.WasHitting = false;
                        comp.UpdateHitInfo(false);
                    }
                }
                comp.UpdatePower = false;
            }

            if (!comp.Powered || !isBlock && ((IMyCharacter)comp.Parent).SuitEnergyLevel <= 0)
                return;

            if (comp.Dirty)
            {
                comp.SubpartsInit();
                comp.ReloadModels();
            }

            //if (gridComp.ConveyorsDirty)
            //    comp.UpdateConnections();

            if (isBlock && !block.Enabled)
                return;

            var activated = comp.Activated;
            var handToolShooting = !isBlock && comp.HandTool.IsShooting;
            if (!activated && !comp.GunBase.Shooting && !handToolShooting)
                return;

            if (activated)
            {
                if (!MySessionComponentSafeZones.IsActionAllowed(comp.Parent, CastHax(MySessionComponentSafeZones.AllowedActions, (int)comp.Mode)))
                {
                    comp.Activated = false;
                    return;
                }
            }

            if (isBlock && IsServer && comp.CompTick60 == TickMod60 && comp.Mode != ToolComp.ToolMode.Weld)
                comp.ManageInventory();

            Vector3D worldPos, worldForward, worldUp;
            CalculateWorldVectors(comp, out worldPos, out worldForward, out worldUp);


            var ownerId = isBlock ? block.OwnerId : handTool.OwnerIdentityId;

            if (!isBlock && !IsDedicated && ownerId == MyAPIGateway.Session.LocalHumanPlayer?.IdentityId)
            {
                var leftMousePressed = MyAPIGateway.Input.IsNewLeftMousePressed();
                if (leftMousePressed || MyAPIGateway.Input.IsNewRightMousePressed())
                {
                    var action = leftMousePressed ? ToolComp.ToolAction.Primary : ToolComp.ToolAction.Secondary;
                    if (action != comp.Action)
                    {
                        comp.Action = action;
                        Networking.SendPacketToServer(new UpdatePacket(comp.ToolEntity.EntityId, FieldType.Action, (int)comp.Action));
                        return;
                    }
                }
            }

            var toolValues = comp.Values;

            // Initial raycast?
            IHitInfo hitInfo = null;
            if (!IsDedicated || workTick && def.EffectShape == EffectShape.Ray)
            {
                MyAPIGateway.Physics.CastRay(worldPos, worldPos + worldForward * toolValues.Length, out hitInfo);
                if (hitInfo?.HitEntity != null)
                {
                    MyStringHash material;
                    var entity = hitInfo.HitEntity;
                    var hitPos = hitInfo.Position;
                    if (entity is MyVoxelBase)
                    {
                        var voxelMatDef = ((MyVoxelBase)entity).GetMaterialAt(ref hitPos);
                        material = voxelMatDef?.MaterialTypeNameHash ?? MyStringHash.GetOrCompute("Rock");
                    }
                    else if (entity is IMyCharacter)
                        material = MyStringHash.GetOrCompute("Character");
                    else if (entity is MyEnvironmentSector)
                        material = MyStringHash.GetOrCompute("Tree");
                    else
                        material = MyStringHash.GetOrCompute("Metal");

                    if (def.Location == Location.Hit)
                        worldPos = hitPos;

                    comp.UpdateHitInfo(true, hitInfo.Position, material);
                }
                else comp.UpdateHitInfo(false);
            }

            if (!workTick)
                return;
            
            if (comp.ActiveThreads > 0)
                return;

            comp.DrawBoxes.ClearList();

            if (def.CacheBlocks && comp.Mode != ToolComp.ToolMode.Drill && comp.WorkSet.Count == def.Rate)
            {
                comp.OnGetBlocksComplete(null);
                return;
            }

            var line = false;
            var rayLength = toolValues.Length;
            switch (def.EffectShape)
            {
                case EffectShape.Sphere:
                case EffectShape.Cylinder:
                case EffectShape.Cuboid:
                    def.EffectSphere.Center = worldPos;
                    def.EffectSphere.Radius = toolValues.BoundingRadius;
                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref def.EffectSphere, _entities);
                    break;
                case EffectShape.Line:
                    var effectLine = new LineD(worldPos, worldPos + worldForward * toolValues.Length);
                    line = true;
                    MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref effectLine, _lineOverlaps);
                    break;
                case EffectShape.Ray:
                    if (hitInfo?.HitEntity != null)
                    {
                        _entities.Add((MyEntity)hitInfo.HitEntity);
                        rayLength *= hitInfo.Fraction;
                    }
                    break;
                default:
                    return;
            }

            var damageType = (int)def.ToolType < 2 ? MyDamageType.Drill : (int)def.ToolType < 4 ? MyDamageType.Grind : MyDamageType.Weld;

            var count = line ? _lineOverlaps.Count : _entities.Count;
            for (int k = 0; k < count; k++)
            {
                var entity = line ? _lineOverlaps[k].Element : _entities[k];

                if (entity is IMyDestroyableObject)
                {
                    if (entity is IMyCharacter && !def.DamageCharacters)
                        continue;

                    if (!isBlock && !def.AffectOwnGrid && entity == comp.Parent)
                        continue;

                    var obb = new MyOrientedBoundingBoxD(entity.PositionComp.LocalAABB, entity.PositionComp.WorldMatrixRef);
                    if (def.Debug) DrawBox(obb, Color.Red, false, 8);
                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            if (obb.Contains(ref def.EffectSphere) == ContainmentType.Disjoint)
                                continue;
                            break;
                        case EffectShape.Cylinder:
                            var offset = obb.Center - worldPos;
                            var halfEdge = entity.PositionComp.LocalAABB.HalfExtents.AbsMax();

                            var radial = Vector3D.ProjectOnPlane(ref offset, ref worldForward);
                            var radialDistSqr = (float)radial.LengthSquared();
                            var radiusPlus = toolValues.Radius + halfEdge;
                            if (radialDistSqr > (radiusPlus * radiusPlus))
                                continue;
                            var axial = Vector3D.ProjectOnVector(ref offset, ref worldForward);
                            var axialDistSqr = (float)axial.LengthSquared();
                            var halfLen = (toolValues.Length / 2) + halfEdge;
                            if (axialDistSqr > (halfLen * halfLen))
                                continue;
                            break;
                        case EffectShape.Cuboid:
                            var orientation = Quaternion.CreateFromForwardUp(worldForward, worldUp);
                            comp.Obb.Center = worldPos;
                            comp.Obb.Orientation = orientation;
                            comp.Obb.HalfExtent = toolValues.HalfExtent;
                            if (obb.Contains(ref comp.Obb) == ContainmentType.Disjoint)
                                continue;
                            break;
                        case EffectShape.Line:
                            var effectLine = new LineD(worldPos, worldPos + worldForward * toolValues.Length);
                            if (obb.Intersects(ref effectLine) == null)
                                continue;
                            break;
                        default:
                            break;

                    }
                    comp.Working = true;
                    var destroyableObject = (IMyDestroyableObject)entity;
                    destroyableObject.DoDamage(1f, damageType, true, null, ownerId);
                    continue;
                }

/*                if (entity is MyEnvironmentSector)
                {
                    IHitInfo hitInfo;
                    if (MyAPIGateway.Physics.CastRay(worldPos, worldPos + def.EffectSphere.Radius * worldForward, out hitInfo))
                    {
                        var hitEntity = hitInfo.HitEntity;
                        if (hitEntity is MyEnvironmentSector)
                        {
                            var sector = hitEntity as MyEnvironmentSector;
                            uint shapeKey = hitInfo.Value.HkHitInfo.GetShapeKey(0);
                            int itemFromShapeKey = sector.GetItemFromShapeKey(shapeKey);
                            if (sector.DataView.Items[itemFromShapeKey].ModelIndex >= 0)
                            {
                                MyBreakableEnvironmentProxy module = sector.GetModule<MyBreakableEnvironmentProxy>();
                                Vector3D hitnormal = base.CubeGrid.WorldMatrix.Right + base.CubeGrid.WorldMatrix.Forward;
                                hitnormal.Normalize();
                                float num = 10f;
                                float mass = base.CubeGrid.Physics.Mass;
                                float num2 = num * num * mass;
                                module.BreakAt(itemFromShapeKey, hitInfo.Value.HkHitInfo.Position, hitnormal, (double)num2);
                            }
                        }
                    }
                }*/

                if (entity is IMyVoxelBase)
                {
                    if (comp.Mode != ToolComp.ToolMode.Drill)
                        continue;

                    var voxel = (IMyVoxelBase)entity;

                    if ((voxel as MyVoxelBase).GetOrePriority() == 0)
                        continue;

                    var localCentre = Vector3D.Transform(worldPos + Vector3D.TransformNormal((voxel as MyVoxelBase).SizeInMetresHalf, voxel.WorldMatrix), voxel.WorldMatrixNormalizedInv);
                    var matrixNI = voxel.PositionComp.WorldMatrixNormalizedInv;
                    Vector3D localForward;
                    Vector3D.TransformNormal(ref worldForward, ref matrixNI, out localForward);

                    Vector3I minExtent;
                    Vector3I maxExtent;
                    if (def.EffectShape == EffectShape.Cuboid)
                    {
                        Vector3D localUp;
                        Vector3D.TransformNormal(ref worldUp, ref matrixNI, out localUp);
                        var orientation = Quaternion.CreateFromForwardUp(localForward, localUp);

                        comp.Obb.Center = localCentre;
                        comp.Obb.Orientation = orientation;
                        comp.Obb.HalfExtent = toolValues.HalfExtent;

                        var box = comp.Obb.GetAABB();
                        minExtent = Vector3I.Round(localCentre - box.HalfExtents);
                        maxExtent = Vector3I.Round(localCentre + box.HalfExtents);
                    }
                    else
                    {
                        var drillRadius = toolValues.BoundingRadius;
                        minExtent = Vector3I.Round(localCentre - drillRadius);
                        maxExtent = Vector3I.Round(localCentre + drillRadius);
                    }

                    var size = voxel.Storage.Size;
                    var min = Vector3I.Max(minExtent, Vector3I.Zero);
                    var max = Vector3I.Min(maxExtent, size);

                    if (def.Debug)
                    {
                        var offset = (voxel as MyVoxelBase).SizeInMetresHalf;
                        var drawBox = new BoundingBoxD((Vector3D)min - offset, (Vector3D)max - offset);
                        var drawObb = new MyOrientedBoundingBoxD(drawBox, voxel.PositionComp.LocalMatrixRef);
                        DrawBox(drawObb, Color.IndianRed, false, 4, 0.005f);
                    }

                    var data = DrillDataPool.Count > 0 ? DrillDataPool.Pop() : new DrillData();
                    data.Voxel = voxel;
                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.Start(comp.DrillSphere, comp.OnDrillComplete, data);
                            break;
                        case EffectShape.Cylinder:
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.Start(comp.DrillCylinder, comp.OnDrillComplete, data);
                            break;
                        case EffectShape.Cuboid:
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.Start(comp.DrillCuboid, comp.OnDrillComplete, data);
                            break;
                        case EffectShape.Line:
                            data.Origin = worldPos;
                            data.Direction = worldForward;
                            MyAPIGateway.Parallel.Start(comp.DrillLine, comp.OnDrillComplete, data);
                            break;
                        default:
                            break;
                    }
                    comp.ActiveThreads++;

                }

                if (entity is MyCubeGrid)
                {
                    var grid = entity as MyCubeGrid;

                    if (isBlock && !def.AffectOwnGrid && grid == comp.Grid)
                        continue;

                    var immune = (comp.Mode != ToolComp.ToolMode.Weld && (grid.Immune || !grid.DestructibleBlocks || grid.Projector != null)) || !grid.Editable;
                    var invalid = comp.Mode != ToolComp.ToolMode.Weld && (grid.Physics == null || !grid.Physics.Enabled);
                    if (immune || invalid || grid.MarkedForClose) continue;
                    
                    var toolData = ToolDataPool.Count > 0 ? ToolDataPool.Pop() : new ToolComp.ToolData();
                    toolData.Entity = entity;
                    toolData.Position = worldPos;
                    toolData.Forward = worldForward;
                    toolData.Up = worldUp;
                    toolData.RayLength = rayLength;

                    MyAPIGateway.Parallel.Start(comp.GetBlocksInVolume, comp.OnGetBlocksComplete, toolData);
                    comp.ActiveThreads++;
                }

            } //Hits loop

            _entities.Clear();
            _lineOverlaps.Clear();

        }

        internal void StartComps()
        {
            try
            {
                _startGrids.ApplyAdditions();
                for (int i = 0; i < _startGrids.Count; i++)
                {
                    var grid = _startGrids[i];

                    if (grid?.Physics == null || grid.IsPreview)
                        continue;

                    var gridComp = GridCompPool.Count > 0 ? GridCompPool.Pop() : new GridComp();
                    gridComp.Init(grid, this);

                    GridList.Add(gridComp);
                    GridMap[grid] = gridComp;
                    grid.OnClose += OnGridClose;
                }
                _startGrids.ClearImmediate();

                _startComps.ApplyAdditions();
                for (int i = 0; i < _startComps.Count; i++)
                {
                    var entity = _startComps[i];

                    if (entity is MyCubeBlock)
                    {
                        var block = (MyCubeBlock)entity;

                        if (block?.CubeGrid?.Physics == null || block.CubeGrid.IsPreview)
                            continue;

                        GridComp gridComp;
                        if (!GridMap.TryGetValue(block.CubeGrid, out gridComp))
                            continue;

                        var tool = block as IMyConveyorSorter;
                        if (tool != null)
                        {
                            ToolDefinition def;
                            if (!DefinitionMap.TryGetValue(tool.BlockDefinition, out def))
                                continue;

                            var comp = new ToolComp(entity, def, this);
                            ToolMap[block.EntityId] = comp;
                            comp.Init();
                            //((IMyCubeGrid)gridComp.Grid).WeaponSystem.Register(comp.GunBase);
                            gridComp.FatBlockAdded(block);

                            continue;
                        }

                        //var controller = block as MyShipController;
                        //if (controller != null)
                        //{
                        //    var control = new Control(controller);
                        //    gridComp.Controllers.TryAdd(controller, control);
                        //}

                        continue;
                    }

                    if (entity is IMyHandheldGunObject<MyDeviceBase>)
                    {
                        if (!entity.DefinitionId.HasValue)
                            continue;

                        ToolDefinition def;
                        if (!DefinitionMap.TryGetValue(entity.DefinitionId.Value, out def))
                            continue;

                        var comp = new ToolComp(entity, def, this);
                        ToolMap[entity.EntityId] = comp;
                        comp.Init();
                        HandTools.Add(comp);
                    }
                }
                _startComps.ClearImmediate();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

        }
    }
}

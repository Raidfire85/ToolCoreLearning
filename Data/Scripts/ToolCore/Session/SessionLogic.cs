using Entities.Blocks;
using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Voxels;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using ToolCore.Definitions.Serialised;
using ToolCore.Comp;
using ToolCore.Utils;
using static ToolCore.Utils.Draw;
using static ToolCore.Utils.Utils;
using Sandbox.Game.Weapons;

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
                    var comp = gridComp.ToolComps[j];
                    var def = comp.Definition;

                    UpdateTool(comp, gridComp);

                    var avState = comp.State & def.EventFlags;
                    if (!comp.AvActive && avState > 0)
                    {
                        AvComps.Add(comp);
                        comp.AvActive = true;
                    }

                    if (comp.Mode != ToolComp.ToolMode.Drill)
                        UpdateHitState(comp);

                    if (!IsDedicated && comp.Draw)
                        DrawComp(comp);


                } //Tools loop



            } //Grids loop

        }

        private void DrawComp(ToolComp comp)
        {
            var def = comp.Definition;
            var tool = comp.Tool;
            var pos = tool.PositionComp;
            var toolMatrix = pos.WorldMatrixRef;

            Vector3D worldPos = pos.WorldAABB.Center;
            Vector3D worldForward = toolMatrix.Forward;
            Vector3D worldUp = toolMatrix.Up;

            if (comp.HasEmitter)
            {
                var muzzleMatrix = (MatrixD)comp.Muzzle.Matrix;
                var partMatrix = comp.MuzzlePart.PositionComp.WorldMatrixRef;

                var localPos = muzzleMatrix.Translation;

                if (!Vector3D.IsZero(def.Offset))
                {
                    Vector3D offset;
                    Vector3D.Rotate(ref def.Offset, ref muzzleMatrix, out offset);
                    localPos += offset;
                }
                var muzzleForward = Vector3D.Normalize(muzzleMatrix.Forward);
                var muzzleUp = Vector3D.Normalize(muzzleMatrix.Up);

                Vector3D.Transform(ref localPos, ref partMatrix, out worldPos);
                Vector3D.TransformNormal(ref muzzleForward, ref partMatrix, out worldForward);
                Vector3D.TransformNormal(ref muzzleUp, ref partMatrix, out worldUp);
            }
            else if (!Vector3D.IsZero(def.Offset))
            {
                Vector3D offset;
                Vector3D.Rotate(ref def.Offset, ref toolMatrix, out offset);
                worldPos += offset;
            }

            var toolValues = comp.Values;

            MatrixD drawMatrix;
            switch (def.EffectShape)
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
                    DrawLine(worldPos, worldForward, Color.AliceBlue, 0.02f, toolValues.Length);
                    break;
                case EffectShape.Ray:
                    DrawLine(worldPos, worldForward, Color.AliceBlue, 0.02f, toolValues.Length);
                    break;
                default:
                    return;
            }
        }

        private void UpdateHitState(ToolComp comp, bool drillDebug = false)
        {
            if (comp.Mode == ToolComp.ToolMode.Drill && comp.ActiveDrillThreads > 0)
                return;

            var operational = comp.Functional && comp.Powered && comp.Enabled;
            var firing = comp.Activated || comp.GunBase.Shooting;

            var hitting = comp.Hitting && operational && firing;
            if (hitting != comp.WasHitting)
            {
                comp.WasHitting = hitting;
                comp.UpdateState(Trigger.Hit, hitting);

                if (drillDebug && !hitting)
                {
                    Logs.WriteLine("read: " + DsUtil.GetValue("read").ToString());
                    Logs.WriteLine("sort: " + DsUtil.GetValue("sort").ToString());
                    Logs.WriteLine("calc: " + DsUtil.GetValue("calc").ToString());
                    Logs.WriteLine("write: " + DsUtil.GetValue("write").ToString());
                    Logs.WriteLine("notify: " + DsUtil.GetValue("notify").ToString());
                    DsUtil.Clean();
                }
            }

            comp.Hitting = false;
        }

        private void UpdateTool(ToolComp comp, GridComp gridComp)
        {
            //if (!Tick10) continue;
            var compTick10 = comp.CompTick10 == TickMod10;

            var tool = comp.Tool;
            var parentGrid = gridComp.Grid;

            if (comp.Functional != tool.IsFunctional)
            {
                comp.Functional = tool.IsFunctional;
                comp.UpdateState(Trigger.Functional, tool.IsFunctional);
                Logs.WriteLine("AAA");
                comp.Dirty = true;
            }

            if (!comp.Functional)
                return;

            //MyAPIGateway.Utilities.ShowNotification(comp.Hitting.ToString() + " " + comp.WasHitting.ToString(), 16);

            if (comp.UpdatePower || comp.CompTick20 == TickMod20)
            {
                var wasPowered = comp.Powered;
                var isPowered = comp.IsPowered();
                if (comp.UpdatePower) Logs.WriteLine($"UpdatePower: {wasPowered} : {isPowered}");
                if (wasPowered != isPowered)
                {
                    comp.UpdateState(Trigger.Powered, comp.Powered);

                    if (!isPowered)
                    {
                        comp.WasHitting = false;
                        comp.UpdateHitInfo(false);
                    }
                }
                comp.UpdatePower = false;
            }

            if (!comp.Powered)
                return;

            if (comp.Dirty)
            {
                comp.SubpartsInit();
                comp.ReloadModels();
            }

            if (gridComp.ConveyorsDirty)
                comp.UpdateConnections();

            if (!tool.Enabled)
                return;

            var activated = comp.Activated;
            if (!activated && !comp.GunBase.Shooting)
                return;

            if (activated)
            {
                if (!MySessionComponentSafeZones.IsActionAllowed(comp.Grid, CastHax(MySessionComponentSafeZones.AllowedActions, (int)comp.Mode)))
                {
                    comp.Activated = false;
                    return;
                }
            }

            if (IsServer && comp.CompTick120 == TickMod120 && comp.Mode != ToolComp.ToolMode.Weld)
                comp.ManageInventory();

            if (comp.Definition.Debug)
            {
                DrawBoxes.ApplyAdditions();
                MyAPIGateway.Utilities.ShowNotification(DrawBoxes.Count.ToString(), 16);
                foreach (var tuple in DrawBoxes)
                    DrawLine(tuple.Item1.Center, Vector3D.One, tuple.Item2, 0.02f, 0.1f);
                    //DrawBox(tuple.Item1, tuple.Item2, false, 1, 0.01f);

                //if (comp.Hitting)
                //{
                //    DrawScaledPoint(comp.HitInfo.Position, 0.5, Color.Red);
                //    MyAPIGateway.Utilities.ShowNotification(comp.HitInfo.Position.ToString("F0"), 16);
                //}
            }

            var def = comp.Definition;
            var pos = tool.PositionComp;
            var toolMatrix = pos.WorldMatrixRef;

            Vector3D worldPos = pos.WorldAABB.Center;
            Vector3D worldForward = toolMatrix.Forward;
            Vector3D worldUp = toolMatrix.Up;

            if (comp.HasEmitter)
            {
                var muzzleMatrix = (MatrixD)comp.Muzzle.Matrix;
                var partMatrix = comp.MuzzlePart.PositionComp.WorldMatrixRef;

                var localPos = muzzleMatrix.Translation;

                if (!Vector3D.IsZero(def.Offset))
                {
                    Vector3D offset;
                    Vector3D.Rotate(ref def.Offset, ref muzzleMatrix, out offset);
                    localPos += offset;
                }
                var muzzleForward = Vector3D.Normalize(muzzleMatrix.Forward);
                var muzzleUp = Vector3D.Normalize(muzzleMatrix.Up);

                Vector3D.Transform(ref localPos, ref partMatrix, out worldPos);
                Vector3D.TransformNormal(ref muzzleForward, ref partMatrix, out worldForward);
                Vector3D.TransformNormal(ref muzzleUp, ref partMatrix, out worldUp);
            }
            else if (!Vector3D.IsZero(def.Offset))
            {
                Vector3D offset;
                Vector3D.Rotate(ref def.Offset, ref toolMatrix, out offset);
                worldPos += offset;
            }



            var toolValues = comp.Values;

            // Initial raycast?
            IHitInfo hitInfo = null;
            if (!IsDedicated || compTick10 && def.EffectShape == EffectShape.Ray)
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

            if (!compTick10)// || !IsServer)
                return;

            if (comp.Mode == ToolComp.ToolMode.Drill && comp.ActiveDrillThreads > 0)
                return;

            var line = false;
            var rayLength = toolValues.Length;
            switch (def.EffectShape)
            {
                case EffectShape.Sphere:
                case EffectShape.Cylinder:
                case EffectShape.Cuboid:
                    var sphere = def.EffectSphere;
                    sphere.Center = worldPos;
                    sphere.Radius = toolValues.BoundingRadius;
                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, _entities);
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

            _debugBlocks.Clear();
            _debugBlocks.UnionWith(_hitBlocks);
            _hitBlocks.ClearImmediate();

            var count = line ? _lineOverlaps.Count : _entities.Count;
            for (int k = 0; k < count; k++)
            {
                var entity = line ? _lineOverlaps[k].Element : _entities[k];

                if (entity is IMyCharacter)
                {
                    comp.Hitting = true;
                    var character = (IMyCharacter)entity;
                    character.DoDamage(1f, damageType, true, null, tool.OwnerId);
                    continue;
                }

                if (entity is IMyDestroyableObject)
                {
                    comp.Hitting = true;
                    var floating = (IMyDestroyableObject)entity;
                    floating.DoDamage(1f, damageType, true, null, tool.OwnerId);
                    continue;
                }

                //if (entity is MyEnvironmentSector)
                //{
                //    IHitInfo hitInfo;
                //    if (MyAPIGateway.Physics.CastRay(worldPos, worldPos + def.EffectSphere.Radius * worldForward, out hitInfo))
                //    {
                //        var hitEntity = hitInfo.HitEntity;
                //        if (hitEntity is MyEnvironmentSector)
                //        {
                //            var sector = hitEntity as MyEnvironmentSector;
                //            uint shapeKey = hitInfo.Value.HkHitInfo.GetShapeKey(0);
                //            int itemFromShapeKey = sector.GetItemFromShapeKey(shapeKey);
                //            if (sector.DataView.Items[itemFromShapeKey].ModelIndex >= 0)
                //            {
                //                MyBreakableEnvironmentProxy module = sector.GetModule<MyBreakableEnvironmentProxy>();
                //                Vector3D hitnormal = base.CubeGrid.WorldMatrix.Right + base.CubeGrid.WorldMatrix.Forward;
                //                hitnormal.Normalize();
                //                float num = 10f;
                //                float mass = base.CubeGrid.Physics.Mass;
                //                float num2 = num * num * mass;
                //                module.BreakAt(itemFromShapeKey, hitInfo.Value.HkHitInfo.Position, hitnormal, (double)num2);
                //            }
                //        }
                //    }
                //}

                if (entity is IMyVoxelBase)
                {
                    if (comp.Mode != ToolComp.ToolMode.Drill)
                        continue;

                    var voxel = (IMyVoxelBase)entity;

                    if ((voxel as MyVoxelBase).GetOrePriority() == 0)
                        continue;

                    var localCentre = Vector3D.Transform(worldPos + Vector3D.TransformNormal((voxel as MyVoxelBase).SizeInMetresHalf, voxel.WorldMatrix), voxel.WorldMatrixNormalizedInv);
                    //MyAPIGateway.Utilities.ShowNotification($"{localCentre.ToString("N1")}", 160);
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

                    ToolComp.Drills data;
                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            data = comp.DrillData;
                            data.Voxel = voxel;
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            MyAPIGateway.Parallel.StartBackground(comp.DrillSphere, comp.OnDrillComplete);
                            break;
                        case EffectShape.Cylinder:
                            data = comp.DrillData;
                            data.Voxel = voxel;
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.StartBackground(comp.DrillCylinder, comp.OnDrillComplete);
                            break;
                        case EffectShape.Cuboid:
                            data = comp.DrillData;
                            data.Voxel = voxel;
                            data.Min = min;
                            data.Max = max;
                            data.Origin = localCentre;
                            data.Direction = localForward;
                            MyAPIGateway.Parallel.StartBackground(comp.DrillCuboid, comp.OnDrillComplete);
                            break;
                        case EffectShape.Line:
                            data = comp.DrillData;
                            data.Voxel = voxel;
                            //data.Min = min;
                            //data.Max = max;
                            data.Origin = worldPos;
                            data.Direction = worldForward;
                            MyAPIGateway.Parallel.StartBackground(comp.DrillLine, comp.OnDrillComplete);
                            break;
                        default:
                            break;
                    }
                    comp.ActiveDrillThreads++;

                }

                if (entity is MyCubeGrid)
                {
                    var grid = entity as MyCubeGrid;

                    if (!def.AffectOwnGrid && grid == parentGrid)
                        continue;

                    var exit = (comp.Mode != ToolComp.ToolMode.Weld && (grid.Immune || !grid.DestructibleBlocks)) || !grid.Editable;
                    if (exit) continue;

                    var gridMatrixNI = grid.PositionComp.WorldMatrixNormalizedInv;
                    var localCentre = grid.WorldToGridScaledLocal(worldPos);
                    Vector3D localForward;
                    Vector3D.TransformNormal(ref worldForward, ref gridMatrixNI, out localForward);

                    var gridSizeR = grid.GridSizeR;
                    var radius = toolValues.BoundingRadius * gridSizeR;

                    Vector3D minExtent;
                    Vector3D maxExtent;

                    if (def.EffectShape == EffectShape.Cuboid)
                    {
                        Vector3D localUp;
                        Vector3D.TransformNormal(ref worldUp, ref gridMatrixNI, out localUp);
                        var orientation = Quaternion.CreateFromForwardUp(localForward, localUp);

                        comp.Obb.Center = localCentre;
                        comp.Obb.Orientation = orientation;
                        comp.Obb.HalfExtent = toolValues.HalfExtent * gridSizeR;

                        var box = comp.Obb.GetAABB();

                        minExtent = localCentre - box.HalfExtents;
                        maxExtent = localCentre + box.HalfExtents;

                        if (def.Debug)
                        {
                            var gridSize = grid.GridSize;
                            var drawBox = new BoundingBoxD(minExtent * gridSize, maxExtent * gridSize);
                            var drawObb = new MyOrientedBoundingBoxD(drawBox, grid.PositionComp.LocalMatrixRef);
                            DrawBox(drawObb, Color.CornflowerBlue, false, 4, 0.005f);
                        }
                    }
                    else
                    {
                        minExtent = localCentre - radius;
                        maxExtent = localCentre + radius;
                    }

                    var sMin = Vector3I.Round(minExtent);
                    var sMax = Vector3I.Round(maxExtent);

                    var gMin = grid.Min;
                    var gMax = grid.Max;

                    var min = Vector3I.Max(sMin, gMin);
                    var max = Vector3I.Min(sMax, gMax);

                    switch (def.EffectShape)
                    {
                        case EffectShape.Sphere:
                            GridUtils.GetBlocksInSphere(grid, min, max, localCentre, radius, _hitBlocks);
                            break;
                        case EffectShape.Cylinder:
                            GridUtils.GetBlocksInCylinder(grid, min, max, localCentre, localForward, toolValues.Radius * gridSizeR, toolValues.Length * gridSizeR, _hitBlocks, comp.Definition.Debug);
                            break;
                        case EffectShape.Cuboid:
                            GridUtils.GetBlocksInCuboid(grid, min, max, comp.Obb, _hitBlocks);
                            break;
                        case EffectShape.Line:
                            GridUtils.GetBlocksOverlappingLine(grid, worldPos, worldPos + worldForward * toolValues.Length, _hitBlocks);
                            break;
                        case EffectShape.Ray:
                            GridUtils.GetBlockInRayPath(grid, worldPos + worldForward * (rayLength + 0.01), _hitBlocks, comp.Definition.Debug);
                            break;
                        default:
                            break;
                    }

                    continue;
                }

            } //Hits loop

            _entities.Clear();
            _lineOverlaps.Clear();
            _missingComponents.Clear();
            _hitBlocks.ApplyAdditions();

            comp.Hitting |= _hitBlocks.Count > 0;
            //Logs.WriteLine($"{count} : {_hitBlocks.Count} {(int)comp.Mode}");

            var inventory = comp.Inventory;
            switch (comp.Mode)
            {
                case ToolComp.ToolMode.Drill:
                    break;
                case ToolComp.ToolMode.Grind:
                    #region Grind

                    var grindCount = _hitBlocks.Count;
                    grindCount = grindCount > 0 ? grindCount : 1;

                    var speedMultGrind = 0.25f / (float)Math.Min(4, grindCount);
                    var grindAmount = MyAPIGateway.Session.GrinderSpeedMultiplier * 4f * speedMultGrind;
                    for (int a = 0; a < _hitBlocks.Count; a++)
                    {
                        var slim = _hitBlocks[a];
                        Logs.WriteLine($"aaaa");

                        var hitGrid = slim.CubeGrid as MyCubeGrid;
                        if (!hitGrid.Editable || hitGrid.Immune)
                            continue;


                        MyDamageInformation damageInfo = new MyDamageInformation(false, grindAmount, MyDamageType.Grind, tool.EntityId);
                        if (slim.UseDamageSystem) Session.DamageSystem.RaiseBeforeDamageApplied(slim, ref damageInfo);

                        slim.DecreaseMountLevel(damageInfo.Amount, inventory, false);
                        slim.MoveItemsFromConstructionStockpile(inventory, MyItemFlags.None);

                        if (slim.UseDamageSystem) Session.DamageSystem.RaiseAfterDamageApplied(slim, damageInfo);

                        if (slim.IsFullyDismounted)
                        {
                            if (slim.FatBlock != null && slim.FatBlock.HasInventory)
                            {
                                GridUtils.EmptyBlockInventories((MyCubeBlock)slim.FatBlock, inventory);
                            }

                            slim.SpawnConstructionStockpile();
                            slim.CubeGrid.RazeBlock(slim.Min);
                        }

                    }

                    #endregion
                    break;
                case ToolComp.ToolMode.Weld:
                    #region Weld

                    var buildCount = _hitBlocks.Count;
                    for (int a = 0; a < _hitBlocks.Count; a++)
                    {
                        var slim = _hitBlocks[a];

                        if (slim.IsFullIntegrity)
                        {
                            buildCount--;
                            if (!slim.HasDeformation)
                            {
                                _hitBlocks.Remove(slim);
                                continue;
                            }
                        }
                        else
                        {
                            slim.GetMissingComponents(_missingComponents);
                        }

                    }
                    _hitBlocks.ApplyRemovals();

                    foreach (var component in _missingComponents)
                    {
                        var required = component.Value;
                        if (required == 0)
                        {
                            Logs.WriteLine("Required component is zero");
                            continue;
                        }

                        MyDefinitionId defId = new MyDefinitionId(typeof(MyObjectBuilder_Component), component.Key);

                        var current = inventory.GetItemAmount(defId);
                        var difference = required - current;
                        if (difference > 0 && inventory.CargoPercentage < 0.999f)
                        {
                            current += parentGrid.ConveyorSystem.PullItem(defId, difference, tool, inventory, false);
                        }

                    }

                    buildCount = buildCount > 0 ? buildCount : 1;
                    var speedMultWeld = 0.25f / (float)Math.Min(4, buildCount);
                    var weldAmount = MyAPIGateway.Session.WelderSpeedMultiplier * toolValues.Speed * speedMultWeld;
                    for (int a = 0; a < _hitBlocks.Count; a++)
                    {
                        var slim = _hitBlocks[a];

                        var welder = tool as IMyShipWelder;
                        if (welder != null && gridComp.Projector != null)
                        {
                            var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;

                            if (!slim.CanContinueBuild(inventory))
                                continue;

                            if (slim.WillBecomeFunctional(weldAmount) && !welder.IsWithinWorldLimits(gridComp.Projector, "", cubeDef.PCU - 1))
                                continue;
                        }

                        slim.MoveItemsToConstructionStockpile(inventory);

                        slim.IncreaseMountLevel(weldAmount, tool.OwnerId, inventory, 0.15f, welder?.HelpOthers ?? false);

                    }
                    #endregion

                    break;
                default:
                    break;
            }



            //for (int a = 0; a < _hitBlocks.Count; a++)
            //{
            //    var slim = _hitBlocks[a];

            //    if (!_debugBlocks.Remove(slim)) //wasn't there last tick
            //        slim.Dithering = -0.1f;
            //}

            //foreach (var slim in _debugBlocks)
            //{
            //    slim.Dithering = 0;
            //}
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

                    var gridComp = _gridCompPool.Count > 0 ? _gridCompPool.Pop() : new GridComp();
                    gridComp.Init(grid, this);

                    GridList.Add(gridComp);
                    GridMap[grid] = gridComp;
                    grid.OnClose += OnGridClose;
                }
                _startGrids.ClearImmediate();

                _startBlocks.ApplyAdditions();
                for (int i = 0; i < _startBlocks.Count; i++)
                {
                    var entity = _startBlocks[i];

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
                            var def = DefinitionMap[tool.BlockDefinition];
                            var comp = new ToolComp(tool, def, this);
                            ToolMap[block.EntityId] = comp;
                            comp.Init();
                            ((IMyCubeGrid)gridComp.Grid).WeaponSystem.Register(comp.GunBase);
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

                    if (entity is IMyGunObject<MyToolBase>)
                    {

                    }
                }
                _startBlocks.ClearImmediate();
            }
            catch (Exception ex)
            {
                Logs.WriteLine($"Exception in StartComps: {ex}");
            }

        }
    }
}

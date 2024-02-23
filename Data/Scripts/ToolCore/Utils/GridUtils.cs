using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using ToolCore.Comp;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static ToolCore.Comp.ToolComp;

namespace ToolCore
{
    internal static class GridUtils
    {

        internal static void GetBlocksInVolume(this ToolComp comp, WorkData workData)
        {
            try
            {
                var def = comp.Definition;
                var toolValues = comp.Values;
                var data = workData as ToolData;
                var grid = data.Entity as MyCubeGrid;

                var gridMatrixNI = grid.PositionComp.WorldMatrixNormalizedInv;
                var localCentre = grid.WorldToGridScaledLocal(data.Position);
                Vector3D localForward;
                Vector3D.TransformNormal(ref data.Forward, ref gridMatrixNI, out localForward);

                var gridSizeR = grid.GridSizeR;
                var radius = toolValues.BoundingRadius * gridSizeR;

                Vector3D minExtent;
                Vector3D maxExtent;

                if (def.EffectShape == EffectShape.Cuboid)
                {
                    Vector3D localUp;
                    Vector3D.TransformNormal(ref data.Up, ref gridMatrixNI, out localUp);
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
                        comp.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(drawObb, Color.LightBlue));
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
                        comp.GetBlocksInSphere(data, grid, min, max, localCentre, radius);
                        break;
                    case EffectShape.Cylinder:
                        comp.GetBlocksInCylinder(data, grid, min, max, localCentre, localForward, toolValues.Radius * gridSizeR, toolValues.Length * gridSizeR);
                        break;
                    case EffectShape.Cuboid:
                        comp.GetBlocksInCuboid(data, grid, min, max, comp.Obb);
                        break;
                    case EffectShape.Line:
                        comp.GetBlocksOverlappingLine(data, grid, data.Position, data.Position + data.Forward * toolValues.Length);
                        break;
                    case EffectShape.Ray:
                        comp.GetBlockInRayPath(data, grid, data.Position + data.Forward * (data.RayLength + 0.01));
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
                comp.ActiveThreads--;
            }
        }

        private static void GetBlocksInSphere(this ToolComp comp, ToolData data, MyCubeGrid grid, 
            Vector3I min, Vector3I max, Vector3D centre, double radius)
        {
            int i, j, k;
            for (i = min.X; i <= max.X; i++)
            {
                for (j = min.Y; j <= max.Y; j++)
                {
                    for (k = min.Z; k <= max.Z; k++)
                    {
                        var pos = new Vector3I(i, j, k);

                        var posD = (Vector3D)pos;
                        Vector3D corner = Vector3D.Clamp(centre, posD - 0.5, posD + 0.5);

                        var distSqr = Vector3D.DistanceSquared(corner, centre);
                        if (distSqr > radius * radius)
                            continue;

                        MyCube cube;
                        if (!grid.TryGetCube(pos, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        if (!data.HitBlocksHash.Add(slim))
                            continue;

                        if (slim.FatBlock != null && (slim.FatBlock.MarkedForClose || slim.FatBlock.Closed))
                            continue;

                        if (comp.Mode == ToolMode.Weld && grid.Projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                            continue;

                        comp.HitBlocks.TryAdd(slim, (float)distSqr);
                    }
                }
            }
        }

        private static void GetBlocksInCylinder(this ToolComp comp, ToolData data, MyCubeGrid grid, 
            Vector3I min, Vector3I max, Vector3D centre, Vector3D forward, double radius, double length)
        {
            var endOffset = forward * (length / 2);
            var endOffsetAbs = Vector3D.Abs(endOffset);

            //if (debug)
            //{
            //    var centrepoint = grid.GridIntegerToWorld(centre);
            //    DrawScaledPoint(centrepoint, 0.3f, Color.Green);
            //    var end1 = grid.GridIntegerToWorld(centre + endOffset);
            //    var end2 = grid.GridIntegerToWorld(centre - endOffset);
            //    DrawScaledPoint(end1, 0.3f, Color.Green);
            //    DrawScaledPoint(end2, 0.3f, Color.Green);
            //    DrawLine(end1, end2, Color.Green, 0.05f);
            //}

            int i, j, k;
            for (i = min.X; i <= max.X; i++)
            {
                for (j = min.Y; j <= max.Y; j++)
                {
                    for (k = min.Z; k <= max.Z; k++)
                    {
                        var pos = new Vector3I(i, j, k);

                        var posD = (Vector3D)pos;
                        var blockOffset = posD - centre;
                        var blockDir = Vector3D.ProjectOnPlane(ref blockOffset, ref forward);

                        var clampedIntersect = Vector3D.Clamp(posD - blockDir, centre - endOffsetAbs, centre + endOffsetAbs);
                        var corner = Vector3D.Clamp(clampedIntersect, posD - 0.5, posD + 0.5);

                        //if (debug)
                        //{
                        //    var a = grid.GridIntegerToWorld(clampedIntersect);
                        //    DrawScaledPoint(a, 0.1f, Color.Blue);

                        //    var b = grid.GridIntegerToWorld(corner);
                        //    DrawScaledPoint(b, 0.2f, Color.Red);

                        //    DrawLine(a, b, Color.Yellow, 0.02f);
                        //}

                        var distSqr = Vector3D.DistanceSquared(corner, clampedIntersect);
                        if (distSqr > radius * radius)
                            continue;

                        var cornerOffset = corner - centre;
                        var lengthVector = Vector3D.ProjectOnVector(ref cornerOffset, ref forward);
                        var lengthSqr = lengthVector.LengthSquared();
                        if (lengthSqr > length * length)
                            continue;

                        MyCube cube;
                        if (!grid.TryGetCube(pos, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        if (!data.HitBlocksHash.Add(slim))
                            continue;

                        if (slim.FatBlock != null && (slim.FatBlock.MarkedForClose || slim.FatBlock.Closed))
                            continue;

                        if (comp.Mode == ToolMode.Weld && grid.Projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                            continue;

                        comp.HitBlocks.TryAdd(slim, (float)distSqr);

                    }
                }
            }
        }

        private static void GetBlocksInCuboid(this ToolComp comp, ToolData data, MyCubeGrid grid, 
            Vector3I min, Vector3I max, MyOrientedBoundingBoxD obb)
        {
            int i, j, k;
            for (i = min.X; i <= max.X; i++)
            {
                for (j = min.Y; j <= max.Y; j++)
                {
                    for (k = min.Z; k <= max.Z; k++)
                    {
                        var pos = new Vector3I(i, j, k);

                        var posD = (Vector3D)pos;

                        var box = new BoundingBoxD(posD - 0.5, posD + 0.5);
                        if (obb.Contains(ref box) == ContainmentType.Disjoint)
                            continue;

                        MyCube cube;
                        if (!grid.TryGetCube(pos, out cube))
                            continue;

                        var slim = (IMySlimBlock)cube.CubeBlock;
                        if (!data.HitBlocksHash.Add(slim))
                            continue;

                        if (slim.FatBlock != null && (slim.FatBlock.MarkedForClose || slim.FatBlock.Closed))
                            continue;

                        if (comp.Mode == ToolMode.Weld && grid.Projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                            continue;

                        var distSqr = Vector3D.DistanceSquared(posD, obb.Center);
                        comp.HitBlocks.TryAdd(slim, (float)distSqr);

                    }
                }
            }
        }

        private static void GetBlocksOverlappingLine(this ToolComp comp, ToolData data, MyCubeGrid grid, 
            Vector3D start, Vector3D end)
        {
            var hitPositions = new List<Vector3I>();
            grid.RayCastCells(start, end, hitPositions);

            for (int i = 0; i < hitPositions.Count; i++)
            {
                var pos = hitPositions[i];

                MyCube cube;
                if (!grid.TryGetCube(pos, out cube))
                    continue;

                var slim = (IMySlimBlock)cube.CubeBlock;
                if (!data.HitBlocksHash.Add(slim))
                    continue;

                if (slim.FatBlock != null && (slim.FatBlock.MarkedForClose || slim.FatBlock.Closed))
                    continue;

                if (comp.Mode == ToolMode.Weld && grid.Projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                    continue;

                var distSqr = Vector3D.DistanceSquared(start, pos);
                comp.HitBlocks.TryAdd(slim, (float)distSqr);
            }
        }

        private static void GetBlockInRayPath(this ToolComp comp, ToolData data, MyCubeGrid grid, 
            Vector3D pos)
        {
            var localPos = Vector3D.Transform(pos, grid.PositionComp.WorldMatrixNormalizedInv);
            var cellPos = Vector3I.Round(localPos * grid.GridSizeR);

            MyCube cube;
            if (!grid.TryGetCube(cellPos, out cube))
                return;

            var slim = (IMySlimBlock)cube.CubeBlock;

            if (slim.FatBlock != null && (slim.FatBlock.MarkedForClose || slim.FatBlock.Closed))
                return;

            if (comp.Mode == ToolMode.Weld && grid.Projector == null && slim.IsFullIntegrity && !slim.HasDeformation)
                return;

            comp.HitBlocks.TryAdd(slim, data.RayLength);
        }

        internal static void OnGetBlocksComplete(this ToolComp comp, WorkData workData)
        {
            try
            {
                if (workData != null)
                {
                    var toolData = (ToolData)workData;
                    toolData.Clean();
                    comp.Session.ToolDataPool.Push(toolData);
                    comp.ActiveThreads--;
                }
                
                if (comp.ActiveThreads != 0)
                    return;

                var workSet = comp.WorkSet;
                var blocks = new Dictionary<IMySlimBlock, float>(comp.HitBlocks);
                var sortedBlocks = comp.HitBlocksSorted;
                if (blocks.Count == 0 && workSet.Count == 0)
                    return;

                var start = 0;
                if (comp.Definition.CacheBlocks && workSet.Count > 0)
                {
                    foreach (var slim in workSet)
                    {
                        var fat = slim.FatBlock;
                        if (slim.IsFullyDismounted || slim.CubeGrid.Closed || slim.CubeGrid.MarkedForClose || fat != null && (fat.Closed || fat.MarkedForClose))
                            continue;

                        sortedBlocks.Add(slim);
                    }
                    start = sortedBlocks.Count;
                    workSet.Clear();
                }

                foreach (var entry in blocks)
                {
                    var key = entry.Key;
                    var fat = key.FatBlock;
                    if (key.IsFullyDismounted || key.CubeGrid.Closed || key.CubeGrid.MarkedForClose || fat != null && (fat.Closed || fat.MarkedForClose))
                        continue;

                    int k;
                    for (k = start; k < sortedBlocks.Count; k++)
                    {
                        var block = sortedBlocks[k];
                        if (blocks[block] > entry.Value)
                            break;
                    }
                    sortedBlocks.Insert(k, key);

                    if (comp.Definition.Debug)
                    {
                        var slim = key;
                        var grid = (MyCubeGrid)slim.CubeGrid;
                        var worldPos = grid.GridIntegerToWorld(slim.Position);
                        var matrix = grid.PositionComp.WorldMatrixRef;
                        matrix.Translation = worldPos;

                        var sizeHalf = grid.GridSizeHalf - 0.05;
                        var halfExtent = new Vector3D(sizeHalf, sizeHalf, sizeHalf);
                        var bb = new BoundingBoxD(-halfExtent, halfExtent);
                        var obb = new MyOrientedBoundingBoxD(bb, matrix);
                        comp.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(obb, Color.Red));
                    }
                }
                blocks.Clear();


                switch (comp.Mode)
                {
                    case ToolMode.Drill:
                        break;
                    case ToolMode.Grind:
                        comp.GrindBlocks();
                        break;
                    case ToolMode.Weld:
                        if (comp.Definition.Rate > 4)
                        {
                            comp.WeldBlocksBulk();
                            break;
                        }
                        comp.WeldBlocks();
                        break;
                    default:
                        break;
                }

                sortedBlocks.Clear();
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
                comp.HitBlocks.Clear();
                comp.HitBlocksSorted.Clear();
            }

        }

        internal static void GrindBlocks(this ToolComp comp)
        {
            var inventory = comp.Inventory;
            var toolValues = comp.Values;
            var sortedBlocks = comp.HitBlocksSorted;
            var hitCount = sortedBlocks.Count;
            var tool = comp.ToolEntity;
            var maxBlocks = comp.Definition.Rate;
            var grindCount = Math.Min(hitCount, maxBlocks);

            var grindScaler = 0.25f / (float)Math.Min(4, grindCount > 0 ? grindCount : 1);
            var grindAmount = grindScaler * toolValues.Speed * MyAPIGateway.Session.GrinderSpeedMultiplier * 4f;
            for (int i = 0; i < grindCount; i++)
            {
                var slim = sortedBlocks[i];

                comp.Working = true;

                MyCubeBlockDefinition.PreloadConstructionModels((MyCubeBlockDefinition)slim.BlockDefinition);

                MyDamageInformation damageInfo = new MyDamageInformation(false, grindAmount, MyDamageType.Grind, tool.EntityId);
                if (slim.UseDamageSystem) comp.Session.Session.DamageSystem.RaiseBeforeDamageApplied(slim, ref damageInfo);

                slim.DecreaseMountLevel(damageInfo.Amount, inventory, false);
                slim.MoveItemsFromConstructionStockpile(inventory, MyItemFlags.None);

                if (slim.UseDamageSystem) comp.Session.Session.DamageSystem.RaiseAfterDamageApplied(slim, damageInfo);

                if (slim.IsFullyDismounted)
                {
                    if (slim.FatBlock != null && slim.FatBlock.HasInventory)
                    {
                        Utils.Utils.EmptyBlockInventories((MyCubeBlock)slim.FatBlock, inventory);
                    }

                    slim.SpawnConstructionStockpile();
                    slim.CubeGrid.RazeBlock(slim.Min);
                }
                else if (comp.Definition.CacheBlocks)
                {
                    comp.WorkSet.Add(slim);
                }
            }
        }

        internal static void WeldBlocks(this ToolComp comp)
        {
            var inventory = comp.Inventory;
            var toolValues = comp.Values;
            var sortedBlocks = comp.HitBlocksSorted;
            var hitCount = sortedBlocks.Count;
            var tool = comp.ToolEntity;
            var validBlocks = 0;
            var maxBlocks = comp.Definition.Rate;
            var ownerId = comp.IsBlock ? comp.BlockTool.OwnerId : comp.HandTool.OwnerIdentityId;

            var missingComponents = comp.Session.MissingComponents;
            var buildCount = Math.Min(hitCount, maxBlocks);
            var weldScaler = 0.25f / (float)Math.Min(4, buildCount);
            var weldAmount = weldScaler * toolValues.Speed * MyAPIGateway.Session.WelderSpeedMultiplier;

            for (int i = 0; i < hitCount; i++)
            {
                if (validBlocks >= maxBlocks)
                    return;

                var slim = sortedBlocks[i];
                var grid = (MyCubeGrid)slim.CubeGrid;
                var projector = grid.Projector as IMyProjector;
                var blockDef = slim.BlockDefinition as MyCubeBlockDefinition;
                missingComponents.Clear();

                if (projector != null)
                {
                    var components = blockDef.Components;
                    if (components != null && components.Length != 0)
                    {
                        var firstComp = components[0].Definition.Id.SubtypeName;
                        if (missingComponents.ContainsKey(firstComp))
                            missingComponents[firstComp] += 1;
                        else missingComponents[firstComp] = 1;
                    }
                }
                else if (slim.IsFullIntegrity && !slim.HasDeformation)
                {
                    continue;
                }

                slim.GetMissingComponents(missingComponents);

                if (!MyAPIGateway.Session.CreativeMode && comp.Session.MissingComponents.Count > 0 && !comp.TryPullComponents())
                    continue;

                if (projector != null)
                {
                    if (projector.CanBuild(slim, true) != BuildCheckResult.OK)
                        continue;

                    if (!MyAPIGateway.Session.CreativeMode && inventory.RemoveItemsOfType(1, blockDef.Components[0].Definition.Id) < 1)
                        continue;

                    var builtBy = comp.IsBlock ? comp.BlockTool.SlimBlock.BuiltBy : ownerId;
                    projector.Build(slim, ownerId, tool.EntityId, true, builtBy);

                    var pos = projector.CubeGrid.WorldToGridInteger(slim.CubeGrid.GridIntegerToWorld(slim.Position));
                    var newSlim = projector.CubeGrid.GetCubeBlock(pos);
                    if (comp.Definition.CacheBlocks && newSlim != null && !newSlim.IsFullIntegrity)
                    {
                        comp.WorkSet.Add(newSlim);
                    }

                    comp.Working = true;
                    validBlocks++;
                    continue;
                }

                if (!slim.CanContinueBuild(inventory))
                    continue;

                //if (welder != null)
                //{
                //    if (slim.WillBecomeFunctional(weldAmount) && !welder.IsWithinWorldLimits(gridComp.Projector, "", cubeDef.PCU - 1))
                //        continue;
                //}

                comp.Working = true;
                validBlocks++;

                slim.MoveItemsToConstructionStockpile(inventory);

                slim.IncreaseMountLevel(weldAmount, ownerId, inventory, 0.15f, false);

                if (comp.Definition.CacheBlocks && !slim.IsFullIntegrity || slim.HasDeformation)
                {
                    comp.WorkSet.Add(slim);
                }
            }

        }

        private static bool TryPullComponents(this ToolComp comp)
        {
            var inventory = comp.Inventory;
            var tool = comp.ToolEntity;

            var first = true;
            foreach (var component in comp.Session.MissingComponents)
            {
                var required = component.Value;
                var defId = new MyDefinitionId(typeof(MyObjectBuilder_Component), component.Key);

                var current = inventory.GetItemAmount(defId);
                var difference = required - current;
                if (comp.IsBlock && difference > 0 && inventory.CargoPercentage < 0.999f)
                {
                    current += comp.Grid.ConveyorSystem.PullItem(defId, difference, tool, inventory, false);
                }

                if (current < 1)
                {
                    if (first)
                        return false;

                    return true;
                }
                first = false;
            }
            return true;
        }

        internal static void WeldBlocksBulk(this ToolComp comp)
        {
            var inventory = comp.Inventory;
            var toolValues = comp.Values;
            var sortedBlocks = comp.HitBlocksSorted;
            var hitCount = sortedBlocks.Count;
            var tool = comp.ToolEntity;
            var validBlocks = 0;
            var maxBlocks = comp.Definition.Rate;
            var ownerId = comp.IsBlock ? comp.BlockTool.OwnerId : comp.HandTool.OwnerIdentityId;

            var missingComponents = comp.Session.MissingComponents;
            var buildCount = hitCount;

            var start = 0;
            var end = Math.Min(maxBlocks, hitCount);
            while (validBlocks < maxBlocks && start < hitCount)
            {
                for (int i = start; i < end; i++)
                {
                    var slim = sortedBlocks[i];

                    if (((MyCubeGrid)slim.CubeGrid).Projector != null)
                    {
                        var components = ((MyCubeBlockDefinition)slim.BlockDefinition).Components;
                        if (components != null && components.Length != 0)
                        {
                            var first = components[0].Definition.Id.SubtypeName;
                            if (missingComponents.ContainsKey(first))
                                missingComponents[first] += 1;
                            else missingComponents[first] = 1;
                        }
                        continue;
                    }

                    if (slim.IsFullIntegrity)
                    {
                        buildCount--;
                        continue;
                    }

                    slim.GetMissingComponents(missingComponents);
                }

                foreach (var component in missingComponents)
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
                    if (comp.IsBlock && difference > 0 && inventory.CargoPercentage < 0.999f)
                    {
                        current += comp.Grid.ConveyorSystem.PullItem(defId, difference, tool, inventory, false);
                    }

                }
                missingComponents.Clear();

                buildCount = buildCount > 0 ? buildCount : 1;
                buildCount = Math.Min(buildCount, maxBlocks);
                var weldScaler = 0.25f / (float)Math.Min(4, buildCount);
                var weldAmount = weldScaler * toolValues.Speed * MyAPIGateway.Session.WelderSpeedMultiplier;

                //var welder = null as IMyShipWelder;
                for (int j = start; j < end; j++)
                {
                    var slim = sortedBlocks[j];
                    var grid = (MyCubeGrid)slim.CubeGrid;

                    var cubeDef = slim.BlockDefinition as MyCubeBlockDefinition;
                    if (grid.Projector != null)
                    {
                        var projector = (IMyProjector)grid.Projector;
                        if (projector.CanBuild(slim, true) != BuildCheckResult.OK)
                            continue;

                        if (!MyAPIGateway.Session.CreativeMode && inventory.RemoveItemsOfType(1, cubeDef.Components[0].Definition.Id) < 1)
                            continue;

                        var builtBy = comp.IsBlock ? comp.BlockTool.SlimBlock.BuiltBy : ownerId;
                        projector.Build(slim, ownerId, tool.EntityId, true, builtBy);

                        var pos = projector.CubeGrid.WorldToGridInteger(slim.CubeGrid.GridIntegerToWorld(slim.Position));
                        var newSlim = projector.CubeGrid.GetCubeBlock(pos);
                        if (comp.Definition.CacheBlocks && newSlim != null && !newSlim.IsFullIntegrity)
                        {
                            comp.WorkSet.Add(newSlim);
                        }

                        comp.Working = true;
                        validBlocks++;
                        continue;
                    }

                    if (!slim.CanContinueBuild(inventory))
                        continue;

                    //if (welder != null)
                    //{
                    //    if (slim.WillBecomeFunctional(weldAmount) && !welder.IsWithinWorldLimits(gridComp.Projector, "", cubeDef.PCU - 1))
                    //        continue;
                    //}

                    comp.Working = true;
                    validBlocks++;

                    slim.MoveItemsToConstructionStockpile(inventory);

                    slim.IncreaseMountLevel(weldAmount, ownerId, inventory, 0.15f, false);

                    if (comp.Definition.CacheBlocks && !slim.IsFullIntegrity || slim.HasDeformation)
                    {
                        comp.WorkSet.Add(slim);
                    }

                }

                start += maxBlocks;
                end = Math.Min(end + maxBlocks, hitCount);
            }
        }
    }
}

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
using VRage;
using VRage.Collections;
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
using ToolCore.Session;
using ToolCore.Comp;
using ToolCore.Utils;
using static ToolCore.Utils.Draw;
using PositionData = ToolCore.Comp.ToolComp.PositionData;
using StorageInfo = ToolCore.Comp.ToolComp.StorageInfo;

namespace ToolCore
{
    internal static class VoxelUtils
    {

        internal static void DrillSphere(this ToolComp comp)
        {
            var session = comp.Session;
            var def = comp.Definition;
            var drillData = comp.DrillData;
            var forward = drillData.Direction;
            var radius = def.Radius;
            var extendedRadius = radius + 0.5f;
            var extRadiusSqr = extendedRadius * extendedRadius;

            var voxel = drillData.Voxel;
            var min = drillData.Min;
            var max = drillData.Max;
            var centre = drillData.Origin - (Vector3D)min;

            if (def.Debug) session.DrawBoxes.ClearImmediate();

            var reduction = (int)(def.Speed * 255);
            using ((voxel as MyVoxelBase).Pin())
            {
                var data = new MyStorageData();
                data.Resize(min, max);

                session.DsUtil.Start("read");
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);
                session.DsUtil.Complete("read", true);

                Vector3I pos;
                MyFixedPoint amount = 0;

                int content;
                byte material;

                session.DsUtil.Start("sort");
                var maxLayer = 0;
                var foundContent = false;
                for (int i = 0; i < data.SizeLinear; i++)
                {
                    data.ComputePosition(i, out pos);

                    content = data.Content(i);
                    if (content == 0)
                        continue;

                    var offset = (Vector3D)pos - centre;
                    var distSqr = offset.LengthSquared();
                    if (distSqr > extRadiusSqr)
                        continue;

                    foundContent = true;

                    var dist = 0f;
                    switch (def.Pattern)
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

                    var posData = new PositionData(i, dist, 0f);

                    var roundDist = MathHelper.RoundToInt(dist);
                    if (roundDist > maxLayer) maxLayer = roundDist;

                    List<PositionData> layer;
                    if (comp.WorkLayers.TryGetValue(roundDist, out layer))
                        layer.Add(posData);
                    else comp.WorkLayers[roundDist] = new List<PositionData>() { posData };
                }

                if (foundContent) comp.StorageDatas.Add(new StorageInfo(min, max));
                session.DsUtil.Complete("sort", true);

                session.DsUtil.Start("calc");
                var hit = false;
                for (int i = 0; i <= maxLayer; i++)
                {
                    List<PositionData> layer;
                    if (!comp.WorkLayers.TryGetValue(i, out layer))
                        continue;

                    var maxContent = 0;
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

                        if (comp.Definition.Debug && comp.Session.DrawBoxes.Count < 100)
                        {
                            var matrix = voxel.PositionComp.WorldMatrixRef;
                            matrix.Translation = voxel.PositionLeftBottomCorner;
                            data.ComputePosition(index, out pos);
                            pos += min;
                            var lowerHalf = (Vector3D)pos - 0.475;
                            var upperHalf = (Vector3D)pos + 0.475;
                            var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                            var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                            var color = (Color)Vector4.Lerp(Color.Red, Color.Green, overlap);
                            comp.Session.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(obb, color));
                        }

                        if (overlap < 1f)
                        {
                            overlap *= 255;
                            var excluded = 255 - MathHelper.FloorToInt(overlap);
                            var excess = content - excluded;
                            if (excess <= 0f)
                                continue;

                            removal = Math.Min(removal, excess);
                        }

                        var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        var hardness = (voxelDef != null) ? session.Settings.MaterialModifiers[voxelDef] : 1f;
                        var effectiveContent = MathHelper.CeilToInt(content * hardness);
                        maxContent = Math.Max(maxContent, effectiveContent);

                        if (!hit)
                        {
                            //data.ComputePosition(index, out testPos);
                            //var localPos = (Vector3D)testPos + min;
                            //var voxelMatrix = voxel.PositionComp.WorldMatrixRef;
                            //voxelMatrix.Translation = voxel.PositionLeftBottomCorner;
                            //Vector3D worldPos;
                            //Vector3D.Transform(ref localPos, ref voxelMatrix, out worldPos);
                            //comp.HitInfo.Update(worldPos, voxelDef.MaterialTypeNameHash);

                            hit = true;
                            comp.Hitting = true;
                        }

                        if (comp.Session.IsServer && voxelDef != null && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                            oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                            var yield = (removal / 255f) * voxelDef.MinedOreRatio * def.HarvestRatio * session.VoxelHarvestRatio;

                            if (!comp.Yields.TryAdd(oreOb, yield))
                                comp.Yields[oreOb] += yield;
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
                session.DsUtil.Complete("calc", true);

                session.DsUtil.Start("write");
                voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);
                session.DsUtil.Complete("write", true);

            }
            comp.WorkLayers.Clear();

        }

        internal static void DrillCylinder(this ToolComp comp)
        {
            var session = comp.Session;
            var drillData = comp.DrillData;
            var def = comp.Definition;

            var centre = drillData.Origin;
            var forward = drillData.Direction;
            var radius = def.Radius;
            var length = def.Length;
            var endOffset = forward * (length / 2f);

            var voxel = drillData.Voxel;
            var min = drillData.Min;
            var max = drillData.Max;

            var halfLenSqr = Math.Pow(length / 2f, 2);
            var radiusSqr = (float)Math.Pow(radius, 2);
            var reduction = (int)(def.Speed * 255);
            using ((voxel as MyVoxelBase).Pin())
            {
                var data = new MyStorageData();
                data.Resize(min, max);

                session.DsUtil.Start("read");
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);
                session.DsUtil.Complete("read", true);

                MyFixedPoint amount = 0;
                Vector3I testPos = new Vector3I();

                byte content;
                byte material;

                session.DsUtil.Start("sort");
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
                            switch (def.Pattern)
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

                            if (comp.Definition.Debug)
                            {
                                var matrix = voxel.PositionComp.WorldMatrixRef;
                                matrix.Translation = voxel.PositionLeftBottomCorner;
                                var lowerHalf = (Vector3D)testPos - 0.5;
                                var upperHalf = (Vector3D)testPos + 0.5;
                                var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                                var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                                MyAPIGateway.Utilities.InvokeOnGameThread(() => DrawBox(obb, Color.BlueViolet, false, 1, 0.01f));
                            }

                            var posData = new PositionData(index, dist, secondaryDistSqr);

                            var roundDist = MathHelper.RoundToInt(dist);
                            if (roundDist > maxLayer) maxLayer = roundDist;

                            List<PositionData> layer;
                            if (comp.WorkLayers.TryGetValue(roundDist, out layer))
                                layer.Add(posData);
                            else comp.WorkLayers[roundDist] = new List<PositionData>() { posData };

                        }
                    }
                }
                session.DsUtil.Complete("sort", true);

                session.DsUtil.Start("calc");
                var foundContent = false;
                //MyAPIGateway.Utilities.ShowNotification($"{WorkLayers.Count} layers", 160);
                for (int i = 0; i <= maxLayer; i++)
                {
                    List<PositionData> layer;
                    if (!comp.WorkLayers.TryGetValue(i, out layer))
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
                        switch (def.Pattern)
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
                        foundContent |= removal > 0;
                        var newContent = removal >= content ? 0 : content - removal;

                        var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        if (voxelDef != null && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                            oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                            var yield = (content - newContent) / 255f * voxelDef.MinedOreRatio * def.HarvestRatio * session.VoxelHarvestRatio;

                            if (!comp.Yields.TryAdd(oreOb, yield))
                                comp.Yields[oreOb] += yield;
                        }

                        data.Content(index, (byte)newContent);
                        if (newContent == 0)
                            data.Material(index, byte.MaxValue);
                    }

                    reduction -= (byte)maxContent;
                    if (reduction <= 0)
                        break;
                }
                session.DsUtil.Complete("calc", true);

                session.DsUtil.Start("write");
                if (foundContent)
                {
                    comp.Hitting = true;
                    comp.StorageDatas.Add(new StorageInfo(min, max));
                    voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);
                }
                session.DsUtil.Complete("write", true);

            }
            comp.WorkLayers.Clear();

        }

        internal static void DrillLine(this ToolComp comp)
        {
            var session = comp.Session;

            session.DsUtil2.Start("total");
            var drillData = comp.DrillData;
            var def = comp.Definition;
            var origin = drillData.Origin;
            var worldForward = drillData.Direction;
            var radius = def.Radius;
            var radiusSqr = radius * radius;
            var halfLenSqr = radiusSqr;
            var length = def.Length;
            var reduction = (int)(def.Speed * 255);

            var voxel = drillData.Voxel;
            var size = voxel.Storage.Size;


            var totalLen = 0f;
            var segmentLen = 2f * radius;

            comp.Session.DrawBoxes.ClearImmediate();

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
                while (totalLen < def.Length)
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
                    session.DsUtil.Start("sort");
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
                                switch (def.Pattern)
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
                                if (comp.WorkLayers.TryGetValue(roundDist, out layer))
                                    layer.Add(posData);
                                else
                                    comp.WorkLayers[roundDist] = new List<PositionData>() { posData };

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
                        comp.StorageDatas.Add(new StorageInfo(min, max));

                    session.DsUtil.Complete("sort", true, true);

                    session.DsUtil.Start("calc");
                    if ((int)def.Pattern <= 2)
                        reduction = (int)(def.Speed * 255);

                    for (int i = 0; i <= maxLayer; i++)
                    {
                        List<PositionData> layer;
                        if (!comp.WorkLayers.TryGetValue(i, out layer))
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
                            switch (def.Pattern)
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
                            comp.Hitting |= removal > 0;
                            var newContent = content - removal;
                            if (removal > maxContent) maxContent = removal;

                            var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                            if (voxelDef != null && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                            {
                                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                                oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                                var yield = removal / 255f * voxelDef.MinedOreRatio * def.HarvestRatio * session.VoxelHarvestRatio;

                                if (!comp.Yields.TryAdd(oreOb, yield))
                                    comp.Yields[oreOb] += yield;
                            }

                            data.Content(index, (byte)newContent);
                            if (newContent == 0)
                                data.Material(index, byte.MaxValue);
                        }

                        reduction -= maxContent;
                        if (reduction <= 0)
                            break;
                    }
                    comp.WorkLayers.Clear();
                    session.DsUtil.Complete("calc", true, true);

                    voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);

                    if (reduction <= 0 && (int)def.Pattern > 2)
                        break;
                }


            }

            session.DsUtil2.Complete("total", true, true);

        }


    }
}

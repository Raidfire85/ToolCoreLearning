﻿using Sandbox.Definitions;
using Sandbox.Game.Entities;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Voxels;
using VRageMath;
using ToolCore.Definitions.Serialised;
using ToolCore.Comp;
using ToolCore.Utils;
using PositionData = ToolCore.Comp.ToolComp.PositionData;
using StorageInfo = ToolCore.Comp.ToolComp.StorageInfo;

namespace ToolCore
{
    internal static partial class VoxelUtils
    {

        internal static void DrillSphere(this ToolComp comp)
        {
            var session = comp.Session;
            var def = comp.Definition;
            var drillData = comp.DrillData;
            var toolValues = comp.Values;
            var forward = drillData.Direction;
            var radius = toolValues.Radius;
            var extendedRadius = radius + 0.5f;
            var extRadiusSqr = extendedRadius * extendedRadius;

            var voxel = drillData.Voxel;
            var min = drillData.Min;
            var max = drillData.Max;
            var centre = drillData.Origin - (Vector3D)min;

            if (def.Debug) session.DrawBoxes.ClearImmediate();

            var reduction = (int)(toolValues.Speed * 255);
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
                    var radialDist = 0f;
                    switch (def.Pattern)
                    {
                        case WorkOrder.InsideOut:
                            dist = (float)offset.Length();
                            radialDist = dist;
                            break;
                        case WorkOrder.OutsideIn:
                            radialDist = (float)offset.Length();
                            dist = radius - radialDist;
                            break;
                        case WorkOrder.Forward:
                            var displacement = Vector3D.ProjectOnVector(ref offset, ref forward);
                            dist = radius + ((float)displacement.Length() * Math.Sign(Vector3D.Dot(offset, forward)));
                            radialDist = (float)offset.Length();
                            break;
                        case WorkOrder.Backward:
                            displacement = Vector3D.ProjectOnVector(ref offset, ref forward);
                            dist = radius - ((float)displacement.Length() * Math.Sign(Vector3D.Dot(offset, forward)));
                            radialDist = (float)offset.Length();
                            break;
                        default:
                            break;
                    }

                    var posData = new PositionData(i, radialDist);

                    var roundDist = MathHelper.RoundToInt(dist);
                    if (roundDist > maxLayer) maxLayer = roundDist;

                    List<PositionData> layer;
                    if (comp.WorkLayers.TryGetValue(roundDist, out layer))
                        layer.Add(posData);
                    else
                        comp.WorkLayers[roundDist] = new List<PositionData>() { posData };
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

                        var overlap = radius + 0.5f - distance;
                        if (overlap <= 0f)
                            continue;

                        content = data.Content(index);
                        material = data.Material(index);

                        var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        var validVoxel = voxelDef != null;

                        var harvestRatio = 0f;
                        var hardness = 1f;
                        var removal = reduction;
                        if (validVoxel)
                        {
                            hardness = session.MaterialModifiers[voxelDef];
                            if (def.HasMaterialModifiers)
                            {
                                var modifiers = def.MaterialModifiers[voxelDef];
                                hardness /= modifiers.Speed > 0 ? modifiers.Speed : 1f;
                                harvestRatio = modifiers.HarvestRatio;
                            }
                            removal = (int)(removal / hardness);
                        }
                        removal = Math.Min(removal, content);

                        if (overlap < 1f)
                        {
                            overlap *= 255;
                            var excluded = 255 - MathHelper.FloorToInt(overlap);
                            var excess = content - excluded;
                            if (excess <= 0f)
                                continue;

                            removal = Math.Min(removal, excess);
                        }

                        var effectiveContent = MathHelper.FloorToInt(removal * hardness);
                        maxContent = Math.Max(maxContent, effectiveContent);

                        //if (def.Debug)
                        //{
                        //    var matrix = voxel.PositionComp.WorldMatrixRef;
                        //    matrix.Translation = voxel.PositionLeftBottomCorner;
                        //    data.ComputePosition(index, out pos);
                        //    pos += min;
                        //    var lowerHalf = (Vector3D)pos - 0.475;
                        //    var upperHalf = (Vector3D)pos + 0.475;
                        //    var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                        //    var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                        //    var color = (Color)Vector4.Lerp(Color.Red, Color.Green, overlap);
                        //    session.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(obb, color));
                        //}

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

                        harvestRatio *= toolValues.HarvestRatio;
                        if (harvestRatio > 0 && comp.Session.IsServer && validVoxel && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                            oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                            var yield = harvestRatio * voxelDef.MinedOreRatio * session.VoxelHarvestRatio * removal / 255f;

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
            var def = comp.Definition;
            var toolValues = comp.Values;
            var drillData = comp.DrillData;

            var voxel = drillData.Voxel;
            var min = drillData.Min;
            var max = drillData.Max;

            var centre = drillData.Origin - (Vector3D)min;
            var forward = drillData.Direction;
            var radius = toolValues.Radius;
            var length = toolValues.Length;

            var halfLen = length / 2;
            var halfLenPlusSqr = Math.Pow(halfLen + 0.5f, 2);
            var halfLenMinusSqr = Math.Pow(halfLen - 0.5f, 2);
            var radiusPlusSqr = (float)Math.Pow(radius + 0.5f, 2);
            var radiusMinusSqr = (float)Math.Pow(radius - 0.5f, 2);

            var reduction = (int)(toolValues.Speed * 255);
            using ((voxel as MyVoxelBase).Pin())
            {
                var data = new MyStorageData();
                data.Resize(min, max);

                session.DsUtil.Start("read");
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);
                session.DsUtil.Complete("read", true);

                MyFixedPoint amount = 0;
                Vector3I pos;

                byte content;
                byte material;

                session.DsUtil.Start("sort");
                var maxLayer = 0;
                for (int i = 0; i < data.SizeLinear; i++)
                {
                    content = data.Content(i);
                    if (content == 0)
                        continue;

                    data.ComputePosition(i, out pos);

                    var posD = (Vector3D)pos;

                    var offset = (Vector3D)pos - centre;
                    var radial = Vector3D.ProjectOnPlane(ref offset, ref forward);
                    var radialDistSqr = (float)radial.LengthSquared();
                    if (radialDistSqr > radiusPlusSqr)
                        continue;
                    var axial = Vector3D.ProjectOnVector(ref offset, ref forward);
                    var axialDistSqr = (float)axial.LengthSquared();
                    if (axialDistSqr > halfLenPlusSqr)
                        continue;

                    var axialDist = axialDistSqr > halfLenMinusSqr ? halfLen + 0.5f - (float)Math.Sqrt(axialDistSqr) : 1f;
                    var radialDist = radialDistSqr > radiusMinusSqr ? radius + 0.5f - (float)Math.Sqrt(radialDistSqr) : 1f;

                    var dist = 0f;
                    switch (def.Pattern)
                    {
                        case WorkOrder.InsideOut:
                            dist = (float)radial.Length();
                            break;
                        case WorkOrder.OutsideIn:
                            dist = radius - (float)radial.Length();
                            break;
                        case WorkOrder.Forward:
                            dist = length / 2f + (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, forward));
                            break;
                        case WorkOrder.Backward:
                            dist = length / 2f - (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, forward));
                            break;
                        default:
                            break;
                    }

                    var posData = new PositionData(i, axialDist, radialDist);

                    var roundDist = MathHelper.RoundToInt(dist);
                    if (roundDist > maxLayer) maxLayer = roundDist;

                    List<PositionData> layer;
                    if (comp.WorkLayers.TryGetValue(roundDist, out layer))
                        layer.Add(posData);
                    else comp.WorkLayers[roundDist] = new List<PositionData>() { posData };
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
                        var dist1 = positionData.Distance;
                        var dist2 = positionData.Distance2;

                        content = data.Content(index);
                        material = data.Material(index);

                        var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        var validVoxel = voxelDef != null;

                        var harvestRatio = 0f;
                        var hardness = 1f;
                        var removal = reduction;
                        if (validVoxel)
                        {
                            hardness = session.MaterialModifiers[voxelDef];
                            if (def.HasMaterialModifiers)
                            {
                                var modifiers = def.MaterialModifiers[voxelDef];
                                hardness /= modifiers.Speed > 0 ? modifiers.Speed : 1f;
                                harvestRatio = modifiers.HarvestRatio;
                            }
                            removal = (int)(reduction / hardness);
                        }
                        removal = Math.Min(removal, content);

                        var overlap = dist1 * dist2;
                        if (overlap < 1f)
                        {
                            overlap *= 255;
                            var excluded = 255 - MathHelper.FloorToInt(overlap);
                            var excess = content - excluded;
                            if (excess <= 0f)
                                continue;

                            removal = Math.Min(removal, excess);
                        }

                        var effectiveContent = MathHelper.FloorToInt(removal * hardness);
                        maxContent = Math.Max(maxContent, effectiveContent);

                        //if (overlap < 0.5f)
                        //{
                        //    var density = MathHelper.Clamp(overlap, -1, 1) * 0.5 + 0.5;
                        //    removal = (int)(removal * density);
                        //}
                        //else if (distance > i + 0.5f)
                        //{
                        //    var edgeDist = i - distance;
                        //    var density = MathHelper.Clamp(edgeDist, -1, 1) * 0.5 + 0.5;
                        //    var leftover = reduction - removal;
                        //    removal = (int)(removal * density) + leftover;
                        //}
                        foundContent |= removal > 0;

                        harvestRatio *= toolValues.HarvestRatio;
                        if (harvestRatio > 0 && comp.Session.IsServer && validVoxel && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                            oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                            var yield = removal * harvestRatio * voxelDef.MinedOreRatio * session.VoxelHarvestRatio / 255f;

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
            var toolValues = comp.Values;
            var origin = drillData.Origin;
            var worldForward = drillData.Direction;
            var length = toolValues.Length;
            var radius = toolValues.Radius;
            var radiusMinusSqr = (float)Math.Pow(radius - 0.5f, 2);
            var radiusSqr = radius * radius;
            var halfLenSqr = radiusSqr;
            var reduction = (int)(toolValues.Speed * 255);

            var voxel = drillData.Voxel;
            var size = voxel.Storage.Size;


            var totalLen = 0f;
            var segmentLen = 2f * radius;

            comp.Session.DrawBoxes.ClearImmediate();

            Vector3I pos = new Vector3I();

            byte content;
            byte material;

            Vector3D localForward;
            var matrixNI = voxel.PositionComp.WorldMatrixNormalizedInv;
            Vector3D.TransformNormal(ref worldForward, ref matrixNI, out localForward);
            var voxelWorldExtent = Vector3D.TransformNormal((voxel as MyVoxelBase).SizeInMetresHalf, voxel.WorldMatrix);

            var maxLayer = 0;
            using ((voxel as MyVoxelBase).Pin())
            {
                while (totalLen < toolValues.Length)
                {
                    totalLen += segmentLen;
                    var centreLen = totalLen - radius;

                    var worldCentre = origin + worldForward * centreLen;
                    var localCentre = Vector3D.Transform(worldCentre + voxelWorldExtent, voxel.WorldMatrixNormalizedInv);

                    var minExtent = Vector3I.Round(localCentre - radius - 1);
                    var maxExtent = Vector3I.Round(localCentre + radius + 1);

                    var min = Vector3I.Max(minExtent, Vector3I.Zero);
                    var max = Vector3I.Min(maxExtent, size);

                    var data = new MyStorageData();
                    data.Resize(min, max);
                    voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);

                    var foundContent = false;
                    session.DsUtil.Start("sort");
                    for (int i = min.X; i <= max.X; i++)
                    {
                        pos.X = i;
                        for (int j = min.Y; j <= max.Y; j++)
                        {
                            pos.Y = j;
                            for (int k = min.Z; k <= max.Z; k++)
                            {
                                pos.Z = k;

                                var relativePos = pos - min;
                                var index = data.ComputeLinear(ref relativePos);
                                if (index < 0 || index > data.SizeLinear)
                                    continue;

                                content = data.Content(index);
                                if (content == 0)
                                    continue;

                                var offset = (Vector3D)pos - localCentre;
                                var radial = Vector3D.ProjectOnPlane(ref offset, ref localForward);
                                var radialDistSqr = (float)radial.LengthSquared();
                                if (radialDistSqr > radiusSqr)
                                    continue;
                                var axial = Vector3D.ProjectOnVector(ref offset, ref localForward);
                                var axialDistSqr = (float)axial.LengthSquared();
                                if (axialDistSqr > halfLenSqr)
                                    continue;

                                var centreLenMinus = centreLen - 0.5f;
                                var axialDist = axialDistSqr > (centreLenMinus * centreLenMinus) ? centreLen + 0.5f - (float)Math.Sqrt(axialDistSqr) : 1f;
                                var radialDist = radialDistSqr > radiusMinusSqr ? radius + 0.5f - (float)Math.Sqrt(radialDistSqr) : 1f;

                                foundContent = true;
                                var dist = 0f;
                                switch (def.Pattern)
                                {
                                    case WorkOrder.InsideOut:
                                        dist = (float)radial.Length();
                                        break;
                                    case WorkOrder.OutsideIn:
                                        dist = radius - (float)radial.Length();
                                        break;
                                    case WorkOrder.Forward:
                                        dist = centreLen + (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, localForward));
                                        break;
                                    case WorkOrder.Backward:
                                        dist = centreLen - (float)axial.Length() * Math.Sign(Vector3D.Dot(axial, localForward));
                                        break;
                                    default:
                                        break;
                                }

                                var posData = new PositionData(index, axialDist, radialDist);

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
                        reduction = (int)(toolValues.Speed * 255);

                    var hit = false;
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
                            var dist1 = posData.Distance;
                            var dist2 = posData.Distance2;

                            content = data.Content(index);
                            material = data.Material(index);

                            var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                            var validVoxel = voxelDef != null;

                            var harvestRatio = 0f;
                            var hardness = 1f;
                            var removal = reduction;
                            if (validVoxel)
                            {
                                hardness = session.MaterialModifiers[voxelDef];
                                if (def.HasMaterialModifiers)
                                {
                                    var modifiers = def.MaterialModifiers[voxelDef];
                                    hardness /= modifiers.Speed > 0 ? modifiers.Speed : 1f;
                                    harvestRatio = modifiers.HarvestRatio;
                                }
                                removal = (int)(reduction / hardness);
                            }
                            removal = Math.Min(removal, content);

                            var overlap = dist1 * dist2;
                            if (overlap < 1f)
                            {
                                overlap *= 255;
                                var excluded = 255 - MathHelper.FloorToInt(overlap);
                                var excess = content - excluded;
                                if (excess <= 0f)
                                    continue;

                                removal = Math.Min(removal, excess);
                            }

                            var effectiveContent = MathHelper.FloorToInt(removal * hardness);
                            maxContent = Math.Max(maxContent, effectiveContent);

                            if (!hit)
                            {
                                hit = true;
                                comp.Hitting = true;
                            }

                            harvestRatio *= toolValues.HarvestRatio;
                            if (harvestRatio > 0 && comp.Session.IsServer && validVoxel && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                            {
                                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                                oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                                var yield = removal * harvestRatio * voxelDef.MinedOreRatio * session.VoxelHarvestRatio / 255f;

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
                    comp.WorkLayers.Clear();
                    session.DsUtil.Complete("calc", true, true);

                    voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);

                    if (reduction <= 0 && (int)def.Pattern > 2)
                        break;
                }
            }

            session.DsUtil2.Complete("total", true, true);

        }

        internal static void DrillCuboid(this ToolComp comp)
        {
            try
            {
                var session = comp.Session;
                var def = comp.Definition;
                var drillData = comp.DrillData;
                var toolValues = comp.Values;
                var forward = drillData.Direction;
                var radius = toolValues.BoundingRadius;
                var hE = toolValues.HalfExtent;

                var voxel = drillData.Voxel;
                var min = drillData.Min;
                var max = drillData.Max;
                var centre = drillData.Origin;

                if (def.Debug) session.DrawBoxes.ClearImmediate();

                var reduction = (int)(toolValues.Speed * 255);
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
                    var obb = comp.Obb;
                    for (int i = 0; i < data.SizeLinear; i++)
                    {
                        content = data.Content(i);
                        if (content == 0)
                            continue;

                        data.ComputePosition(i, out pos);

                        var posD = (Vector3D)pos + min;
                        var minPos = posD - 0.5;
                        var box = new BoundingBoxD(minPos, minPos + 1);

                        var containment = obb.Contains(ref box);

                        if (containment == ContainmentType.Disjoint)
                            continue;

                        foundContent = true;

                        var offset = posD - centre;

                        var dist = 0f;
                        switch (def.Pattern)
                        {
                            case WorkOrder.InsideOut:
                                dist = (float)Vector3D.Abs(posD - centre).Sum;
                                break;
                            case WorkOrder.OutsideIn:
                                dist = hE.Sum - (float)Vector3D.Abs(posD - centre).Sum;
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

                        var posData = new PositionData(i, dist, minPos, containment == ContainmentType.Contains);

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
                            var posData = layer[j];
                            var index = posData.Index;
                            var distance = posData.Distance;

                            content = data.Content(index);
                            material = data.Material(index);

                            var voxelDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                            var validVoxel = voxelDef != null;

                            var harvestRatio = 0f;
                            var hardness = 1f;
                            var removal = reduction;
                            if (validVoxel)
                            {
                                hardness = session.MaterialModifiers[voxelDef];
                                if (def.HasMaterialModifiers)
                                {
                                    var modifiers = def.MaterialModifiers[voxelDef];
                                    hardness /= modifiers.Speed > 0 ? modifiers.Speed : 1f;
                                    harvestRatio = modifiers.HarvestRatio;
                                }
                                removal = (int)(removal / hardness);
                            }
                            removal = Math.Min(removal, content);

                            var overlap = posData.Contained ? 1f : Overlap(posData.Position, 3, obb);

                            if (overlap < 1f)
                            {
                                overlap *= 255;
                                var excluded = 255 - MathHelper.FloorToInt(overlap);
                                var excess = content - excluded;
                                if (excess <= 0f)
                                    continue;

                                removal = Math.Min(removal, excess);
                            }

                            var effectiveContent = MathHelper.FloorToInt(removal * hardness);
                            maxContent = Math.Max(maxContent, effectiveContent);

                            //if (overlap < 1 && def.Debug)
                            //{
                            //    var matrix = voxel.PositionComp.WorldMatrixRef;
                            //    matrix.Translation = voxel.PositionLeftBottomCorner;
                            //    data.ComputePosition(index, out pos);
                            //    pos += min;
                            //    var lowerHalf = (Vector3D)pos - 0.475;
                            //    var upperHalf = (Vector3D)pos + 0.475;
                            //    var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                            //    var boxObb = new MyOrientedBoundingBoxD(bbb, matrix);
                            //    var color = (Color)Vector4.Lerp(Color.Red, Color.Green, overlap);
                            //    session.DrawBoxes.Add(new MyTuple<MyOrientedBoundingBoxD, Color>(boxObb, color));
                            //}

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

                            harvestRatio *= toolValues.HarvestRatio;
                            if (harvestRatio > 0 && comp.Session.IsServer && validVoxel && voxelDef.CanBeHarvested && !string.IsNullOrEmpty(voxelDef.MinedOre))
                            {
                                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(voxelDef.MinedOre);
                                oreOb.MaterialTypeName = voxelDef.Id.SubtypeId;
                                var yield = removal * harvestRatio * voxelDef.MinedOreRatio * session.VoxelHarvestRatio / 255f;

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
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }
        }

        private static float Overlap(Vector3D min, int slices, MyOrientedBoundingBoxD obb)
        {
            var increment = 1.0 / slices;
            var edge = increment / 2.0;

            min += edge;

            Vector3D pos;
            var contained = 0;
            for (int i = 0; i < slices; i++)
            {
                pos.X = min.X + (i * increment);
                for (int j = 0; j < slices; j++)
                {
                    pos.Y = min.Y + (j * increment);
                    for (int k = 0; k < slices; k++)
                    {
                        pos.Z = min.Z + (k * increment);

                        if (obb.Contains(ref pos))
                            contained++;
                    }
                }
            }

            var total = Math.Pow(slices, 3);
            var halfUnit = (1 / total) / 2;
            var fraction = contained / total;

            return (float)MathHelper.Clamp(fraction, halfUnit, 1 - halfUnit);
        }
    }
}

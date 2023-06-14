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
using static ToolCore.Draw;
using static ToolCore.ToolDefinition;

namespace ToolCore
{
    internal partial class ToolComp
    {
        internal void DrillSphere2()
        {
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
                Session.DsUtil.Complete("read", true, true);

                MyFixedPoint amount = 0;
                Vector3I testPos = new Vector3I();

                byte content;
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
                if (foundContent) StorageDatas.Add(new StorageInfo(min, max));
                Session.DsUtil.Complete("sort", true, true);

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

                        var overlap = MathHelper.Clamp(radius + 0.5f - distance, 0f, 1f);
                        if (overlap == 0f)
                        {
                            Logs.WriteLine("Overlap zero, ignoring voxel");
                            continue;
                        }

                        Hitting = true;
                        if (overlap < 1f)
                        {
                            removal = (int)(removal * overlap);
                        }
                        removal = Math.Min(removal, content);

                        var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        if (def != null && def.CanBeHarvested && !string.IsNullOrEmpty(def.MinedOre))
                        {
                            var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
                            oreOb.MaterialTypeName = def.Id.SubtypeId;
                            var yield = removal / 255f * def.MinedOreRatio * Definition.VoxelHarvestRatio;

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
                Session.DsUtil.Complete("calc", true, true);

                Session.DsUtil.Start("write");
                voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max, false);
                Session.DsUtil.Complete("write", true, true);

            }
            WorkLayers.Clear();

        }
    }
}

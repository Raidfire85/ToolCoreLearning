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
using static ToolCore.Draw;

namespace ToolCore
{
    internal class VoxelUtils
    {
        private readonly Dictionary<MyObjectBuilder_Ore, float> _yields = new Dictionary<MyObjectBuilder_Ore, float>();
        internal void DrillSphere(IMyVoxelBase voxel, Vector3I min, Vector3I max, Vector3D centre, ToolComp comp)
        {
            var radius = comp.Definition.EffectSphere.Radius;
            var radiusSqr = Math.Pow(radius, 2);
            var radiusMinusOneSqr = Math.Pow(comp.Definition.EffectSphere.Radius - 1, 2);
            var reduction = (byte)(comp.Definition.Speed * 255);
            using ((voxel as MyVoxelBase).Pin())
            {
                var data = new MyStorageData();
                data.Resize(min, max);
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, min, max);

                MyFixedPoint amount = 0;
                Vector3I pos1;

                int i, j, k;
                switch (comp.Definition.Pattern)
                {
                    case WorkOrder.Uniform:
                        for (i = 0; i < data.SizeLinear; i++)
                        {
                            data.ComputePosition(i, out pos1);

                            var localPos = (Vector3D)(min + pos1);
                            var distSqr = Vector3D.DistanceSquared(localPos, centre);
                            if (distSqr > radiusSqr)
                                continue;

                            byte content;
                            byte material;
                            try
                            {
                                content = data.Content(i);
                                material = data.Material(i);
                            }
                            catch (Exception ex)
                            {
                                Logs.LogException(ex);
                                continue;
                            }

                            if (content == 0)
                                continue;

                            var removal = reduction;
                            if (Vector3D.DistanceSquared(localPos, centre) > radiusMinusOneSqr)
                            {
                                var dist = radius - Vector3D.Distance(localPos, centre);
                                var density = MathHelper.Clamp(dist, -1, 1) * 0.5 + 0.5;
                                removal = (byte)(removal * density);
                                Logs.WriteLine($"{dist} : {density} : {removal}");
                            }

                            var newContent = removal >= content ? MyVoxelConstants.VOXEL_CONTENT_EMPTY : (byte)(content - removal);

                            var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                            if (def != null && def.CanBeHarvested && !string.IsNullOrEmpty(def.MinedOre))
                            {
                                var oreOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
                                oreOb.MaterialTypeName = def.Id.SubtypeId;
                                var yield = (content - newContent) / 255f * def.MinedOreRatio * comp.Definition.HarvestRatio;

                                if (_yields.ContainsKey(oreOb))
                                {
                                    _yields[oreOb] += yield;
                                }
                                else
                                    _yields.Add(oreOb, yield);
                            }

                            data.Content(i, newContent);
                            if (newContent == 0)
                                data.Material(i, byte.MaxValue);

                            if (comp.Debug)
                            {
                                var matrix = voxel.PositionComp.WorldMatrixRef;
                                matrix.Translation = voxel.PositionLeftBottomCorner;
                                var lowerHalf = localPos - 0.5;
                                var upperHalf = localPos + 0.5;
                                var bbb = new BoundingBoxD(lowerHalf, upperHalf);
                                var obb = new MyOrientedBoundingBoxD(bbb, matrix);
                                DrawBox(obb, Color.BlueViolet, false, 1, 0.01f);
                            }
                        }
                        break;
                    default:
                        break;
                }



                if (comp.Inventory != null)
                {
                    foreach (var ore in _yields.Keys)
                    {
                        var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(ore);
                        amount = (MyFixedPoint)(_yields[ore] / itemDef.Volume);
                        comp.Inventory.AddItems(amount, ore);
                    }
                }
                _yields.Clear();

                voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Content, min, max);

            }
        }
    }
}

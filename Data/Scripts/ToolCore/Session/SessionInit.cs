using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using ToolCore.Definitions;
using ToolCore.Definitions.Serialised;
using static ToolCore.Definitions.Serialised.ToolCoreSettings;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {

        internal void LoadDefinitions()
        {
            foreach (var def in MyDefinitionManager.Static?.GetAllDefinitions())
            {
                if (def == null || !def.Enabled)
                    continue;

                if (def is MyPhysicalMaterialDefinition)
                    LoadPhysicalMaterial(def as MyPhysicalMaterialDefinition);

            }
        }

        internal void LoadVoxelMaterials()
        {
            foreach (var def in MyDefinitionManager.Static?.GetVoxelMaterialDefinitions())
            {
                if (def == null || !def.Enabled)
                    continue;

                LoadVoxelMaterial(def);
            }
        }

        internal void LoadPhysicalMaterial(MyPhysicalMaterialDefinition def)
        {
            var start = MyStringId.GetOrCompute("Start");
            Dictionary<MyStringHash, MyPhysicalMaterialDefinition.CollisionProperty> materialProperties;
            if (!def.CollisionProperties.TryGetValue(start, out materialProperties))
                return;

            var pMap = new Dictionary<MyStringHash, string>();
            var sMap = new Dictionary<MyStringHash, MySoundPair>();
            foreach (var material in materialProperties.Keys)
            {
                var cProp = materialProperties[material];
                pMap.Add(material, cProp.ParticleEffect);
                sMap.Add(material, cProp.Sound);
            }
            ParticleMap.Add(def.Id.SubtypeId, pMap);
            SoundMap.Add(def.Id.SubtypeId, sMap);
            Logs.WriteLine($"Added {pMap.Count} material properties for material {def.Id.SubtypeName}");
        }

        internal void LoadVoxelMaterial(MyVoxelMaterialDefinition def)
        {
            var materialName = def.MaterialTypeName;
            var categories = Settings.CategoryModifiers;

            var restitution = def.Restitution;
            if (restitution != 1 && restitution > 0)
            {
                Settings.MaterialModifiers.Add(def, restitution);
                return;
            }

            var isOre = !def.MinedOre.Equals("Stone") && !def.MinedOre.Equals("Ice");
            float hardness;
            if (isOre && categories.TryGetValue("Ore", out hardness))
            {
                Settings.MaterialModifiers.Add(def, hardness);
                return;
            }

            foreach (var category in categories.Keys)
            {
                if (!materialName.Contains(category))
                    continue;

                Settings.MaterialModifiers.Add(def, categories[category]);
                return;
            }

            if (categories.TryGetValue("Rock", out hardness))
                Settings.MaterialModifiers.Add(def, hardness);
        }

        internal void LoadToolCoreDefs()
        {
            foreach (var mod in MyAPIGateway.Session.Mods)
            {
                if (!MyAPIGateway.Utilities.FileExistsInModLocation(PATH, mod))
                    continue;

                using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(PATH, mod))
                {
                    while (reader.Peek() != -1)
                    {
                        ImportFile(reader.ReadLine(), mod);
                    }
                }
            }
        }

        private void ImportFile(string name, MyObjectBuilder_Checkpoint.ModItem mod)
        {
            if (name.Length <= 0) return;

            var path = "Data\\" + name;
            if (!MyAPIGateway.Utilities.FileExistsInModLocation(path, mod))
                return;

            Definitions.Serialised.Definitions definitions = null;
            using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(path, mod))
            {
                StringBuilder builder = new StringBuilder();
                while (reader.Peek() != -1)
                {
                    var line = reader.ReadLine();
                    if (line.Contains("<Definition xsi:type="))
                        line = "<Definition>";

                    builder.AppendLine(line);

                }

                var data = builder.ToString();
                try
                {
                    definitions = MyAPIGateway.Utilities.SerializeFromXML<Definitions.Serialised.Definitions>(data);

                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole(ex.ToString());
                }
            }

            if (definitions == null) return;

            ImportDefinitions(definitions.CubeBlocks);
            ImportDefinitions(definitions.Components);
            ImportDefinitions(definitions.PhysicalItems);
            ImportDefinitions(definitions.Definition);

        }

        private void ImportDefinitions(Definition[] definitions)
        {
            if (definitions == null || definitions.Length == 0) return;

            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition.ToolValues == null) continue;

                if (_toolTypes.Contains(definition.Id.TypeId))
                {
                    var toolDef = new ToolDefinition(definition.ToolValues, this);
                    DefinitionMap[definition.Id] = toolDef;
                }

            }
        }

        private void InitPlayers()
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Multiplayer.Players.GetPlayers(players);

            for (int i = 0; i < players.Count; i++)
                PlayerConnected(players[i].IdentityId);
        }


    }
}

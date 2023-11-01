using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ToolCore.Utils;
using static ToolCore.Definitions.ToolCoreSettings;

namespace ToolCore.Definitions
{
    internal class Settings
    {
        internal const string CONFIG_FILE = "ToolCore.cfg";
        internal const int CONFIG_VERSION = 0;

        internal ToolCoreSettings CoreSettings;

        internal readonly Dictionary<string, float> CategoryModifiers = new Dictionary<string, float>();

        internal void Clean()
        {
            CategoryModifiers.Clear();
        }

        internal void LoadConfig()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(CONFIG_FILE, typeof(Settings)))
                {

                    var writer = MyAPIGateway.Utilities.ReadFileInWorldStorage(CONFIG_FILE, typeof(Settings));

                    ToolCoreSettings xmlData = null;

                    try { xmlData = MyAPIGateway.Utilities.SerializeFromXML<ToolCoreSettings>(writer.ReadToEnd()); }
                    catch (Exception ex)
                    {
                        writer.Dispose();
                        Logs.LogException(ex);
                    }

                    writer.Dispose();

                    if (xmlData?.Version == CONFIG_VERSION)
                    {
                        Logs.WriteLine($"Found up to date config file");

                        CoreSettings = xmlData;
                        CorruptionCheck();
                        SaveConfig();
                    }
                    else
                    {
                        var versionStr = xmlData != null ? xmlData.Version.ToString() : "null";
                        Logs.WriteLine($"Found config file with version {versionStr} : updating to version {CONFIG_VERSION}");

                        GenerateConfig(xmlData);
                    }
                }
                else
                {
                    Logs.WriteLine($"No config file found, generating...");

                    GenerateConfig();
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

            for (int i = 0; i < CoreSettings.Materials.Length; i++)
            {
                var data = CoreSettings.Materials[i];
                CategoryModifiers.Add(data.Category, data.Hardness);
            }
            Logs.WriteLine($"Found {CategoryModifiers.Count} categories");
        }

        private void GenerateConfig(ToolCoreSettings oldSettings = null)
        {

            if (oldSettings != null)
            {
                RebuildConfig(oldSettings);
            }
            else
                CoreSettings = new ToolCoreSettings { Version = CONFIG_VERSION };

            CorruptionCheck();
            SaveConfig();
        }

        private void RebuildConfig(ToolCoreSettings oldSettings)
        {
            CoreSettings = new ToolCoreSettings { Version = CONFIG_VERSION };
        }

        private void CorruptionCheck()
        {
            var valid = false;
            if (CoreSettings.Materials != null && CoreSettings.Materials.Length > 0)
            {
                var materials = new List<MaterialData>();
                for (int i = 0; i < CoreSettings.Materials.Length; i++)
                {
                    var data = CoreSettings.Materials[i];
                    if (string.IsNullOrEmpty(data.Category) || data.Hardness == 0)
                        continue;

                    materials.Add(data);
                }

                valid = materials.Count > 0;
                if (valid)
                    CoreSettings.Materials = materials.ToArray();
            }

            if (!valid)
                RecreateMaterials();


        }

        private void RecreateMaterials()
        {
            CoreSettings.Materials = MaterialData.Default();
        }

        private void SaveConfig()
        {
            MyAPIGateway.Utilities.DeleteFileInWorldStorage(CONFIG_FILE, typeof(Settings));
            var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(CONFIG_FILE, typeof(Settings));
            var data = MyAPIGateway.Utilities.SerializeToXML(CoreSettings);
            Write(writer, data);
        }

        private static void Write(TextWriter writer, string data)
        {
            writer.Write(data);
            writer.Flush();
            writer.Dispose();
        }

    }

    #region Settings

    [ProtoContract]
    public class ToolCoreSettings
    {
        [ProtoMember(1)] public int Version = 0;
        [ProtoMember(2)] public MaterialData[] Materials = MaterialData.Default();

        [ProtoContract]
        public class MaterialData
        {
            [XmlAttribute][ProtoMember(1)] public string Category;
            [XmlAttribute][ProtoMember(2)] public float Hardness;

            public static MaterialData[] Default()
            {
                return new MaterialData[]
                {
                    new MaterialData { Category = "Sand", Hardness = 0.75f },
                    new MaterialData { Category = "Snow", Hardness = 0.8f },
                    new MaterialData { Category = "Grass", Hardness = 0.85f },
                    new MaterialData { Category = "Soil", Hardness = 0.9f },
                    new MaterialData { Category = "Ice", Hardness = 0.95f },
                    new MaterialData { Category = "Rock", Hardness = 1f },
                    new MaterialData { Category = "Ore", Hardness = 1f },
                };
            }
        }
    }


    #endregion

}

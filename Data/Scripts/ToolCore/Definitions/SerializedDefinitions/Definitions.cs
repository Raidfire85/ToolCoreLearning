using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;
using VRageMath;

namespace ToolCore.Definitions.Serialised
{
    #region Definitions

    /// <summary>
    /// Fields which will be deserialised from xml
    /// </summary>
    public class ToolValues
    {
        public ToolType ToolType;
        public EffectShape EffectShape;
        public WorkOrder WorkOrder;

        public string Emitter;

        public SerializableVector3D Offset = Vector3D.Zero;

        public SerializableVector3D HalfExtent = new Vector3D(1f, 1f, 1f); //Cuboid
        public float Radius = 1f; //Sphere, Cylinder
        public float Length = 1f; //Cylinder, Line

        public float IdlePower = 0f;
        public float ActivePower = 1f;
        public float Speed = 1f;
        public float HarvestRatio = 1f;
        public int UpdateInterval = 10;

        public bool AffectOwnGrid = false;
        public bool Turret = false;

        public Event[] Events;

        public Material[] MaterialSpecificModifiers;
    }

    public enum ToolType
    {
        Drill = 1,
        Grind = 2,
        GrindDrill = 3,
        Weld = 4,
        WeldDrill = 5,
        WeldGrind = 6,
        Multi = 7,
    }

    public enum EffectShape
    {
        Sphere = 0,
        Cylinder = 1,
        Cuboid = 2,
        Line = 3,
        Ray = 4,
    }

    public enum WorkOrder
    {
        Uniform = 0,
        InsideOut = 1,
        OutsideIn = 2,
        Forward = 3,
        Backward = 4,
    }

    public class Event
    {
        public Trigger Trigger;
        public Animation[] Animations;
        public ParticleEffect[] ParticleEffects;
        public Sound Sound;

    }

    [Flags]
    public enum Trigger
    {
        None = 0,
        Functional = 1,
        Powered = 2,
        Enabled = 4,
        Activated = 8,
        LeftClick = 16,
        RightClick = 32,
        Click = 48,
        Active = 56,
        Hit = 64,

    }

    public class Animation
    {
        public string Subpart;
        public AnimationType Type;
        public SerializableVector3 Direction = Vector3.Zero;
        public float Speed = 1f; //(degrees/metres) per second
        public int WindupTime = 0; //ticks
    }

    public class ParticleEffect
    {
        public string Dummy;
        public string Name;
        public SerializableVector3 Offset = Vector3.Zero;
        public bool Loop;
    }

    public class Sound
    {
        public string Name;
    }

    public enum AnimationType
    {
        Rotate = 0,
        Linear = 1,
        Hide = 2,
        Unhide = 3,
    }

    public class Material
    {
        public string Category;
        public string SubtypeId;
        public float Speed;
        public float HarvestRatio;
    }

    #endregion

    #region Settings

    [ProtoContract]
    public class ToolCoreSettings
    {
        [ProtoMember(1)] public int Version = 0;
        [ProtoMember(2)] public MaterialData[] Materials = new MaterialData[]
        {
            new MaterialData { Category = "Snow", Hardness = 0.2f },
            new MaterialData { Category = "Sand", Hardness = 0.4f },
            new MaterialData { Category = "Soil", Hardness = 0.6f },
            new MaterialData { Category = "Grass", Hardness = 0.6f },
            new MaterialData { Category = "Ice", Hardness = 0.8f },
            new MaterialData { Category = "Rock", Hardness = 1f },
            new MaterialData { Category = "Ore", Hardness = 1f },
        };

        [ProtoContract]
        public class MaterialData
        {
            [ProtoMember(1)] public string Category;
            [ProtoMember(2)] public float Hardness;
        }
    }


    #endregion

    /// <summary>
    /// Skeleton classes for deserialising from xml
    /// </summary>
    public class Definitions
    {
        public Definition[] CubeBlocks;
        public Component[] Components;
        public PhysicalItem[] PhysicalItems;
        public Definition[] Definition;
    }


    public class PhysicalItem : Definition
    {

    }

    public class Component : Definition
    {

    }

    public class Definition
    {
        public SerializableDefinitionId Id;
        public ToolValues ToolValues;

    }
}

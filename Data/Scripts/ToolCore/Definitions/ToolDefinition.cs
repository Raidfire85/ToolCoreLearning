using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using ToolCore.Session;
using ToolCore.Comp;
using ToolCore.Utils;
using ToolCore.Definitions.Serialised;

namespace ToolCore.Definitions
{
    /// <summary>
    /// Stored values from cubeblock definitions plus precalculated constants
    /// </summary>
    internal class ToolDefinition
    {
        internal readonly ToolType ToolType;
        internal readonly EffectShape EffectShape;
        internal WorkOrder Pattern;
        internal Location Location;
        internal string EmitterName;
        internal bool Turret;
        internal bool AffectOwnGrid;
        internal Vector3D Offset;
        internal float Speed;
        internal float ActivePower;
        internal float IdlePower;

        //Shape dimensions
        internal Vector3D HalfExtent;
        internal float Radius;
        internal float Length;

        //BoundingSphere Dimensions
        internal float SegmentLength;
        internal float BoundingRadius;
        internal float SegmentBoundingRadius;
        internal float SegmentRatio = 1f;

        internal BoundingSphereD EffectSphere;
        internal BoundingBox EffectBox;

        internal float HarvestRatio;

        internal readonly List<ToolComp.ToolMode> ToolModes = new List<ToolComp.ToolMode>();
        internal readonly List<ToolComp.ToolAction> ToolActions = new List<ToolComp.ToolAction>();
        internal readonly List<List<Vector3I>> Layers = new List<List<Vector3I>>();

        internal readonly Trigger EventFlags;
        internal readonly Dictionary<Trigger, MyTuple<List<AnimationDef>, List<ParticleEffectDef>, List<BeamDef>, SoundDef>> EventEffectDefs = new Dictionary<Trigger, MyTuple<List<AnimationDef>, List<ParticleEffectDef>, List<BeamDef>, SoundDef>>();
        
        internal class AnimationDef
        {
            internal readonly string Subpart;
            internal readonly AnimationType Type;
            internal readonly Vector3 Direction;
            internal readonly float Speed;
            internal readonly int WindupTime;
            internal readonly Matrix Transform;
            internal readonly bool HasWindup;
            internal readonly float WindupRadsFraction;

            internal AnimationDef(Animation animation)
            {
                Subpart = animation.Subpart;
                Type = animation.Type;
                Direction = animation.Direction;
                Speed = animation.Speed;
                WindupTime = animation.WindupTime;
                HasWindup = WindupTime > 0;

                var speedTicks = Speed / 60f;
                switch (Type)
                {
                    case AnimationType.Rotate:
                        var radsPerTick = MathHelper.ToRadians(speedTicks);
                        Transform = Matrix.CreateFromAxisAngle(Direction, radsPerTick);
                        if (HasWindup) WindupRadsFraction = radsPerTick / WindupTime;
                        break;
                    case AnimationType.Linear:
                        Transform = Matrix.CreateTranslation(Direction * speedTicks);
                        break;
                    default:
                        Transform = Matrix.Zero;
                        break;

                }
                Logs.WriteLine("Animation Matrix:");
                Logs.WriteLine(Transform.ToString());
            }
        }

        internal class ParticleEffectDef
        {
            internal readonly string Name;
            internal readonly Location Location;
            internal readonly string Dummy;
            internal readonly Vector3 Offset;
            internal readonly bool Loop;
            internal readonly bool Lookup;

            internal readonly Dictionary<MyStringHash, string> ParticleMap;

            public ParticleEffectDef(ParticleEffect particleEffect, ToolSession session)
            {
                Name = particleEffect.Name;
                Location = particleEffect.Location;
                Dummy = particleEffect.Dummy;
                Offset = particleEffect.Offset;
                Loop = particleEffect.Loop;

                if (!Name.StartsWith("MaterialProperties"))
                    return;

                var material = Name.Substring(19);
                Logs.WriteLine(material);

                var mHash = MyStringHash.GetOrCompute(material);
                Lookup = session.ParticleMap.TryGetValue(mHash, out ParticleMap);
            }
        }

        internal class BeamDef
        {
            internal readonly string Start;
            internal readonly string End;
            internal readonly bool EndAtHit;
            internal readonly MyStringId Material;
            internal readonly float Width;
            internal readonly Vector4 Color;

            internal BeamDef(Beam beam)
            {
                Start = beam.Start;
                End = beam.End;
                EndAtHit = End.Equals("Hit");
                Material = MyStringId.GetOrCompute(beam.Material);
                Width = beam.Width;
                Color = beam.Color;
            }
        }

        internal class SoundDef
        {
            internal readonly MySoundPair SoundPair;
            internal readonly string Name;
            internal readonly bool Lookup;

            internal readonly Dictionary<MyStringHash, MySoundPair> SoundMap;

            public SoundDef(Sound sound, ToolSession session)
            {
                Name = sound.Name;

                if (!Name.StartsWith("MaterialProperties"))
                {
                    SoundPair = new MySoundPair(Name, false);
                    return;
                }

                var material = Name.Substring(19);
                Logs.WriteLine(material);

                var mHash = MyStringHash.GetOrCompute(material);
                Lookup = session.SoundMap.TryGetValue(mHash, out SoundMap);
            }
        }

        public ToolDefinition(ToolValues values, ToolSession session)
        {
            ToolType = values.ToolType;
            EffectShape = values.EffectShape;
            Pattern = values.WorkOrder;
            Location = values.WorkOrigin;
            EmitterName = values.Emitter;
            Turret = values.Turret;
            AffectOwnGrid = values.AffectOwnGrid;
            Offset = values.Offset;
            Speed = values.Speed;
            ActivePower = values.ActivePower;
            IdlePower = values.IdlePower;

            HarvestRatio = values.HarvestRatio;

            HalfExtent = values.HalfExtent;
            Radius = values.Radius;
            Length = values.Length;

            if ((ToolType & ToolType.Drill) > 0)
                ToolModes.Add(ToolComp.ToolMode.Drill);
            if ((ToolType & ToolType.Grind) > 0)
                ToolModes.Add(ToolComp.ToolMode.Grind);
            if ((ToolType & ToolType.Weld) > 0)
                ToolModes.Add(ToolComp.ToolMode.Weld);

            if (ToolModes.Count == 0)
                Logs.WriteLine($"No valid tool modes!");

            ToolActions.Add(ToolComp.ToolAction.Primary);
            ToolActions.Add(ToolComp.ToolAction.Secondary);


            int radius = 0;
            Vector3I pos = new Vector3I();
            List<Vector3I> layer;
            var tempLayers = new Dictionary<int, List<Vector3I>>();
            switch (EffectShape)
            {
                case EffectShape.Sphere:
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, Radius);
                    radius = MathHelper.CeilToInt(Radius);
                    for (int i = -radius; i <= radius; i++)
                    {
                        pos.X = i;
                        for (int j = -radius; j <= radius; j++)
                        {
                            pos.Y = j;
                            for (int k = -radius; k <= radius; k++)
                            {
                                pos.Z = k;
                                var dist = MathHelper.CeilToInt(((Vector3D)pos).Length());
                                if (dist > (radius + 1)) continue;
                                if (tempLayers.TryGetValue(dist, out layer))
                                    layer.Add(pos);
                                else tempLayers[dist] = new List<Vector3I>() { pos };
                            }
                        }
                    }
                    BoundingRadius = Radius;
                    Length = Location == Location.Hit ? Length : Radius;
                    break;
                case EffectShape.Cylinder:
                    HalfExtent = new Vector3(Radius, Radius, Length * 0.5f);
                    EffectBox = new BoundingBox(-HalfExtent, HalfExtent);
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, HalfExtent.Length());
                    BoundingRadius = (float)HalfExtent.Length();
                    break;
                case EffectShape.Cuboid:
                    EffectBox = new BoundingBox(-HalfExtent, HalfExtent);
                    var halfExtentLength = (float)HalfExtent.Length();
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, halfExtentLength);
                    BoundingRadius = halfExtentLength;
                    Length = halfExtentLength;
                    break;
                case EffectShape.Line:
                    BoundingRadius = Length * 0.5f; //mm
                    SegmentLength = Radius > 0 ? Length * 0.5f / Radius : Length;
                    var halfExtent = new Vector3(Radius, Radius, Radius);
                    SegmentBoundingRadius = halfExtent.Length();
                    break;
                case EffectShape.Ray:
                    break;
            }


            if (EffectShape == EffectShape.Sphere)
            {
                for (int i = 0; i <= radius + 1; i++)
                {
                    if (!tempLayers.ContainsKey(i))
                        continue;

                    Layers.Add(tempLayers[i]);
                }
            }


            foreach (var eventDef in values.Events)
            {
                var hasAnimations = eventDef.Animations != null && eventDef.Animations.Length > 0;
                var hasParticleEffects = eventDef.ParticleEffects != null && eventDef.ParticleEffects.Length > 0;
                var hasBeams = eventDef.Beams != null && eventDef.Beams.Length > 0;
                var hasSound = !string.IsNullOrEmpty(eventDef.Sound?.Name);
                if (!hasAnimations && !hasParticleEffects && !hasBeams && !hasSound)
                    continue;

                var animationDefs = new List<AnimationDef>();
                if (hasAnimations)
                {
                    foreach (var animation in eventDef.Animations)
                    {
                        animationDefs.Add(new AnimationDef(animation));
                    }
                }

                var particleEffectDefs = new List<ParticleEffectDef>();
                if (hasParticleEffects)
                {
                    foreach (var particleEffect in eventDef.ParticleEffects)
                    {
                        particleEffectDefs.Add(new ParticleEffectDef(particleEffect, session));
                    }
                }

                var beamDefs = new List<BeamDef>();
                if (hasBeams)
                {
                    foreach (var beam in eventDef.Beams)
                    {
                        beamDefs.Add(new BeamDef(beam));
                    }
                }

                SoundDef soundDef = null;
                if (hasSound)
                    soundDef = new SoundDef(eventDef.Sound, session);

                EventEffectDefs[eventDef.Trigger] = new MyTuple<List<AnimationDef>, List<ParticleEffectDef>, List<BeamDef>, SoundDef>(animationDefs, particleEffectDefs, beamDefs, soundDef);
                EventFlags |= eventDef.Trigger;
            }


        }

    }
}

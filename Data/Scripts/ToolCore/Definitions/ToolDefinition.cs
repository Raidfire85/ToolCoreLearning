using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRageMath;

namespace ToolCore
{
    /// <summary>
    /// Stored values from cubeblock definitions plus precalculated constants
    /// </summary>
    internal class ToolDefinition
    {
        internal readonly ToolType ToolType;
        internal readonly EffectShape EffectShape;
        internal WorkOrder Pattern;
        internal string EmitterName;
        internal bool Turret;
        internal bool AffectOwnGrid;
        internal Vector3D Offset;
        internal float Speed;

        //Shape dimensions
        internal Vector3D HalfExtent;
        internal float Radius;
        internal float Length;

        //BoundingSphere Dimensions
        internal float SegmentLength;
        internal float SegmentRadius;
        internal float BoundingRadius;
        internal float SegmentBoundingRadius;
        internal float SegmentRatio = 1f;

        internal BoundingSphereD EffectSphere;
        internal BoundingBox EffectBox;

        internal float VoxelHarvestRatio;

        internal readonly List<List<Vector3I>> Layers = new List<List<Vector3I>>();

        internal readonly Trigger EventFlags;
        internal readonly Dictionary<Trigger, MyTuple<List<AnimationDef>, List<ParticleEffectDef>, SoundDef>> EventEffectDefs = new Dictionary<Trigger, MyTuple<List<AnimationDef>, List<ParticleEffectDef>, SoundDef>>();
        internal readonly List<Animation> AnimationDefs = new List<Animation>();
        internal readonly List<ParticleEffect> ParticleEffectDefs = new List<ParticleEffect>();

        internal class AnimationDef
        {
            internal readonly string Subpart;
            internal readonly AnimationType Type;
            internal readonly Vector3 Direction;
            internal readonly float Speed;
            internal readonly int WindupTime;
            internal readonly Matrix Transform;

            internal AnimationDef(Animation animation)
            {
                Subpart = animation.Subpart;
                Type = animation.Type;
                Direction = animation.Direction;
                Speed = animation.Speed;
                WindupTime = animation.WindupTime;

                var speedTicks = Speed / 60f;
                switch (Type)
                {
                    case AnimationType.Rotate:
                        var radsPerTick = MathHelper.ToRadians(speedTicks);
                        Transform = Matrix.CreateFromAxisAngle(Direction, radsPerTick);
                        break;
                    case AnimationType.Linear:
                        Transform = Matrix.CreateTranslation(Direction * speedTicks);
                        break;
                    default:
                        Transform = Matrix.Zero;
                        break;

                }
            }
        }

        internal class ParticleEffectDef
        {
            internal readonly string Dummy;
            internal readonly string Name;
            internal readonly Vector3 Offset;
            internal readonly bool Loop;

            public ParticleEffectDef(ParticleEffect particleEffect)
            {
                Dummy = particleEffect.Dummy;
                Name = particleEffect.Name;
                Offset = particleEffect.Offset;
                Loop = particleEffect.Loop;
            }
        }

        internal class SoundDef
        {
            internal readonly MySoundPair SoundPair;
            internal readonly string Name;

            public SoundDef(Sound sound)
            {
                Name = sound.Name;
                SoundPair = new MySoundPair(Name, false);
            }
        }

        public ToolDefinition(ToolValues values)
        {
            ToolType = values.ToolType;
            EffectShape = values.EffectShape;
            Pattern = values.WorkOrder;
            EmitterName = values.Emitter;
            Turret = values.Turret;
            AffectOwnGrid = values.AffectOwnGrid;
            Offset = values.Offset;
            Speed = values.Speed;

            VoxelHarvestRatio = 0.009f * MyAPIGateway.Session.SessionSettings.HarvestRatioMultiplier; // * tool multiplier?


            HalfExtent = values.HalfExtent;
            Radius = values.Radius;
            Length = values.Length;




            try
            {
                Logs.WriteLine($"{ToolType}");
                Logs.WriteLine($"{EffectShape}");
                Logs.WriteLine($"{Pattern}");
                Logs.WriteLine($"{Offset}");
                Logs.WriteLine($"{Radius}");
                Logs.WriteLine($"{Length}");

            }
            catch (Exception ex)
            {
                Logs.WriteLine($"Exception in ToolDefinition() - {ex}");
            }

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
                    break;
                case EffectShape.Cylinder:
                    HalfExtent = new Vector3(Radius, Radius, Length * 0.5f);
                    EffectBox = new BoundingBox(-HalfExtent, HalfExtent);
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, HalfExtent.Length());
                    BoundingRadius = (float)HalfExtent.Length();
                    break;
                case EffectShape.Cuboid:
                    EffectBox = new BoundingBox(-HalfExtent, HalfExtent);
                    EffectSphere = new BoundingSphereD(Vector3D.Zero, HalfExtent.Length());
                    BoundingRadius = (float)HalfExtent.Length();
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
                Logs.WriteLine($"event def has particles: {hasParticleEffects}");
                var hasSound = !string.IsNullOrEmpty(eventDef.Sound?.Name);
                if (!hasAnimations && !hasParticleEffects && !hasSound)
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
                        particleEffectDefs.Add(new ParticleEffectDef(particleEffect));
                    }
                }

                SoundDef soundDef = null;
                if (hasSound)
                    soundDef = new SoundDef(eventDef.Sound);

                EventEffectDefs[eventDef.Trigger] = new MyTuple<List<AnimationDef>, List<ParticleEffectDef>, SoundDef>(animationDefs, particleEffectDefs, soundDef);
                EventFlags |= eventDef.Trigger;
            }


        }

    }
}

using System.Collections.Generic;
using ToolCore.Definitions.Serialised;
using ToolCore.Utils;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using static ToolCore.Definitions.ToolDefinition;

namespace ToolCore.Comp
{
    internal class Effects
    {
        internal readonly bool HasAnimations;
        internal readonly bool HasParticles;
        internal readonly bool HasBeams;
        internal readonly bool HasSound;
        internal readonly List<Animation> Animations;
        internal readonly List<ParticleEffect> ParticleEffects;
        internal readonly List<Beam> Beams;
        internal readonly SoundDef SoundDef;

        internal bool Active;
        internal bool Expired;
        internal bool Dirty;
        internal bool Restart;
        internal bool SoundStopped;
        internal int LastActiveTick;

        internal Effects(List<AnimationDef> animationDefs, List<ParticleEffectDef> particleEffectDefs, List<BeamDef> beamDefs, SoundDef soundDef, ToolComp comp)
        {
            var tool = comp.ToolEntity;

            if (animationDefs?.Count > 0)
            {
                Animations = new List<Animation>();
                foreach (var aDef in animationDefs)
                {
                    MyEntitySubpart subpart = null;
                    if (!tool.TryGetSubpartRecursive(aDef.Subpart, out subpart))
                    {
                        Logs.WriteLine($"Subpart '{aDef.Subpart}' not found!");
                        continue;
                    }

                    var anim = new Animation(aDef, subpart);
                    Animations.Add(anim);
                }
                HasAnimations = Animations.Count > 0;
            }

            if (particleEffectDefs?.Count > 0)
            {
                ParticleEffects = new List<ParticleEffect>();
                foreach (var pDef in particleEffectDefs)
                {
                    IMyModelDummy dummy = null;
                    MyEntity parent = tool;
                    if (pDef.Location == Location.Emitter && !tool.TryGetDummy(pDef.Dummy, out dummy, out parent))
                    {
                        Logs.WriteLine($"Dummy '{pDef.Dummy}' not found!");
                        continue;
                    }

                    var effect = new ParticleEffect(pDef, dummy, parent);
                    ParticleEffects.Add(effect);
                }
                HasParticles = ParticleEffects.Count > 0;
            }

            if (beamDefs?.Count > 0)
            {
                Beams = new List<Beam>();
                foreach (var beamDef in beamDefs)
                {
                    IMyModelDummy start = null;
                    MyEntity startParent = null;
                    if (!tool.TryGetDummy(beamDef.Start, out start, out startParent))
                    {
                        Logs.WriteLine($"Dummy '{beamDef.Start}' not found!");
                        continue;
                    }

                    IMyModelDummy end = null;
                    MyEntity endParent = null;
                    if (beamDef.EndLocation == Location.Emitter && !tool.TryGetDummy(beamDef.End, out end, out endParent))
                    {
                        Logs.WriteLine($"Dummy '{beamDef.End}' not found!");
                        continue;
                    }

                    var beam = new Beam(beamDef, start, end, startParent, endParent);
                    Beams.Add(beam);
                }
                HasBeams = Beams.Count > 0;
            }

            HasSound = (SoundDef = soundDef) != null;
        }

        internal void UpdateModelData(ToolComp comp)
        {
            if (HasAnimations)
            {
                foreach (var anim in Animations)
                {
                    MyEntitySubpart subpart;
                    if (comp.Subparts.TryGetValue(anim.Definition.Subpart, out subpart))
                    {
                        anim.Subpart = subpart;
                    }
                }
            }

            if (HasParticles)
            {
                foreach (var particle in ParticleEffects)
                {
                    IMyModelDummy dummy;
                    if (particle.Definition.Location == Location.Emitter && comp.Dummies.TryGetValue(particle.Definition.Dummy, out dummy))
                    {
                        particle.Dummy = dummy;
                        particle.Parent = comp.DummyMap[dummy];
                    }
                }
            }

            if (HasBeams)
            {
                foreach (var beam in Beams)
                {
                    IMyModelDummy start;
                    if (comp.Dummies.TryGetValue(beam.Definition.Start, out start))
                    {
                        beam.Start = start;
                        beam.StartParent = comp.DummyMap[start];
                    }

                    if (beam.Definition.EndLocation != Location.Emitter)
                        continue;

                    IMyModelDummy end;
                    if (comp.Dummies.TryGetValue(beam.Definition.End, out end))
                    {
                        beam.End = end;
                        beam.EndParent = comp.DummyMap[end];
                    }
                }
            }
        }

        internal void Clean()
        {
            Active = false;
            Expired = false;
            Dirty = false;
            Restart = false;
            SoundStopped = false;
            LastActiveTick = 0;
        }

        internal class Animation
        {
            internal readonly AnimationDef Definition;

            internal MyEntitySubpart Subpart;

            internal bool Starting;
            internal bool Running;
            internal bool Ending;
            internal int RemainingDuration;
            internal int TransitionState;

            public Animation(AnimationDef def, MyEntitySubpart subpart)
            {
                Definition = def;
                Subpart = subpart;
            }
        }

        internal class ParticleEffect
        {
            internal readonly ParticleEffectDef Definition;

            internal IMyModelDummy Dummy;
            internal MyEntity Parent;
            internal MyParticleEffect Particle;

            public ParticleEffect(ParticleEffectDef def, IMyModelDummy dummy, MyEntity parent)
            {
                Dummy = dummy;
                Parent = parent;
                Definition = def;
            }
        }

        internal class Beam
        {
            internal readonly BeamDef Definition;

            internal IMyModelDummy Start;
            internal IMyModelDummy End;
            internal MyEntity StartParent;
            internal MyEntity EndParent;

            public Beam(BeamDef def, IMyModelDummy start, IMyModelDummy end, MyEntity startParent, MyEntity endParent)
            {
                Definition = def;
                Start = start;
                End = end;
                StartParent = startParent;
                EndParent = endParent;
            }

        }
    }

}

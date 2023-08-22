using ObjectBuilders.SafeZone;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using ToolCore.Comp;
using ToolCore.Utils;
using static ToolCore.Comp.ToolComp;

namespace ToolCore.Session
{
    internal partial class ToolSession
    {
        internal void AvLoop()
        {
            AvComps.ApplyAdditions();
            for (int i = 0; i < AvComps.Count; i++)
            {
                var comp = AvComps[i];
                var tool = comp.Tool;
                var def = comp.Definition;

                MyAPIGateway.Utilities.ShowNotification($"Running {comp.ActiveEffects.Count} effects", 16);
                for (int j = comp.ActiveEffects.Count - 1; j >= 0; j--)
                {
                    var effects = comp.ActiveEffects[j];

                    if (tool.MarkedForClose)
                    {
                        effects.Expired = true;
                        effects.Dirty = true;
                        Logs.WriteLine("Caught effects on block marked for close");
                    }


                    var particlesFinished = !effects.HasParticles || RunParticles(effects);

                    var animationsFinished = !effects.HasAnimations || RunAnimations(effects);
                    
                    if (effects.HasSound) RunSound(effects, comp);

                    effects.LastActiveTick = Tick;
                    effects.Restart = false;

                    if (effects.Dirty || effects.Expired && particlesFinished && animationsFinished)
                    {
                        comp.ActiveEffects.RemoveAtFast(j);
                        effects.Clean();
                    }
                }

                if (comp.ActiveEffects.Count == 0)
                {
                    comp.AvActive = false;
                    AvComps.Remove(comp);
                }
            }
            AvComps.ApplyRemovals();
        }

        internal bool RunParticles(Effects effects)
        {
            var particles = effects.ParticleEffects;
            MyAPIGateway.Utilities.ShowNotification($"Running {particles.Count} particles", 16);
            for (int i = 0; i < particles.Count; i++)
            {
                var pEffect = particles[i];
                var def = pEffect.Definition;

                if (effects.Expired && pEffect.Particle != null)
                {
                    if (def.Loop)
                    {
                        pEffect.Particle.Stop(false);
                        pEffect.Particle = null;
                    }
                    continue;
                }

                if (effects.Restart || effects.LastActiveTick < Tick - 1)
                {
                    if (pEffect.Particle != null)
                        continue;

                    var matrix = MatrixD.Normalize(pEffect.Dummy.Matrix);
                    var pos = matrix.Translation;
                    matrix.Translation += def.Offset;
                    var renderId = pEffect.Parent.Render.GetRenderObjectID();
                    MyParticleEffect myParticle;
                    if (!MyParticlesManager.TryCreateParticleEffect(def.Name, ref matrix, ref pos, renderId, out myParticle))
                        continue;

                    if (def.Loop)
                    {
                        pEffect.Particle = myParticle;
                    }
                    continue;
                }

                if (def.Loop && !effects.Expired)
                {
                    if (pEffect.Particle == null)
                    {
                        Logs.WriteLine($"MyParticleEffect '{def.Name}' null in particle loop!");
                        continue;
                    }

                    var matrix = MatrixD.Normalize(pEffect.Dummy.Matrix);
                    matrix.Translation += def.Offset;
                    pEffect.Particle.WorldMatrix = matrix;
                }

            }
            return true;
        }

        internal bool RunAnimations(Effects effects)
        {
            var animations = effects.Animations;
            var finished = true;
            MyAPIGateway.Utilities.ShowNotification($"Running {animations.Count} animations", 16);
            for (int i = 0; i < animations.Count; i++)
            {
                var anim = animations[i];
                var subpart = anim.Subpart;

                if (subpart == null)
                {
                    Logs.WriteLine($"Subpart null in animation loop!");
                    continue;
                }
                var transform = anim.Definition.Transform;

                if (effects.Expired)
                {
                    if (!anim.Definition.HasWindup || anim.TransitionState <= 0)
                        continue;

                    anim.Starting = false;

                    finished = false;
                    anim.TransitionState--;
                    transform = Matrix.CreateFromAxisAngle(anim.Definition.Direction, anim.TransitionState * anim.Definition.WindupRadsFraction);
                }

                if (anim.Definition.HasWindup && effects.LastActiveTick < Tick - 1)
                {
                    anim.Starting = true;
                }

                if (anim.Starting)
                {
                    anim.TransitionState++;
                    if (anim.TransitionState >= anim.Definition.WindupTime - 1)
                        anim.Starting = false;

                    transform = Matrix.CreateFromAxisAngle(anim.Definition.Direction, anim.TransitionState * anim.Definition.WindupRadsFraction);
                }

                var lm = subpart.PositionComp.LocalMatrixRef;
                var trans = lm.Translation;
                //lm *= transform;
                Matrix.MultiplyRotation(ref lm, ref transform, out lm);
                lm.Translation = trans;
                subpart.PositionComp.SetLocalMatrix(ref lm);
            }

            return finished;

        }

        internal void RunSound(Effects effects, ToolComp comp)
        {
            var emitter = comp.SoundEmitter;
            if (emitter == null)
            {
                Logs.WriteLine("Sound emitter null!");
                return;
            }
            var sound = effects.SoundDef;

            if (effects.Expired)
            {
                if (emitter.IsPlaying)
                {
                    emitter.StopSound(true);
                    Logs.WriteLine("Stopping sound");
                }

                return;
            }

            if (effects.LastActiveTick < Tick - 1)
            {
                if (emitter.IsPlaying)
                {
                    emitter.StopSound(true);
                    Logs.WriteLine("Stopping sound");
                }

                emitter.PlaySound(sound.SoundPair);
                Logs.WriteLine("Playing sound");
            }

        }

    }
}

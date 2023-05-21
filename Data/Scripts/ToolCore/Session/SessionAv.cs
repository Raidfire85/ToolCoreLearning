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
using static ToolCore.ToolComp;

namespace ToolCore
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

                var avState = comp.State & def.EventFlags;
                if (avState == 0)
                {
                    //AvComps.Remove(comp);
                    //clean
                }

                for (int j = comp.ActiveEffects.Count - 1; j >= 0; j--)
                {
                    var effects = comp.ActiveEffects[j];

                    if (effects.HasParticles)
                        RunParticles(effects);

                    if (effects.HasAnimations)
                        RunAnimations(effects);
                    
                    if (effects.HasSound)
                        RunSound(effects, comp);



                    effects.LastActiveTick = Tick;
                    if (effects.Dirty)
                        comp.ActiveEffects.RemoveAtFast(j);
                }

            }
            AvComps.ApplyRemovals();
        }

        internal void RunParticles(Effects effects)
        {
            Logs.WriteLine("AAA");
            var particles = effects.ParticleEffects;
            for (int i = 0; i < particles.Count; i++)
            {
                var pEffect = particles[i];

                if (effects.Expired)
                {
                    pEffect.Particle?.Stop();
                    pEffect.Particle = null;
                    continue;
                }

                if (effects.LastActiveTick < Tick - 1)
                {
                    var matrix = MatrixD.Normalize(pEffect.Dummy.Matrix);
                    var pos = matrix.Translation;
                    matrix.Translation += pEffect.Offset;
                    var renderId = pEffect.Parent.Render.GetRenderObjectID();
                    MyParticleEffect myParticle;
                    if (!MyParticlesManager.TryCreateParticleEffect(pEffect.Name, ref matrix, ref pos, renderId, out myParticle))
                        continue;

                    if (pEffect.Loop)
                    {
                        pEffect.Particle = myParticle;
                    }
                    continue;
                }

                if (pEffect.Loop)
                {
                    if (pEffect.Particle == null)
                    {
                        Logs.WriteLine($"MyParticleEffect '{pEffect.Name}' null in particle loop!");
                        continue;
                    }

                    var matrix = MatrixD.Normalize(pEffect.Dummy.Matrix);
                    pEffect.Particle.WorldMatrix = matrix;
                }

            }
        }

        internal void RunAnimations(Effects effects)
        {
            var animations = effects.Animations;
            for (int i = 0; i < animations.Count; i++)
            {
                var anim = animations[i];
                var subpart = anim.Subpart;

                if (subpart == null)
                {
                    Logs.WriteLine($"Subpart null in animation loop!");
                    continue;
                }
                var transform = anim.Transform;

                if (effects.Expired)
                {
                    if (anim.TransitionState <= 0)
                        continue;

                    anim.TransitionState--;
                    transform *= anim.TransitionState / anim.WindupTime;
                }

                if (effects.LastActiveTick < Tick - 1)
                {
                    anim.Starting = true;
                }

                if (anim.Starting)
                {
                    anim.TransitionState++;
                    if (anim.TransitionState >= anim.WindupTime - 1)
                        anim.Starting = false;

                    transform *= anim.TransitionState / anim.WindupTime;
                }

                var lm = subpart.PositionComp.LocalMatrixRef;
                var trans = lm.Translation;
                lm *= transform;
                //lm.Translation = trans;
                subpart.PositionComp.SetLocalMatrix(ref lm);
            }

        }

        internal void RunSound(Effects effects, ToolComp comp)
        {
            var sound = effects.SoundDef;
            comp.SoundEmitter.PlaySound(sound.SoundPair);
        }

    }
}

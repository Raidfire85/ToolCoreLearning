using ProtoBuf;
using Sandbox.ModAPI;
using System;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.Collections.Generic;
using VRageMath;
using Sandbox.Game.Entities;

namespace ToolCore.Comp
{
    [ProtoContract]
    internal class ToolRepo
    {
        [ProtoMember(1)] public bool Activated;
        [ProtoMember(2)] public bool Draw;
        [ProtoMember(3)] public int Mode;

        internal void Sync(ToolComp comp)
        {
            Activated = comp.Activated;
            Draw = comp.Draw;
            Mode = (int)comp.Mode;
        }
    }
}

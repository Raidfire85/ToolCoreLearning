using ProtoBuf;

namespace ToolCore.Comp
{
    [ProtoContract]
    internal class ToolRepo
    {
        [ProtoMember(1)] public bool Activated;
        [ProtoMember(2)] public bool Draw;
        [ProtoMember(3)] public byte Mode;
        [ProtoMember(4)] public byte Action;

        internal void Sync(ToolComp comp)
        {
            Activated = comp.Activated;
            Draw = comp.Draw;
            Mode = (byte)comp.Mode;
            Action = (byte)comp.Action;
        }
    }
}

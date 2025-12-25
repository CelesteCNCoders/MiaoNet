using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetCommand
{
    public readonly ref struct Context
    {
        public MiaoNetContext MiaoNetContext { get; }

        public IReadOnlyList<string> Segments { get; }

        public Context(MiaoNetContext miaoNetContext, IReadOnlyList<string> segments)
        {
            MiaoNetContext = miaoNetContext;
            Segments = segments;
        }

        public void QueuePacket(IContextualPacket packet)
            => MiaoNetContext.QueuePacket(packet);

        public void TipMessage(string message)
            => MiaoNetContext.ChatComponent.TipMessage(message);
    }
}
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetCommand
{
    public readonly struct Context
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

        public void Request<TResponse>(PacketRequest<TResponse> packet, Action<TResponse> callback)
            where TResponse : PacketResponse
            => MiaoNetContext.Request(packet, callback);

        public void TipMessage(string message)
            => MiaoNetContext.ChatComponent.TipMessage(message);

        public void TipErrorMessage(string message)
            => MiaoNetContext.ChatComponent.TipErrorMessage(message);
    }
}
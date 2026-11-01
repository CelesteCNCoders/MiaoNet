namespace Celeste.Mod.CelesteNet.Client.Components;

/// <summary>
/// The original client ticks its components through this interface, at the tick rate
/// announced by the server.
/// </summary>
public interface ITickReceiver
{
    void Tick();
}

namespace MiaoNet.Server;

public class MoveResult
{
    public ScopeTuple From;
    public ScopeTuple To;

    public MoveResult(ScopeTuple from, ScopeTuple to)
    {
        this.From = from;
        this.To = to;
    }
}

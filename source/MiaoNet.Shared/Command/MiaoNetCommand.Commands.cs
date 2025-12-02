namespace MiaoNet.Shared;

partial class MiaoNetCommand
{
    public static readonly IReadOnlyList<MiaoNetCommand> Commands;

    static MiaoNetCommand()
    {
        Commands = [
            new CommandAnnounce()
        ];
    }
}
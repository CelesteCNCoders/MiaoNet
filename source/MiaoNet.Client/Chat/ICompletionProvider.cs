#nullable enable

namespace Celeste.Mod.MiaoNet.Chat;

// TODO matched parts?
// TODO using ReadOnlySpan<char>?
public interface ICompletionProvider
{
    public IEnumerable<Completion>? GetCompletions(string input);
}
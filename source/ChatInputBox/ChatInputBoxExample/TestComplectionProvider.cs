using Celeste.Mod.ChatInputBox;

namespace Celeste.Mod.ChatInputBoxExample;

public class TestCompletionProvider : ICompletionProvider
{
    public IEnumerable<Completion> GetCompletions(string input)
    {
        return ((List<string>)["some", "completions", "here"]).Select(s => new Completion(s, 0));
    }
}

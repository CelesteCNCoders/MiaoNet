using Aoore = System.ArgumentOutOfRangeException;

namespace MiaoNet.Shared;

public readonly struct EmoteData : IRefBinarySerializable<EmoteData>
{
    public const int DefaultFps = 7;

    public ushort Fps { get; }

    public bool Loop { get; }

    public EmoteAtlasCategory Category { get; }

    public string Prefix { get; }

    public IReadOnlyList<string> Frames { get; }

    public EmoteData(ushort fps, bool loop, EmoteAtlasCategory category, string prefix, IReadOnlyList<string> frames)
    {
        Aoore.ThrowIfEqual(frames.Count, 0);
        Aoore.ThrowIfGreaterThan(frames.Count, byte.MaxValue);
        foreach (var frame in frames)
            Aoore.ThrowIfGreaterThan(frame.Length, byte.MaxValue);

        Fps = fps;
        Loop = loop;
        Category = category;
        Prefix = prefix;
        Frames = frames;
    }

    public EmoteData(bool loop, EmoteAtlasCategory category, string prefix, string[] frames)
        : this(DefaultFps, loop, category, prefix, frames)
    {
    }

    #region Parsing

    // <category><fps?>:<prefix> <frame1> <frame2> ... <frameN> !
    public static bool TryParse(string text, out EmoteData emoteData)
    {
        text = text.Trim();

        if (text.Length == 0)
            goto Failed;

        ArraySegment<string> splitParts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        ReadOnlySpan<char> part1 = splitParts[0].AsSpan();
        char cateChar = part1[0];

        if (!TryParseCategory(cateChar, out var category))
            goto Failed;


        int nextColonIndex = part1.Slice(0).IndexOf(':');
        if (nextColonIndex == -1)
            goto Failed;

        ushort fps = DefaultFps;
        ReadOnlySpan<char> fpsCharSpan = part1[1..nextColonIndex];
        if (!(fpsCharSpan.IsEmpty || ushort.TryParse(fpsCharSpan, out fps)))
            goto Failed;

        ReadOnlySpan<char> prefix = part1[(nextColonIndex + 1)..];

        ArraySegment<string> frames = splitParts[1..];

        bool loop = true;
        if (frames.Count != 0)
        {
            if (frames[^1] == "!")
            {
                loop = false;
                frames = frames[..^1];
            }
            else
            {
                loop = true;
            }
        }

        IReadOnlyList<string> frameList = frames.Count == 0 ? [string.Empty] : frames.ToList();
        emoteData = new EmoteData(fps, loop, category, prefix.ToString(), frameList);
        return true;
    Failed:
        emoteData = default;
        return false;
    }

    private static bool TryParseCategory(char c, out EmoteAtlasCategory category)
    {
        switch (char.ToLowerInvariant(c))
        {
        case 'p':
            category = EmoteAtlasCategory.Portrait;
            return true;
        case 'i':
            category = EmoteAtlasCategory.Gui;
            return true;
        case 'g':
            category = EmoteAtlasCategory.Gameplay;
            return true;
        default:
            category = default;
            return false;
        }
    }

    #endregion

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Fps);
        writer.Write(Loop);
        writer.Write((byte)Category);
        writer.Write(Prefix);
        writer.Write((byte)Frames.Count);
        foreach (var frame in Frames)
            writer.Write(frame);
    }

    public static EmoteData Deserialize(ref RefBinaryReader reader)
    {
        ushort fps = reader.ReadUInt16();
        bool loop = reader.ReadBoolean();
        EmoteAtlasCategory category = (EmoteAtlasCategory)reader.ReadByte();
        string prefix = reader.ReadString();
        int framesCount = reader.ReadByte();
        string[] frames = new string[framesCount];
        for (int i = 0; i < framesCount; i++)
            frames[i] = reader.ReadString();

        return new(fps, loop, category, prefix, frames);
    }
}
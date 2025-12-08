using Aoore = System.ArgumentOutOfRangeException;

namespace MiaoNet.Shared;

public readonly struct EmoteData : IRefBinarySerializable<EmoteData>
{
    public const int DefaultFps = 7;

    public ushort Fps { get; }

    public bool Loop { get; }

    public EmoteAtlasCategory Category { get; }

    public string Prefix { get; }

    public string[] Frames { get; }

    public EmoteData(ushort fps, bool loop, EmoteAtlasCategory category, string prefix, string[] frames)
    {
        Aoore.ThrowIfEqual(frames.Length, 0);
        Aoore.ThrowIfGreaterThan(frames.Length, byte.MaxValue);
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

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Fps);
        writer.Write(Loop);
        writer.Write((byte)Category);
        writer.Write(Prefix);
        writer.Write((byte)Frames.Length);
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
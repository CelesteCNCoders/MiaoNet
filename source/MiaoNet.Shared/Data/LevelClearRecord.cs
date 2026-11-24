using System.Globalization;

namespace MiaoNet.Shared;

public sealed record LevelClearRecord : IRefBinarySerializable<LevelClearRecord>
{
    public const int MaxMapSIDLength = 256;
    public const int MaxMapNameLength = 160;
    public const int ShareCooldownSeconds = 10;

    public string ClearID { get; init; } = Guid.NewGuid().ToString("N");

    public string MapSID { get; init; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    public byte Mode { get; init; }

    /// <summary>
    /// Clear duration in ticks, or null when the player disabled the in-game speedrun clock.
    /// </summary>
    public long? TimeTicks { get; init; }

    public int Deaths { get; init; }

    public int Strawberries { get; init; }

    public int TotalStrawberries { get; init; }

    public bool FullClear { get; init; }

    public bool Golden { get; init; }

    public bool IsValid()
        => Guid.TryParseExact(ClearID, "N", out var clearID) && clearID != Guid.Empty
        && IsValidText(MapSID, MaxMapSIDLength)
        && IsValidText(MapName, MaxMapNameLength)
        && Mode <= 2
        && (TimeTicks is null || (TimeTicks >= 0 && TimeTicks <= TimeSpan.TicksPerDay * 365))
        && Deaths >= 0 && Deaths <= 10_000_000
        && Strawberries >= 0 && TotalStrawberries >= Strawberries && TotalStrawberries <= 100_000;

    private static bool IsValidText(string? text, int maxLength)
        => !string.IsNullOrWhiteSpace(text)
        && text.Length <= maxLength
        && !text.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029');

    public string? FormatTime()
    {
        if (TimeTicks is not long ticks)
            return null;

        var time = TimeSpan.FromTicks(ticks);
        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(ClearID);
        writer.Write(MapSID);
        writer.Write(MapName);
        writer.Write(Mode);
        if (TimeTicks.HasValue)
        {
            writer.Write(true);
            writer.Write(TimeTicks.Value);
        }
        else
        {
            writer.Write(false);
        }
        writer.Write(Deaths);
        writer.Write(Strawberries);
        writer.Write(TotalStrawberries);
        writer.Write(FullClear);
        writer.Write(Golden);
    }

    public static LevelClearRecord Deserialize(ref RefBinaryReader reader)
        => new()
        {
            ClearID = reader.ReadString(),
            MapSID = reader.ReadString(),
            MapName = reader.ReadString(),
            Mode = reader.ReadByte(),
            TimeTicks = reader.ReadBoolean() ? reader.ReadInt64() : null,
            Deaths = reader.ReadInt32(),
            Strawberries = reader.ReadInt32(),
            TotalStrawberries = reader.ReadInt32(),
            FullClear = reader.ReadBoolean(),
            Golden = reader.ReadBoolean()
        };
}

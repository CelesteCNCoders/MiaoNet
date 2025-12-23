using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

public static class SpriteIDTracker
{
    private readonly static ConditionalWeakTable<Sprite, string> table = new();

    public static void Load()
    {
        On.Monocle.SpriteBank.Create += SpriteBank_Create;
        On.Monocle.SpriteBank.CreateOn += SpriteBank_CreateOn;
    }

    public static void Unload()
    {
        On.Monocle.SpriteBank.Create -= SpriteBank_Create;
        On.Monocle.SpriteBank.CreateOn -= SpriteBank_CreateOn;
        table.Clear();
    }

    private static Sprite SpriteBank_Create(On.Monocle.SpriteBank.orig_Create orig, SpriteBank self, string id)
    {
        Sprite sprite = orig(self, id);
        table.Add(sprite, id);
        return sprite;
    }

    private static Sprite SpriteBank_CreateOn(
        On.Monocle.SpriteBank.orig_CreateOn orig,
        SpriteBank self, Sprite sprite, string id
    )
    {
        Sprite spriteCreatedOn = orig(self, sprite, id);
        table.Add(spriteCreatedOn, id);
        return spriteCreatedOn;
    }

    public static string? LookupID(Sprite sprite)
    {
        if (table.TryGetValue(sprite, out string? value))
            return value;
        return null;
    }
}

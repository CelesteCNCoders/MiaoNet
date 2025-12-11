using System.Collections.Immutable;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class BakedEmoteData
{
    private readonly int fps;
    private readonly bool loop;
    private readonly ImmutableArray<MTexture> textures;

    public BakedEmoteData(EmoteData emote)
    {
        fps = emote.Fps;
        loop = emote.Loop;
        textures = BakeTextures(emote);
    }

    public MTexture Sample(float time)
    {
        int index = (int)(time * fps);
        index = loop ? index % textures.Length : Math.Min(index, textures.Length - 1);
        return textures[index];
    }

    private ImmutableArray<MTexture> BakeTextures(EmoteData emote)
    {
        var builder = ImmutableArray.CreateBuilder<MTexture>(emote.Frames.Length);

        Atlas atlas = emote.Category switch
        {
            EmoteAtlasCategory.Gameplay => GFX.Game,
            EmoteAtlasCategory.Gui => GFX.Gui,
            EmoteAtlasCategory.Portrait => GFX.Portraits,
            _ => throw new ArgumentException("Atlas of category not found.", nameof(emote))
        };

        foreach (var frame in emote.Frames)
        {
            string fullFrameName = $"{emote.Prefix}{frame}";
            if (atlas.HasAtlasSubtextures(fullFrameName))
            {
                int i = 0;
                while (atlas.HasAtlasSubtexturesAt(fullFrameName, i))
                {
                    builder.Add(atlas.GetAtlasSubtexturesAt(fullFrameName, i));
                    i++;
                }
            }
            else if (atlas.Has(fullFrameName))
            {
                builder.Add(atlas[fullFrameName]);
            }
            else
            {
                builder.Add(atlas.DefaultFallback);
                Logger.Warn($"{nameof(MiaoNet)}/{nameof(EmoteData)}", $"Could not find frame {fullFrameName}.");
                break;
            }
        }

        if (builder.Count != 0)
            return builder.DrainToImmutable();
        else
            return [atlas.DefaultFallback];
    }
}

using System.Diagnostics.CodeAnalysis;
using MiaoNet.Shared;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

public sealed class EmoteWheel : MiaoNetEntity
{
    private const int EmotesCountPerPage = 8;

    private float popupScale = 1f;
    private float popupAlpha = 1f;
    private bool active;
    private Vector2 lastAim;
    private Vector2 aim;

    private int selected = -1;
    private int page = 0;
    private float previewTimer = 0f;
    private float previewPopupTimer = 0f;

    private Tween? tween;

    private readonly MTexture texBG, texIndicator, texLine;
    private List<string>? emotes;
    private readonly List<(BakedEmoteData?, string?)> previews;

    private Color TextSelectColorA = Calc.HexToColor("84FF54");
    private Color TextSelectColorB = Calc.HexToColor("FCFF59");

    public Entity Tracking { get; set; }

    public event Action<(EmoteData?, string?)>? OnEmote;

    public EmoteWheel(Entity tracking)
    {
        Tag = MiaoNetTag.Tag | TagsExt.SubHUD;
        Tracking = tracking;

        previews = new();

        texBG = GFX.Gui["miaonet/wheel_bg"];
        texIndicator = GFX.Gui["miaonet/wheel_indicator"];
        texLine = GFX.Gui["miaonet/wheel_line"];
    }

    public override void Update()
    {
        base.Update();
        lastAim = aim;
        // we need GamePadDeadZone.Circular so we have to call FNA APIs
        var vec = GamePad.GetState(PlayerIndex.One, GamePadDeadZone.Circular).ThumbSticks.Right;
        vec.Y = -vec.Y;
        if (vec.LengthSquared() <= 0.1f * 0.1f)
            vec = Vector2.Zero;
        aim = vec.SafeNormalize();
        float length = vec.LengthSquared();
        const float ActiveThreshold = 0.9f;
        const float InactiveThreshold = 0.2f;
        if (tween is null)
        {
            if (active)
            {
                if (length <= InactiveThreshold * InactiveThreshold)
                    OnShouldInactive();
            }
            else
            {
                if (length >= ActiveThreshold * ActiveThreshold)
                    OnShouldActive();
            }
        }

        if (active && aim != Vector2.Zero)
        {
            previewTimer += Engine.RawDeltaTime;
            previewPopupTimer += Engine.RawDeltaTime * 2f;
            if (previewPopupTimer >= 1f)
                previewPopupTimer = 1f;

            float lastAimRadians = (lastAim.Angle() + MathF.Tau) % (MathF.Tau);
            float aimRadians = (aim.Angle() + MathF.Tau) % (MathF.Tau);
            float radiansPerPreview = MathF.Tau / previews.Count;

            int curSelected = (int)(aimRadians / radiansPerPreview);
            curSelected = Math.Clamp(curSelected, 0, previews.Count);
            if (curSelected != selected)
            {
                float radiansDelta = aimRadians - lastAimRadians;
                if (radiansDelta < -MathF.PI)
                {
                    if (emotes!.Count - (page + 1) * EmotesCountPerPage > 0)
                    {
                        page++;
                        BuildPreviews();
                    }
                }
                else if (radiansDelta > MathF.PI)
                {
                    if (page != 0)
                    {
                        page--;
                        BuildPreviews();
                    }
                }

                previewTimer = 0f;
                previewPopupTimer = 0f;
                selected = curSelected;
            }

            var button = MiaoNetModule.Settings.EmoteWheelSendEmote;
            if (button.Pressed)
            {
                button.ConsumePress();
                string e = emotes![selected + EmotesCountPerPage * page];
                EmoteData? emoteData = EmoteData.Parse(e);
                OnEmote?.Invoke((emoteData, emoteData is null ? e : null));
            }
        }
    }

    private void BuildPreviews()
    {
        SafeGuard.Assert(emotes is not null);
        previews.Clear();
        foreach (var e in emotes.Skip(EmotesCountPerPage * page).Take(EmotesCountPerPage))
        {
            EmoteData? data = EmoteData.Parse(e);
            if (data is not null)
                previews.Add((new BakedEmoteData((EmoteData)data), null));
            else
                previews.Add((null, e));
        }
    }

    private void OnShouldActive()
    {
        active = true;
        emotes = MiaoNetModule.Settings.Emotes;
        BuildPreviews();
        popupScale = 0.8f;
        popupAlpha = 0f;
        tween = Tween.Set(this, Tween.TweenMode.Oneshot, 0.12f, Ease.CubeOut, t =>
            {
                popupScale = MathHelper.Lerp(0.75f, 1f, t.Eased);
                popupAlpha = t.Eased;
            },
            t => tween = null
        );
        tween.UseRawDeltaTime = true;
    }

    private void OnShouldInactive()
    {
        popupScale = 1f;
        popupAlpha = 1f;
        tween = Tween.Set(this, Tween.TweenMode.Oneshot, 0.12f, Ease.CubeOut, t =>
            {
                popupScale = MathHelper.Lerp(1f, 0.8f, t.Eased);
                popupAlpha = 1f - t.Eased;
            },
            t =>
            {
                active = false;
                previews.Clear();
                tween = null;
                page = 0;
            }
        );
        tween.UseRawDeltaTime = true;
    }

    public override void Render()
    {
        base.Render();
        if (!active)
            return;

        const float FixedSize = 96f;
        float scale = 1.5f * popupScale;

        var position = SceneAs<Level>().WorldToScreen(Tracking.Position);

        texBG.DrawCentered(position, Color.White * popupAlpha, scale);
        if (aim != Vector2.Zero)
            texIndicator.DrawCentered(position, Color.White * popupAlpha, scale, aim.Angle());

        int count = previews.Count;
        if (count == 0)
            return;

        float radiansPerPreview = MathF.Tau / count;
        float radius = 96f * scale;


        texLine.DrawCentered(position, Color.Red * popupAlpha, scale);
        for (int i = 1; i < count; i++)
        {
            texLine.DrawCentered(position, Color.Black * popupAlpha, scale, radiansPerPreview * i);
        }

        float curRadians = 0f;
        for (int i = 0; i < count; i++)
        {
            bool thisSelected = i == selected;
            var p = previews[i];

            float curCenter = curRadians + radiansPerPreview / 2f;
            Vector2 center = position + Calc.AngleToVector(curCenter, radius);

            float selectionAlpha = thisSelected ? 1f : 0.625f;
            float selectionPopupScale = thisSelected ? MathHelper.Lerp(0.8f, 1f, Ease.ElasticOut(previewPopupTimer)) : 1f;
            if (p.Item1 is not null)
            {
                var tex = p.Item1.Sample(thisSelected ? previewTimer : 0f);
                tex.DrawCentered(
                    center, Color.White * popupAlpha * selectionAlpha,
                    FixedSize / Math.Max(tex.Width, tex.Height) * selectionPopupScale
                );
            }
            else
            {
                Color color;
                if (!thisSelected)
                    color = Color.LightSlateGray;
                else if (Settings.Instance.DisableFlashes)
                    color = TextSelectColorA;
                else
                    color = Calc.BetweenInterval(previewTimer, 0.1f) ? TextSelectColorA : TextSelectColorB;

                var size = MiaoNetFont.Measure(p.Item2!);
                MiaoNetFont.DrawOutline(
                    p.Item2!, center,
                    Vector2.One / 2f, Vector2.One * (FixedSize / Math.Max(size.X, size.Y)) * selectionPopupScale,
                    color * popupAlpha * selectionAlpha
                );
            }

            curRadians += radiansPerPreview;
        }
    }
}

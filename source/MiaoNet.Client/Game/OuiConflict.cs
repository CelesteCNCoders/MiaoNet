using System.Collections;

namespace Celeste.Mod.MiaoNet;

internal sealed class OuiConflict : Oui
{
    private TextMenu? menu;
    private TextMenu Menu => menu ??= CreateMenu();

    public string? VersionMiaoNet;
    public string? VersionCelesteNet;

    private TextMenu CreateMenu()
    {
        string desc = PFormat.Format(
            Dialog.Clean("miaonet_oui_conflict_description"),
            VersionMiaoNet,
            VersionCelesteNet
        );
        var menu = new TextMenu();
        menu.BatchMode = true;
        menu.Add(new TextMenu.Header(Dialog.Get("miaonet_oui_conflict_title")));
        menu.Add(new TextMenu.SubHeader(desc));
        menu.Add(new TextMenu.Button(string.Empty) { Selectable = false });
        menu.Add(new TextMenu.Button(Dialog.Get("miaonet_oui_conflict_exit_game")).Pressed(new Action(Exit)));
        menu.BatchMode = false;
        return menu;
    }

    public override IEnumerator Enter(Oui from)
    {
        Overworld.Maddy.Hide();
        Scene.Add(Menu);
        TweenInMenu(Menu);
        yield return 0.25f;
        Focused = true;
        yield break;
    }

    public override IEnumerator Leave(Oui next)
    {
        TweenOutMenu(Menu);
        yield return 0.25f;
        Focused = false;
        yield break;
    }

    private static void TweenInMenu(TextMenu menu)
    {
        var from = menu.X - 100f;
        var to = menu.X;
        Tween.Set(menu, Tween.TweenMode.Oneshot, 0.25f, Ease.QuadInOut, t =>
        {
            menu.Alpha = MathHelper.Lerp(0f, 1f, t.Eased);
            menu.X = MathHelper.Lerp(from, to, t.Eased);
        });
    }

    private void TweenOutMenu(TextMenu menu)
    {
        var from = menu.X;
        var to = menu.X + 100f;
        Tween.Set(menu, Tween.TweenMode.Oneshot, 0.25f, Ease.QuadInOut, t =>
        {
            menu.Alpha = MathHelper.Lerp(1f, 0f, t.Eased);
            menu.X = MathHelper.Lerp(from, to, t.Eased);
        }).OnComplete = t =>
        {
            Scene.Remove(menu);
        };
    }

    private void Exit()
    {
        _ = new StarfieldWipe(Scene, false, delegate
        {
            Engine.Scene = new Scene();
            Engine.Instance.Exit();
        });
    }
}

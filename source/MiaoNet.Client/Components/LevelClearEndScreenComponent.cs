using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class LevelClearEndScreenComponent : MiaoNetComponent
{
    private string? pendingClearID;

    public LevelClearEndScreenComponent(MiaoNetContext context)
        : base(context)
    {
    }

    private static bool IsOnEndScreen()
        => Engine.Scene is AreaComplete || CollabUtils2Interop.HasInLevelEndScreen(Engine.Scene);

    private static (MiaoNetModuleSaveData? SaveData, LevelClearRecord? Record) GetShareableClear()
    {
        var saveData = MiaoNetModule.Instance._SaveData as MiaoNetModuleSaveData;
        var record = saveData?.LastClear;
        return record is not null && record.IsValid() ? (saveData, record) : (saveData, null);
    }

    public override void Update()
    {
        if (!IsOnEndScreen() || context.HasComponentFocus || MiaoNetModule.Settings.LiveMode)
            return;

        var (saveData, record) = GetShareableClear();
        if (record is null || saveData is null)
            return;

        var shareButton = MiaoNetModule.Settings.ShareClearButton;
        if (!shareButton.Pressed)
            return;

        shareButton.ConsumePress();
        if (saveData.LastSharedClearID == record.ClearID || pendingClearID == record.ClearID)
            return;

        if (LevelClearSharing.TryShareLastClear(context, out string? error))
        {
            pendingClearID = record.ClearID;
            Audio.Play(SFX.ui_main_button_select);
        }
        else if (error is not null)
        {
            context.StatusComponent.ShowStatusMessage(error);
        }
    }

    public override void Render()
    {
        if (!IsOnEndScreen() || context.HasComponentFocus || MiaoNetModule.Settings.LiveMode)
            return;

        var (saveData, record) = GetShareableClear();
        if (record is null || saveData is null)
            return;

        Vector2 pos = new(Engine.Width - 64f, 64f);
        bool isShared = saveData.LastSharedClearID == record.ClearID || pendingClearID == record.ClearID;
        if (isShared)
        {
            ActiveFont.DrawOutline(
                Dialog.Clean("miaonet_clear_endscreen_shared"),
                pos,
                new Vector2(1f, 0.5f),
                Vector2.One * 0.5f,
                Color.Gold,
                2f,
                Color.Black
            );
            return;
        }

        var binding = MiaoNetModule.Settings.ShareClearButton;
        string prompt = Dialog.Clean("miaonet_clear_endscreen_prompt");
        bool hasBinding = binding.Button is not null
            && (binding.Binding.Keyboard.Count > 0 || binding.Binding.Controller.Count > 0);
        if (hasBinding)
        {
            ButtonUI.Render(pos, prompt, binding.Button!, 0.5f, justifyX: 1f);
        }
        else
        {
            ActiveFont.DrawOutline(
                prompt,
                pos,
                new Vector2(1f, 0.5f),
                Vector2.One * 0.5f,
                Color.White * 0.85f,
                2f,
                Color.Black
            );
        }
    }

    public override void OnDisconnected()
    {
        pendingClearID = null;
    }
}

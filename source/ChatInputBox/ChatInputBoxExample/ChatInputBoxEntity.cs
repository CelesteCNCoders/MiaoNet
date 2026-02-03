using Celeste.Mod.ChatInputBox;
using Microsoft.Xna.Framework.Input;
namespace Celeste.Mod.ChatInputBoxExample;

[Tracked]
public sealed class ChatInputBoxEntity : Entity
{
    // this is an fna bug...
    private float lastMouseScroll = 0f;
    private float scroll = 0f;
    private bool active;
    private readonly ChatMessageListView msgListView;
    private readonly InputBox inputBox;

    public ChatInputBoxEntity()
    {
        Tag |= Tags.HUD | Tags.PauseUpdate | Tags.FrozenUpdate | Tags.TransitionUpdate | Tags.Global;
        Language lang = Dialog.Languages["schinese"];
        TextRenderer r = new(lang)
        {
            Scale = 2f / 3f
        };
        inputBox = new(r);
        msgListView = new(r);
        List<string> randomMsgs = [
            @"\uThis entire sentence is underlined.\r",
            @"\aThis text is red until reset.\r Normal text follows.",
            @"\bBlue mode activated!\r Back to plain.",
            @"\cGreen from here...\r ...and back to default.",
            @"\uUnderline starts here, \athen turns red while still underlined!\r Fully reset now.",
            @"\sStrikethrough begins.\r End of strike.",
            @"\aRed and \uunderlined together!\r All styles cleared.",
            @"\bBlue text.\r Plain again. No lingering effects!",
            @"This is normal, then \uunderline ON\r and now off.",
            @"\cGreen on.\r Off. \uUnderline on.\r Off again.",
            @"\sDeleted content.\r Current content is fine.",
            @"\aError: Something went wrong!\r Please try again.",
            @"\bInfo:\r System is running smoothly.",
            @"\cWarning:\r Disk space is low.",
            @"\uLoading...\r Complete!",
            @"\sOld API deprecated.\r Use the new one instead.",
            @"\aCritical failure!\r Restart required.",
            @"\bDebug log:\r Variable x = 42.",
            @"\cUser logged in.\r Session active.",
            @"\uProcessing request...\r Done.",
            @"\sThis feature will be removed soon.\r Migrate your code.",
            @"\aTimeout occurred.\r Retrying...",
            @"\bConnection established.\r Ready for commands.",
            @"\cPermission denied.\r Check your credentials.",
            @"\uSaving file...\r Saved successfully.",
            @"\sUndoing last action.\r Action undone.",
            @"\aDatabase connected.\r Query executed.",
            @"\bNew message received.\r Content: Hello!",
            @"\cCache cleared.\r Performance improved.",
            @"\uWelcome to the terminal.\r Enjoy your session.",
            @"\aAccess denied.\r Please authenticate first.",
            @"\bInitializing system...\r Initialization complete.",
            @"\cDetected 3 updates.\r Install now?",
            @"\sThis offer expires tomorrow.\r Terms apply.",
            @"\uConnecting to server...\r Connected securely.",
            @"\aFirewall blocked an intrusion attempt.\r System safe.",
            @"\bNew user registered.\r Welcome aboard!",
            @"\cBattery low: 5% remaining.\r Plug in soon.",
            @"\sScheduled maintenance at midnight.\r Downtime expected.",
            @"\uCompiling source code...\r Build succeeded.",
            @"\aMemory leak detected!\r Investigate immediately.",
            @"\bAuto-save enabled.\r Your work is protected.",
            @"\cGPS signal acquired.\r Navigation ready.",
            @"\sLegacy support ending Q4 2025.\r Plan migration.",
            @"\uGenerating report...\r Report saved as 'output.pdf'.",
            @"\aInvalid command syntax.\r Type 'help' for options.",
            @"\bBackground task running.\r Estimated time: 12s.",
            @"\cTwo-factor authentication required.\r Enter code.",
            @"\sYour trial ends in 2 days.\r Upgrade now?",
            @"\uScanning for viruses...\r No threats found.",
            @"\aDisk write error!\r Check storage device.",
            @"\bLanguage set to English (US).\r Changes applied.",
            @"\cWi-Fi disconnected.\r Reconnecting...",
            @"\sUnread messages: 7.\r View inbox?",
            @"\uRendering scene...\r Render complete in 3.2s.",
            @"\aRoot access granted.\r Use with caution.",
            @"\bClipboard updated.\r Content: 'https://example.com'",
            @"\cTemperature warning: CPU at 92°C.\r Cooling down.",
            @"\sAuto-delete enabled for temp files.\r Freeing space.",
            @"\uSyncing with cloud...\r Sync successful.",
            @"\aSuspicious login from new device.\r Review activity?",
            @"\bTheme changed to Dark Mode.\r Restart UI?",
            @"\cPrinter offline.\r Check connection and retry.",
            @"\sSession timeout in 60 seconds.\r Stay active?",
            @"\uExporting data...\r Export finished.",
            @"\aKernel panic avoided!\r System recovered gracefully.",
            @"\bMicrophone muted.\r Tap to unmute.",
            @"\cUpdate available: v2.4.1 → v2.5.0.\r Download now?",
            @"\sYou have 1 pending invitation.\r Accept or decline."
        ];
        foreach (var msg in randomMsgs)
        {
            msgListView.AddChatMessage(ChatText.Create(msg, Color.White));
        }

        lastMouseScroll = Mouse.GetState().ScrollWheelValue;
    }

    public void Activate()
    {
        active = true;
        inputBox.Activate();
        msgListView.Active = true;
        Scene.Paused = true;
    }

    public void Deactivate()
    {
        active = false;
        inputBox.Deactivate();
        msgListView.Scroll = 0f;
        msgListView.Active = false;
        scroll = 0f;
        Scene.Paused = false;
    }

    public override void Update()
    {
        msgListView.Update();
        if (active)
        {
            inputBox.Update();
            float scrollWheelValue = Mouse.GetState().ScrollWheelValue;
            scroll += scrollWheelValue - lastMouseScroll;
            lastMouseScroll = scrollWheelValue;

            scroll = msgListView.ClampScrollValue(scroll);
            msgListView.Scroll = Calc.Approach(
                msgListView.Scroll,
                scroll,
                Math.Max(Math.Abs(scroll - msgListView.Scroll), 24f) * 8f * Engine.DeltaTime
            );

            if (MInput.Keyboard.Pressed(Keys.Enter))
            {
                msgListView.AddChatMessage(ChatText.Create(inputBox.Text, Color.White));
                Deactivate();
                MInput.VirtualInputs.ForEach(i => (i as VirtualButton)?.ConsumePress());
            }
            else if (MInput.Keyboard.Pressed(Keys.Escape))
            {
                Deactivate();
                MInput.VirtualInputs.ForEach(i => (i as VirtualButton)?.ConsumePress());
            }
        }
        else if (MInput.Keyboard.Pressed(Keys.T))
        {
            Activate();
        }
    }

    public override void Render()
    {
        msgListView.Render();
        if (active)
            inputBox.Render();
    }
}

using Celeste.Mod.MiaoNet;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNetShootGame;

/// <summary>
/// 一个用 MiaoNet net-mod 做的对射小游戏: 左键朝鼠标方向开火, 被打中的人自己死, 击杀数打在聊天栏里。
/// <para>
/// 两个包, 两个去向: <see cref="PacketShoot"/> 定向发给同一房间的人(只有他们需要模拟这颗子弹),
/// <see cref="PacketKill"/> 广播给整张地图的人(大家在同一个击杀榜上)。
/// </para>
/// <para>
/// "打中"由被打的人判定而不是开枪的人: 只有他自己的客户端知道自己的碰撞盒此刻在哪。
/// </para>
/// </summary>
public sealed class MiaoNetShootGameModule : EverestModule
{
    private const string NetModName = "MiaoNetShootGame";

    /// <summary>协议版本。服务端按 (名字, 版本) 这一对匹配对端的声明。</summary>
    private const ushort ProtocolVersion = 1;

    /// <summary>两次开枪之间的最短间隔。</summary>
    private const float FireInterval = 0.0f;

    /// <summary>凶手 ID -> 击杀数。整张地图的人按收到的死亡通知各自维护一份。</summary>
    private static readonly Dictionary<int, int> kills = [];

    private static NetMod? netMod;

    private static float fireCooldown;

    public static MiaoNetShootGameModule Instance { get; private set; } = null!;

    public override void Load()
    {
        Instance = this;

        // 声明必须在连接之前给出: 握手把它带给服务端, 之后对端信的就是它。
        // 列表的顺序就是 localID, 这里 1 = 射击, 2 = 死亡通知。
        netMod = MiaoNetNetMods.Register(new NetMod(
            NetModName,
            ProtocolVersion,
            NetModPacketEntry.Create<PacketShoot>(PacketScope.Players, OnShoot),
            NetModPacketEntry.Create<PacketKill>(PacketScope.Map, OnKill)
        ));

        On.Celeste.Level.Update += Level_Update;
        On.Celeste.Level.Render += Level_Render;
    }

    public override void Unload()
    {
        On.Celeste.Level.Update -= Level_Update;
        On.Celeste.Level.Render -= Level_Render;

        // 注销会让当前会话断开: 对端信的还是旧声明, 这是设计如此(重复 Dispose 是空操作)。
        netMod?.Dispose();
        netMod = null;
    }

    #region 输入与绘制

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);

        fireCooldown -= Engine.DeltaTime;
        if (fireCooldown > 0f || self.Paused || !MInput.Mouse.PressedLeftButton)
            return;

        // 聊天框开着的时候, 左键是在点输入框
        if (MiaoNetModule.Instance.MiaoNetContextOptional?.ChatComponent.Active == true)
            return;

        Player? player = self.Tracker.GetEntity<Player>();
        if (player is null)
            return;

        fireCooldown = FireInterval;
        Shoot(self, player);
    }

    private static void Shoot(Level level, Player player)
    {
        // Player.Position 是脚底中心, 所以从碰撞盒中心出膛
        Rectangle bounds = player.Collider.Bounds;
        Vector2 origin = new(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);

        Vector2 direction = level.ScreenToWorld(MInput.Mouse.Position) - origin;
        if (direction.LengthSquared() < 1f)
            return;
        direction = Vector2.Normalize(direction);

        // 两种 scope 下自己都不在收件人里(广播不含发送者; 定向时名单里筛掉了自己),
        // 所以自己看的这颗要本地补上。
        Bullet.Spawn(level, origin, direction, ownerID: null);

        if (netMod is null || !MiaoNetNetMods.IsConnected)
            return;     // 没有会话: 这一枪只有自己看得到

        List<int> targets = Roommates();
        if (targets.Count > 0)
            netMod.SendTo(targets, new PacketShoot(origin, direction));
    }

    // 鼠标在游戏里是被藏起来的, 自己画个准星。这用的是屏幕坐标, 所以画在关卡渲染之后。
    private static void Level_Render(On.Celeste.Level.orig_Render orig, Level self)
    {
        orig(self);

        if (!MiaoNetNetMods.IsConnected || self.Paused)
            return;

        Vector2 mouse = MInput.Mouse.Position;
        MiaoNetContext.BeginRender();
        try
        {
            Draw.Line(mouse - Vector2.UnitX * 7f, mouse + Vector2.UnitX * 7f, Color.Red, 2f);
            Draw.Line(mouse - Vector2.UnitY * 7f, mouse + Vector2.UnitY * 7f, Color.Red, 2f);
        }
        finally
        {
            MiaoNetContext.EndRender();
        }
    }

    #endregion

    #region 收包

    private static void OnShoot(PacketEnvelope envelope, PacketShoot packet)
    {
        if (Engine.Scene is not Level level)
            return;     // 不在关卡里(比如在大地图上)就没有可以放子弹的地方

        // 凶手是谁只从 envelope 来: 身份是服务端转发时写上去的, payload 里没有也不该有。
        Bullet.Spawn(level, packet.Origin, packet.Direction, ownerID: envelope.SenderPlayerID);
    }

    private static void OnKill(PacketEnvelope envelope, PacketKill packet)
        // 发这个包的人就是被打死的那个
        => ApplyKill(victimID: envelope.SenderPlayerID, killerID: packet.KillerPlayerID);

    /// <summary>别人的子弹打中自己时由 <see cref="Bullet"/> 调用。</summary>
    internal static void OnLocalPlayerHit(int killerID, Vector2 direction)
    {
        if (Engine.Scene is not Level level)
            return;

        Player? player = level.Tracker.GetEntity<Player>();
        if (player is null)
            return;

        // 无视无敌帧: 冲刺途中也该被打死。也不记进存档的死亡数 —— 这只是个小游戏。
        if (player.Die(direction, evenIfInvincible: true, registerDeathInStats: false) is null)
            return;     // 已经死了或者正在过场, 不重复计

        // 死亡通知广播给整张地图。广播不含发送者, 所以被打死的这个人那一份在下面自己记。
        if (netMod is not null && MiaoNetNetMods.IsConnected)
            netMod.Send(new PacketKill(killerID));

        ApplyKill(victimID: SelfPlayerID, killerID);
    }

    #endregion

    #region 击杀榜

    private static void ApplyKill(int victimID, int killerID)
    {
        kills[killerID] = kills.GetValueOrDefault(killerID) + 1;
        Announce($"{NameOf(victimID)} 被 {NameOf(killerID)} 击杀 | {Tally()}");
    }

    private static string Tally()
        => string.Join("  ", kills
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => $"{NameOf(pair.Key)}: {pair.Value}"));

    private static string NameOf(int playerID)
        => State is { } state && state.TryGetPlayerOrSelf(playerID, out OnlinePlayer? player)
            ? player.Info.DisplayName
            : $"player #{playerID}";

    private static void Announce(string text)
        => MiaoNetModule.Instance.MiaoNetContextOptional?.ChatComponent.AddLocalChat(
            MiaoNetChatText.CreateAnnouncement($"[{NetModName}] {text}")
        );

    /// <summary>同一张房间里的其他人 —— 只有他们需要模拟我这一枪。</summary>
    private static List<int> Roommates()
    {
        List<int> roommates = [];
        if (State is not { } state)
            return roommates;

        // state.Players 不含自己, 所以服务端那条"定向 scope 不排除发送者"的规则不会把子弹弹回来。
        foreach (OnlinePlayer player in state.Players.Values)
        {
            if (player.Location == state.Self.Location)
                roommates.Add(player.ID);
        }

        return roommates;
    }

    private static ClientState? State => MiaoNetModule.Instance.MiaoNetContextOptional?.ClientState;

    private static int SelfPlayerID => State?.Self.ID ?? -1;

    #endregion
}

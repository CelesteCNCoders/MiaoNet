using System.Collections;
using System.Runtime.CompilerServices;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchClutterSystemAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte SwitchType = 0;
    private const byte CabinetType = 1;
    private const byte DoorType = 2;
    private const byte GroupType = 3;
    private const byte ContactType = 4;
    private const byte ClearGroupEvent = 1;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte BoolFlag = 1 << 2;

    private readonly record struct ContactInfo(
        int ID,
        ClutterBlock.Colors Color,
        Vector2 Position,
        bool Active
    );

    private sealed record ContactIdentity(
        int ID,
        ClutterBlock.Colors Color,
        Vector2 Position
    );

    private static readonly WatchClutterSystemAdapter instance = new();
    private static readonly Dictionary<WatchEntityKey, byte[]> remoteStates = new();
    private static readonly Dictionary<int, ContactInfo> localContacts = new();
    private static readonly Dictionary<int, ContactInfo> touchedThisFrame = new();
    private static readonly ConditionalWeakTable<ClutterBlock, ContactIdentity> contactIdentities = new();
    private static readonly HashSet<ClutterBlock.Colors> publishedClearGroups = new();
    private static readonly HashSet<ClutterBlock.Colors> remoteClearingGroups = new();
    private static string? localRoom;
    private static string? remoteRoom;
    private static Level? updatingLevel;
    private static ClutterBlock? updatingBlock;
    private static int weightDownDepth;

    public WatchEntityKind Kind => WatchEntityKind.ClutterSystem;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.ClutterBlockGenerator.Generate += ClutterBlockGenerator_Generate;
        On.Celeste.ClutterBlock.Update += ClutterBlock_Update;
        On.Celeste.ClutterBlock.WeightDown += ClutterBlock_WeightDown;
        On.Celeste.ClutterBlock.Absorb += ClutterBlock_Absorb;
        On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 += ClutterSwitch_ctor;
        On.Celeste.ClutterCabinet.ctor_EntityData_Vector2 += ClutterCabinet_ctor;
        On.Celeste.ClutterDoor.ctor += ClutterDoor_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ClutterDoor.ctor -= ClutterDoor_ctor;
        On.Celeste.ClutterCabinet.ctor_EntityData_Vector2 -= ClutterCabinet_ctor;
        On.Celeste.ClutterSwitch.ctor_EntityData_Vector2 -= ClutterSwitch_ctor;
        On.Celeste.ClutterBlock.Absorb -= ClutterBlock_Absorb;
        On.Celeste.ClutterBlock.WeightDown -= ClutterBlock_WeightDown;
        On.Celeste.ClutterBlock.Update -= ClutterBlock_Update;
        On.Celeste.ClutterBlockGenerator.Generate -= ClutterBlockGenerator_Generate;
        On.Celeste.Level.Update -= Level_Update;
        WatchEntityIDTable<ClutterSwitch>.Clear();
        WatchEntityIDTable<ClutterCabinet>.Clear();
        WatchEntityIDTable<ClutterDoor>.Clear();
        remoteStates.Clear();
        localContacts.Clear();
        touchedThisFrame.Clear();
        contactIdentities.Clear();
        publishedClearGroups.Clear();
        remoteClearingGroups.Clear();
        localRoom = null;
        remoteRoom = null;
        updatingLevel = null;
        updatingBlock = null;
        weightDownDepth = 0;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        EnsureLocalRoom(level.Session.Level);
        string room = level.Session.Level;
        foreach (ClutterSwitch entity in level.Entities.OfType<ClutterSwitch>())
        {
            if (WatchEntityIDTable<ClutterSwitch>.TryGet(entity, room, out int id))
                yield return Encode(
                    new WatchEntityKey(Kind, id, SwitchType), SwitchType,
                    Flags(entity, entity.pressed), (byte)entity.color,
                    EncodeAnimation(entity.sprite.CurrentAnimationID), entity.sprite.CurrentAnimationFrame,
                    entity.Position, entity.atY, entity.speedY
                );
        }
        foreach (ClutterCabinet entity in level.Entities.OfType<ClutterCabinet>())
        {
            if (WatchEntityIDTable<ClutterCabinet>.TryGet(entity, room, out int id))
                yield return Encode(
                    new WatchEntityKey(Kind, id, CabinetType), CabinetType,
                    Flags(entity, entity.Opened), 0,
                    EncodeAnimation(entity.sprite.CurrentAnimationID), entity.sprite.CurrentAnimationFrame,
                    entity.Position, 0f, 0f
                );
        }
        foreach (ClutterDoor entity in level.Entities.OfType<ClutterDoor>())
        {
            if (WatchEntityIDTable<ClutterDoor>.TryGet(entity, room, out int id))
                yield return Encode(
                    new WatchEntityKey(Kind, id, DoorType), DoorType,
                    Flags(entity, false), (byte)entity.Color,
                    EncodeAnimation(entity.sprite.CurrentAnimationID), entity.sprite.CurrentAnimationFrame,
                    entity.Position, entity.wiggler.Value, 0f
                );
        }

        foreach (ClutterBlock.Colors color in GroupColors())
        {
            bool present = level.Entities.OfType<ClutterBlock>().Any(block => block.BlockColor == color);
            yield return Encode(
                GroupKey(color), GroupType, present ? BoolFlag : (byte)0, (byte)color,
                0, 0, Vector2.Zero, 0f, 0f
            );
        }

        foreach (ContactInfo contact in localContacts.Values.OrderBy(contact => contact.ID))
        {
            yield return Encode(
                new WatchEntityKey(Kind, contact.ID, ContactType), ContactType,
                contact.Active ? BoolFlag : (byte)0,
                (byte)contact.Color, 0, 0, contact.Position, 0f, 0f
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        string room = level.Session.Level;
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteStates.Clear();
            remoteClearingGroups.Clear();
            remoteRoom = room;
        }

        HashSet<WatchEntityKey> packetKeys = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !packetKeys.Add(state.Key))
                return WatchEntityApplyResult.None;
            remoteStates[state.Key] = state.Payload.ToArray();
        }

        if (isCompleteState && GroupColors().Any(color => !remoteStates.ContainsKey(GroupKey(color))))
            return WatchEntityApplyResult.None;

        bool changed = ApplyRemote(level);
        bool requiresReload = false;
        if (isCompleteState)
        {
            Dictionary<WatchEntityKey, Entity> localTracked = EnumerateTracked(level)
                .ToDictionary(pair => pair.Key, pair => pair.Entity);
            foreach (WatchEntityKey key in localTracked.Keys)
            {
                if (!remoteStates.ContainsKey(key))
                    requiresReload = true;
            }
            foreach (WatchEntityKey key in remoteStates.Keys.Where(key => key.SubID <= DoorType))
            {
                if (!localTracked.ContainsKey(key))
                    requiresReload = true;
            }

            foreach (ClutterBlock.Colors color in GroupColors())
            {
                bool remotePresent = IsGroupPresent(color);
                bool localPresent = level.Entities.OfType<ClutterBlock>()
                    .Any(block => block.BlockColor == color);
                if (remotePresent && !localPresent && !remoteClearingGroups.Contains(color))
                    requiresReload = true;
            }
        }

        return (changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None)
            | (requiresReload ? WatchEntityApplyResult.RequiresRoomReload : WatchEntityApplyResult.None);
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.Key.Kind != Kind
            || entityEvent.Key.SubID != GroupType
            || entityEvent.EventID != ClearGroupEvent
            || entityEvent.Payload.Length != 0
            || entityEvent.Key.EntityID is < 0 or > 2)
            return;

        ClutterBlock.Colors color = (ClutterBlock.Colors)entityEvent.Key.EntityID;
        ClutterBlock[] blocks = level.Entities.OfType<ClutterBlock>()
            .Where(block => block.BlockColor == color)
            .ToArray();
        ClutterSwitch? clutterSwitch = level.Entities.OfType<ClutterSwitch>()
            .FirstOrDefault(entity => entity.color == color);
        remoteClearingGroups.Add(color);

        ClutterAbsorbEffect effect = new();
        level.Add(effect);
        if (clutterSwitch is not null)
            clutterSwitch.sprite.Play("break");
        DeactivateBlockBases(level, color);
        foreach (ClutterBlock block in blocks)
            block.Absorb(effect);
        if (clutterSwitch is not null)
        {
            Vector2 target = clutterSwitch.Position + new Vector2(clutterSwitch.Width / 2f, 0f);
            effect.Add(new Coroutine(ReplayAbsorbTail(level, effect, color, target)));
        }
    }

    private static IEnumerator ReplayAbsorbTail(
        Level level,
        ClutterAbsorbEffect effect,
        ClutterBlock.Colors color,
        Vector2 target
    )
    {
        yield return 1.5f;
        List<MTexture> images = GFX.Game.GetAtlasSubtextures($"objects/resortclutter/{color}_");
        for (int batch = 0; batch < 25; batch++)
        {
            if (!ReferenceEquals(effect.Scene, level))
                yield break;
            for (int i = 0; i < 5; i++)
            {
                Vector2 from = target + Calc.AngleToVector(Calc.Random.NextFloat(MathHelper.TwoPi), 320f);
                effect.FlyClutter(from, images[Calc.Random.Next(images.Count)], false, 0f);
            }
            level.Shake(0.3f);
            yield return 0.05f;
        }
        yield return 1.5f;
        if (!ReferenceEquals(effect.Scene, level))
            yield break;
        effect.CloseCabinets();
    }

    private static bool DeactivateBlockBases(Level level, ClutterBlock.Colors color)
    {
        bool changed = false;
        foreach (ClutterBlockBase blockBase in level.Entities.OfType<ClutterBlockBase>()
            .Where(blockBase => blockBase.BlockColor == color))
        {
            if (!blockBase.enabled && !blockBase.Collidable)
                continue;
            blockBase.Deactivate();
            changed = true;
        }
        return changed;
    }

    private static bool ApplyRemote(Level level)
    {
        bool changed = false;
        foreach ((Entity entity, WatchEntityKey key) in EnumerateTracked(level))
        {
            if (!remoteStates.TryGetValue(key, out byte[]? payload))
                continue;
            bool visible = (payload[1] & VisibleFlag) != 0;
            bool collidable = (payload[1] & CollidableFlag) != 0;
            bool boolValue = (payload[1] & BoolFlag) != 0;
            Vector2 position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 8),
                WatchEntityPayloadCodec.ReadSingle(payload, 12)
            );
            changed |= entity.Visible != visible || entity.Collidable != collidable || entity.Position != position;
            entity.Visible = visible;
            entity.Collidable = collidable;
            entity.Position = position;

            switch (entity)
            {
                case ClutterSwitch clutterSwitch:
                    if (!clutterSwitch.pressed && boolValue)
                        clutterSwitch.BePressed();
                    else if (clutterSwitch.pressed && !boolValue)
                        clutterSwitch.sprite.Play("idle");
                    changed |= clutterSwitch.pressed != boolValue;
                    clutterSwitch.pressed = boolValue;
                    clutterSwitch.atY = WatchEntityPayloadCodec.ReadSingle(payload, 16);
                    clutterSwitch.speedY = WatchEntityPayloadCodec.ReadSingle(payload, 20);
                    clutterSwitch.Position = position;
                    ApplyAnimation(clutterSwitch.sprite, payload[3], WatchEntityPayloadCodec.ReadUInt16(payload, 4));
                    break;
                case ClutterCabinet cabinet:
                    if (cabinet.Opened != boolValue)
                    {
                        if (boolValue)
                            cabinet.Open();
                        else
                            cabinet.Close();
                        changed = true;
                    }
                    ApplyAnimation(cabinet.sprite, payload[3], WatchEntityPayloadCodec.ReadUInt16(payload, 4));
                    break;
                case ClutterDoor door:
                    if (!visible && !collidable && (door.Visible || door.Collidable))
                    {
                        door.InstantUnlock();
                        changed = true;
                    }
                    door.wiggler.Value = WatchEntityPayloadCodec.ReadSingle(payload, 16);
                    ApplyAnimation(door.sprite, payload[3], WatchEntityPayloadCodec.ReadUInt16(payload, 4));
                    break;
            }
        }

        foreach (ClutterBlock.Colors color in GroupColors())
        {
            if (IsGroupPresent(color))
                continue;
            remoteClearingGroups.Remove(color);
            changed |= DeactivateBlockBases(level, color);
            foreach (ClutterBlock block in level.Entities.OfType<ClutterBlock>()
                .Where(block => block.BlockColor == color).ToArray())
            {
                block.RemoveSelf();
                changed = true;
            }
        }

        foreach ((WatchEntityKey key, byte[] payload) in remoteStates
            .Where(pair => pair.Key.SubID == ContactType
                && pair.Value[0] == ContactType
                && (pair.Value[1] & BoolFlag) != 0))
        {
            ClutterBlock.Colors color = (ClutterBlock.Colors)payload[2];
            foreach (ClutterBlock block in level.Entities.OfType<ClutterBlock>()
                .Where(block => block.BlockColor == color
                    && GetContactIdentity(block).ID == key.EntityID))
                block.WeightDown();
        }
        return changed;
    }

    private static IEnumerable<(Entity Entity, WatchEntityKey Key)> EnumerateTracked(Level level)
    {
        string room = level.Session.Level;
        foreach (ClutterSwitch entity in level.Entities.OfType<ClutterSwitch>())
            if (WatchEntityIDTable<ClutterSwitch>.TryGet(entity, room, out int id))
                yield return (entity, new WatchEntityKey(WatchEntityKind.ClutterSystem, id, SwitchType));
        foreach (ClutterCabinet entity in level.Entities.OfType<ClutterCabinet>())
            if (WatchEntityIDTable<ClutterCabinet>.TryGet(entity, room, out int id))
                yield return (entity, new WatchEntityKey(WatchEntityKind.ClutterSystem, id, CabinetType));
        foreach (ClutterDoor entity in level.Entities.OfType<ClutterDoor>())
            if (WatchEntityIDTable<ClutterDoor>.TryGet(entity, room, out int id))
                yield return (entity, new WatchEntityKey(WatchEntityKind.ClutterSystem, id, DoorType));
    }

    private static byte Flags(Entity entity, bool boolValue)
        => (byte)((entity.Visible ? VisibleFlag : 0)
            | (entity.Collidable ? CollidableFlag : 0)
            | (boolValue ? BoolFlag : 0));

    private static WatchEntityKey GroupKey(ClutterBlock.Colors color)
        => new(WatchEntityKind.ClutterSystem, (int)color, GroupType);

    private static bool IsGroupPresent(ClutterBlock.Colors color)
        => remoteStates.TryGetValue(GroupKey(color), out byte[]? payload)
            && (payload[1] & BoolFlag) != 0;

    private static IEnumerable<ClutterBlock.Colors> GroupColors()
    {
        yield return ClutterBlock.Colors.Red;
        yield return ClutterBlock.Colors.Green;
        yield return ClutterBlock.Colors.Yellow;
    }

    private static WatchEntityState Encode(
        WatchEntityKey key, byte type, byte flags, byte color, byte animation,
        int frame, Vector2 position, float value0, float value1
    )
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = type;
        payload[1] = flags;
        payload[2] = color;
        payload[3] = animation;
        WatchEntityPayloadCodec.WriteUInt16(payload, 4, (ushort)Math.Max(0, frame));
        WatchEntityPayloadCodec.WriteSingle(payload, 8, position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, value0);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, value1);
        return new WatchEntityState(key, payload);
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.ClutterSystem
            || payload.Length != PayloadSize
            || payload[0] > ContactType
            || state.Key.SubID != payload[0]
            || !HasFiniteValues(payload))
            return false;

        if (payload[0] <= DoorType)
            return (payload[1] & ~(VisibleFlag | CollidableFlag | BoolFlag)) == 0
                && payload[2] <= (byte)ClutterBlock.Colors.Lightning
                && payload[3] <= 4
                && payload[6] == 0 && payload[7] == 0;

        if (payload[0] == GroupType)
            return state.Key.EntityID is >= 0 and <= 2
                && payload[1] is 0 or BoolFlag
                && payload[2] == state.Key.EntityID
                && IsEmptyTail(payload, includePosition: true);

        return payload[1] is 0 or BoolFlag
            && payload[2] <= (byte)ClutterBlock.Colors.Yellow
            && payload[3] == 0
            && payload[4] == 0 && payload[5] == 0
            && payload[6] == 0 && payload[7] == 0
            && WatchEntityPayloadCodec.ReadSingle(payload, 16) == 0f
            && WatchEntityPayloadCodec.ReadSingle(payload, 20) == 0f;
    }

    private static bool HasFiniteValues(ReadOnlySpan<byte> payload)
        => float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 12))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 16))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 20));

    private static bool IsEmptyTail(ReadOnlySpan<byte> payload, bool includePosition)
        => payload[3] == 0
            && payload[4] == 0 && payload[5] == 0
            && payload[6] == 0 && payload[7] == 0
            && (!includePosition
                || (WatchEntityPayloadCodec.ReadSingle(payload, 8) == 0f
                    && WatchEntityPayloadCodec.ReadSingle(payload, 12) == 0f))
            && WatchEntityPayloadCodec.ReadSingle(payload, 16) == 0f
            && WatchEntityPayloadCodec.ReadSingle(payload, 20) == 0f;

    private static byte EncodeAnimation(string animation)
        => animation switch { "idle" => 1, "active" => 2, "open" => 3, "close" => 4, _ => 0 };

    private static void ApplyAnimation(Sprite sprite, byte animation, int frame)
    {
        string? id = animation switch { 1 => "idle", 2 => "active", 3 => "open", 4 => "close", _ => null };
        if (id is null)
            return;
        if (sprite.CurrentAnimationID != id)
            sprite.Play(id);
        sprite.SetAnimationFrame(frame);
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        EnsureLocalRoom(self.Session.Level);
        touchedThisFrame.Clear();
        updatingLevel = self;
        try
        {
            orig(self);
        }
        finally
        {
            updatingLevel = null;
            updatingBlock = null;
            weightDownDepth = 0;
        }

        foreach (int id in localContacts.Keys.ToArray())
            localContacts[id] = localContacts[id] with { Active = false };
        foreach ((int id, ContactInfo contact) in touchedThisFrame)
            localContacts[id] = contact;

        if (MiaoNetModule.IsWatching && StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            ApplyRemote(self);
    }

    private static void ClutterBlock_Update(On.Celeste.ClutterBlock.orig_Update orig, ClutterBlock self)
    {
        ClutterBlock? previous = updatingBlock;
        updatingBlock = self;
        try
        {
            orig(self);
        }
        finally
        {
            updatingBlock = previous;
        }
    }

    private static void ClutterBlock_WeightDown(
        On.Celeste.ClutterBlock.orig_WeightDown orig,
        ClutterBlock self
    )
    {
        if (weightDownDepth == 0
            && ReferenceEquals(updatingBlock, self)
            && ReferenceEquals(updatingLevel, self.Scene)
            && IsGroupColor(self.BlockColor)
            && !WatchEntitySyncRegistry.IsApplyingRemoteState)
        {
            ContactIdentity identity = GetContactIdentity(self);
            touchedThisFrame[identity.ID] = new ContactInfo(
                identity.ID,
                identity.Color,
                identity.Position,
                true
            );
        }

        weightDownDepth++;
        try
        {
            orig(self);
        }
        finally
        {
            weightDownDepth--;
        }
    }

    private static void ClutterBlock_Absorb(
        On.Celeste.ClutterBlock.orig_Absorb orig,
        ClutterBlock self,
        ClutterAbsorbEffect effect
    )
    {
        if (self.Scene is Level level
            && IsGroupColor(self.BlockColor)
            && !WatchEntitySyncRegistry.IsApplyingRemoteState)
        {
            EnsureLocalRoom(level.Session.Level);
            if (publishedClearGroups.Add(self.BlockColor))
            {
                WatchEntitySyncRegistry.PublishEvent(
                    level,
                    new WatchEntityEvent(GroupKey(self.BlockColor), ClearGroupEvent, [])
                );
            }
        }
        orig(self, effect);
    }

    private static void ClutterBlockGenerator_Generate(On.Celeste.ClutterBlockGenerator.orig_Generate orig)
    {
        localContacts.Clear();
        touchedThisFrame.Clear();
        contactIdentities.Clear();
        publishedClearGroups.Clear();
        orig();
        if (Engine.Scene is Level level)
            AssignContactIdentities(level);
    }

    private static bool IsGroupColor(ClutterBlock.Colors color)
        => color is ClutterBlock.Colors.Red
            or ClutterBlock.Colors.Green
            or ClutterBlock.Colors.Yellow;

    private static void EnsureLocalRoom(string room)
    {
        if (StringComparer.Ordinal.Equals(localRoom, room))
            return;
        localRoom = room;
        localContacts.Clear();
        touchedThisFrame.Clear();
        publishedClearGroups.Clear();
    }

    private static int StableContactID(Vector2 position, ClutterBlock.Colors color)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(position.X)) * 16777619;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(position.Y)) * 16777619;
            hash = (hash ^ (uint)color) * 16777619;
            return 0x50000000 | (int)(hash & 0x0fffffff);
        }
    }

    private static void AssignContactIdentities(Level level)
    {
        int index = 0;
        foreach (ClutterBlock block in level.Entities.OfType<ClutterBlock>()
            .Concat(level.Entities.ToAdd.OfType<ClutterBlock>())
            .Distinct()
            .OrderBy(block => block.BlockColor)
            .ThenBy(block => block.Position.Y)
            .ThenBy(block => block.Position.X))
        {
            int id = 0x50000000 | index++;
            contactIdentities.AddOrUpdate(
                block,
                new ContactIdentity(id, block.BlockColor, block.Position)
            );
        }
    }

    private static ContactIdentity GetContactIdentity(ClutterBlock block)
        => contactIdentities.GetValue(block, static candidate => new ContactIdentity(
            StableContactID(candidate.Position, candidate.BlockColor),
            candidate.BlockColor,
            candidate.Position
        ));

    private static void ClutterSwitch_ctor(
        On.Celeste.ClutterSwitch.orig_ctor_EntityData_Vector2 orig,
        ClutterSwitch self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<ClutterSwitch>.Set(self, data.Level.Name, data.ID);
    }

    private static void ClutterCabinet_ctor(
        On.Celeste.ClutterCabinet.orig_ctor_EntityData_Vector2 orig,
        ClutterCabinet self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<ClutterCabinet>.Set(self, data.Level.Name, data.ID);
    }

    private static void ClutterDoor_ctor(
        On.Celeste.ClutterDoor.orig_ctor orig,
        ClutterDoor self,
        EntityData data,
        Vector2 offset,
        Session session
    )
    {
        orig(self, data, offset, session);
        WatchEntityIDTable<ClutterDoor>.Set(self, data.Level.Name, data.ID);
    }
}

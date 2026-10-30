using Legend.Contracts.Proto;

namespace Legend.Playable.Input;

public enum AttackMode
{
    Peace,
    GoodEvil,
    Group,
    Guild,
    All
}

public enum ClickAction
{
    Walk,
    Run,
    Attack,
    ForceAttack,
    Pickup,
    Talk,
    Examine,
    Dig
}

public static class AttackModes
{
    public static AttackMode Next(AttackMode mode) => mode switch
    {
        AttackMode.Peace => AttackMode.GoodEvil,
        AttackMode.GoodEvil => AttackMode.Group,
        AttackMode.Group => AttackMode.Guild,
        AttackMode.Guild => AttackMode.All,
        _ => AttackMode.Peace
    };

    public static string Label(AttackMode mode) => mode switch
    {
        AttackMode.Peace => "和平",
        AttackMode.GoodEvil => "善恶",
        AttackMode.Group => "组队",
        AttackMode.Guild => "行会",
        _ => "全体"
    };

    /// <summary>
    /// Monsters are always legal targets. Players depend on mode.
    /// Good/evil has no karma flag yet, so it does not attack players unless forced.
    /// </summary>
    public static bool CanAttack(AttackMode mode, bool isMonster, bool sameParty, bool sameGuild, bool force)
    {
        if (isMonster)
            return true;
        if (force)
            return true;
        return mode switch
        {
            AttackMode.Peace => false,
            AttackMode.GoodEvil => false,
            AttackMode.Group => !sameParty,
            AttackMode.Guild => !sameGuild,
            AttackMode.All => true,
            _ => false
        };
    }
}

public static class ClassicPointer
{
    public static ClickAction Resolve(bool right, bool shift, bool ctrl, bool alt, string? kind, bool doubleClick)
    {
        if (alt)
            return ClickAction.Dig;
        if (doubleClick && kind == "loot")
            return ClickAction.Pickup;
        if (right && ctrl)
            return ClickAction.Examine;
        if (right)
            return kind == null ? ClickAction.Run : ClickAction.Examine;
        if (ctrl)
            return ClickAction.Run;
        if (shift)
            return ClickAction.ForceAttack;
        return kind switch
        {
            "loot" => ClickAction.Pickup,
            "npc" => ClickAction.Talk,
            "monster" or "player" => ClickAction.Attack,
            _ => ClickAction.Walk
        };
    }
}

public readonly record struct ChatRoute(ChatChannel Channel, string Content, string TargetName, string? Error);

public static class ClassicChat
{
    public static ChatRoute Parse(string raw, ChatChannel current, string whisperName)
    {
        string text = (raw ?? "").Trim();
        if (text.StartsWith("!!", StringComparison.Ordinal))
            return new ChatRoute(ChatChannel.ChatGuild, text[2..].Trim(), "", null);
        if (text.StartsWith("!~", StringComparison.Ordinal))
            return new ChatRoute(ChatChannel.ChatParty, text[2..].Trim(), "", null);
        if (text.StartsWith('!'))
            return new ChatRoute(ChatChannel.ChatWorld, text[1..].Trim(), "", null);
        if (text.StartsWith('@') || text.StartsWith('/'))
        {
            string body = text[1..].Trim();
            int sp = body.IndexOf(' ');
            if (sp <= 0)
                return new ChatRoute(ChatChannel.ChatWhisper, "", "", "私聊格式：@名字 内容");
            string name = body[..sp].Trim();
            string content = body[(sp + 1)..].Trim();
            if (content.Length == 0)
                return new ChatRoute(ChatChannel.ChatWhisper, "", name, "私聊内容是空的");
            return new ChatRoute(ChatChannel.ChatWhisper, content, name, null);
        }
        if (current == ChatChannel.ChatWhisper && string.IsNullOrWhiteSpace(whisperName))
            return new ChatRoute(current, text, "", "先填写私聊对象");
        return new ChatRoute(current, text, whisperName.Trim(), null);
    }
}

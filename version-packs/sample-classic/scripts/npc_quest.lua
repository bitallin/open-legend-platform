-- M3 NPC / shop / quest hooks (HardSandbox whitelist only)

function on_talk(ctx)
    legend_api.log_info(string.format(
        "on_talk player=%s npc=%s",
        ctx.player_name or "?",
        ctx.npc_id or "?"
    ))
end

function on_buy(ctx)
    legend_api.log_info(string.format(
        "on_buy player=%s item=%s x%d gold_left=%d",
        ctx.player_name or "?",
        ctx.item_id or "?",
        ctx.count or 0,
        ctx.gold or 0
    ))
end

function on_quest_accept(ctx)
    legend_api.log_info(string.format(
        "on_quest_accept player=%s quest=%s",
        ctx.player_name or "?",
        ctx.quest_id or "?"
    ))
    legend_api.send_welcome("任务接取", string.format("已接取：%s", ctx.quest_title or ctx.quest_id or "?"))
end

function on_quest_complete(ctx)
    legend_api.log_info(string.format(
        "on_quest_complete player=%s quest=%s gold=%d",
        ctx.player_name or "?",
        ctx.quest_id or "?",
        ctx.reward_gold or 0
    ))
    legend_api.send_welcome("任务完成", string.format("完成了 %s！", ctx.quest_title or ctx.quest_id or "?"))
end

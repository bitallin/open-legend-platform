-- M2 combat hooks (HardSandbox whitelist only)

function on_kill(ctx)
    legend_api.log_info(string.format(
        "on_kill killer=%s victim=%s map=%s",
        ctx.killer_name or "?",
        ctx.victim_name or "?",
        ctx.map_id or "?"
    ))
    legend_api.send_welcome("击杀奖励", string.format("击败了 %s！", ctx.victim_name or "怪物"))
end

function on_loot(ctx)
    legend_api.log_info(string.format(
        "on_loot picker=%s item=%s x%d",
        ctx.picker_name or "?",
        ctx.item_id or "?",
        ctx.count or 0
    ))
end

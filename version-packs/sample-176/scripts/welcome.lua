-- 原创 Lua 热更沙箱钩子示例（MoonSharp HardSandbox）
-- 仅调用白名单 legend_api，禁止任何 OS/IO 访问

function on_player_enter(player)
    local msg = string.format(
        "欢迎来到曙光平原，%s（职业：%s，坐标：%d,%d）！",
        player.name,
        player.class_id,
        player.x,
        player.y
    )
    legend_api.log_info("Lua hook on_player_enter triggered for " .. player.entity_id)
    legend_api.send_welcome("新手欢迎礼", msg)
    legend_api.grant_starter_item("item_wooden_blade", 1)
end

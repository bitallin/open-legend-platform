-- M5 Level-up & Consumable Lua Sandbox Hooks
function on_level_up(player, info)
    legend_api.log_info("on_level_up player=" .. player.name .. " " .. tostring(info.old_level) .. "->" .. tostring(info.new_level) .. " atk=" .. tostring(info.effective_atk))
end

function on_use_item(player, info)
    legend_api.log_info("on_use_item player=" .. player.name .. " item=" .. info.item_id .. " heal_hp=" .. tostring(info.heal_hp) .. " heal_mp=" .. tostring(info.heal_mp))
end

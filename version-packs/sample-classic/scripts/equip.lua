-- M4 Equipment & Enhancement Lua Sandbox Hooks
function on_equip(player, info)
    legend_api.log_info("on_equip player=" .. player.name .. " slot=" .. info.slot .. " item=" .. info.item_id .. " atk=" .. tostring(info.effective_atk))
end

function on_enhance(player, info)
    legend_api.log_info("on_enhance player=" .. player.name .. " slot=" .. info.slot .. " item=" .. info.item_id .. " level=+" .. tostring(info.enhance_level) .. " ok=" .. tostring(info.succeeded))
end

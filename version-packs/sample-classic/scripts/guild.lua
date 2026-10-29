-- M6 guild hooks (sandbox)
function on_guild_create(player_name, guild_name)
  legend_api.log_info("guild_create " .. tostring(player_name) .. " -> " .. tostring(guild_name))
end

function on_guild_join(player_name, guild_name)
  legend_api.log_info("guild_join " .. tostring(player_name) .. " -> " .. tostring(guild_name))
end

function on_guild_leave(player_name, guild_name)
  legend_api.log_info("guild_leave " .. tostring(player_name) .. " <- " .. tostring(guild_name))
end

function on_guild_warehouse_deposit(player_name, guild_name, item_id, count, used_slots)
  legend_api.log_info("guild_wh_deposit " .. tostring(player_name) .. " @" .. tostring(guild_name)
    .. " " .. tostring(item_id) .. "x" .. tostring(count) .. " slots=" .. tostring(used_slots))
end

function on_guild_warehouse_withdraw(player_name, guild_name, item_id, count, used_slots)
  legend_api.log_info("guild_wh_withdraw " .. tostring(player_name) .. " @" .. tostring(guild_name)
    .. " " .. tostring(item_id) .. "x" .. tostring(count) .. " slots=" .. tostring(used_slots))
end

function on_guild_warehouse_clear(player_name, guild_name, cleared_slots)
  legend_api.log_info("guild_wh_clear " .. tostring(player_name) .. " @" .. tostring(guild_name)
    .. " cleared=" .. tostring(cleared_slots))
end

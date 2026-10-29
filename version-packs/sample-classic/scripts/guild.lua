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

function on_guild_rank_change(actor_name, guild_name, target_name, old_rank, new_rank)
  legend_api.log_info("guild_rank_change " .. tostring(actor_name) .. " @" .. tostring(guild_name)
    .. " " .. tostring(target_name) .. " " .. tostring(old_rank) .. "->" .. tostring(new_rank))
end

function on_warehouse_audit(player_name, guild_name, action, item_id, count, audit_id)
  legend_api.log_info("guild_wh_audit " .. tostring(player_name) .. " @" .. tostring(guild_name)
    .. " " .. tostring(action) .. " " .. tostring(item_id) .. "x" .. tostring(count)
    .. " id=" .. tostring(audit_id))
end

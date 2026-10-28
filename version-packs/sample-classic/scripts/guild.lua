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

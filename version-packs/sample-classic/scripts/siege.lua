-- M10 siege / Sabuk Castle hooks (sandbox)
function on_siege_declare(castle_id, guild_id, guild_name, leader_name, cost_gold)
  legend_api.log_info("siege_declare castle=" .. tostring(castle_id) .. " guild=" .. tostring(guild_name) .. "(" .. tostring(guild_id) .. ") leader=" .. tostring(leader_name) .. " cost=" .. tostring(cost_gold))
end

function on_siege_capture(castle_id, guild_id, guild_name, capturer_name)
  legend_api.log_info("siege_capture castle=" .. tostring(castle_id) .. " owner_guild=" .. tostring(guild_name) .. "(" .. tostring(guild_id) .. ") capturer=" .. tostring(capturer_name))
end

function on_siege_end(castle_id, owner_guild_id, owner_guild_name)
  legend_api.log_info("siege_end castle=" .. tostring(castle_id) .. " winner=" .. tostring(owner_guild_name) .. "(" .. tostring(owner_guild_id) .. ")")
end

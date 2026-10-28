-- M9 buff hooks (sandbox)
function on_buff_apply(player_name, buff_id, duration_ms)
  legend_api.log_info("buff_apply " .. tostring(player_name) .. " buff=" .. tostring(buff_id) .. " dur=" .. tostring(duration_ms))
end

function on_buff_expire(player_name, buff_id, reason)
  legend_api.log_info("buff_expire " .. tostring(player_name) .. " buff=" .. tostring(buff_id) .. " reason=" .. tostring(reason))
end

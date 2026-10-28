-- M8 friend / whisper hooks (sandbox)
function on_friend_add(player_name, friend_name)
  legend_api.log_info("friend_add " .. tostring(player_name) .. " -> " .. tostring(friend_name))
end

function on_whisper(from_name, to_name, content)
  legend_api.log_info("whisper " .. tostring(from_name) .. " -> " .. tostring(to_name) .. " : " .. tostring(content))
end

-- M8 party hooks (sandbox)
function on_party_join(player_name, party_id, leader_name)
  legend_api.log_info("party_join " .. tostring(player_name) .. " party=" .. tostring(party_id) .. " leader=" .. tostring(leader_name))
end

function on_party_leave(player_name, party_id, reason)
  legend_api.log_info("party_leave " .. tostring(player_name) .. " party=" .. tostring(party_id) .. " reason=" .. tostring(reason))
end

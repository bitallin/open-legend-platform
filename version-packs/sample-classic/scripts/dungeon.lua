-- M12 dungeon hooks (sandbox)
function on_dungeon_enter(player_name, dungeon_id, instance_id)
  legend_api.log_info("dungeon_enter player=" .. tostring(player_name) .. " dungeon=" .. tostring(dungeon_id) .. " inst=" .. tostring(instance_id))
end

function on_dungeon_complete(player_name, dungeon_id, reward_gold)
  legend_api.log_info("dungeon_complete player=" .. tostring(player_name) .. " dungeon=" .. tostring(dungeon_id) .. " gold=" .. tostring(reward_gold))
end

function on_dungeon_leave(player_name, dungeon_id, completed)
  legend_api.log_info("dungeon_leave player=" .. tostring(player_name) .. " dungeon=" .. tostring(dungeon_id) .. " completed=" .. tostring(completed))
end

function on_dungeon_party_enter(leader_name, dungeon_id, instance_id, entered_count, denied_count)
  legend_api.log_info("dungeon_party_enter leader=" .. tostring(leader_name) .. " dungeon=" .. tostring(dungeon_id) .. " inst=" .. tostring(instance_id) .. " entered=" .. tostring(entered_count) .. " denied=" .. tostring(denied_count))
end

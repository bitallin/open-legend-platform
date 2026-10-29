-- M15 appearance hooks (mount / fashion)
function on_mount(player_name, character_id, mount_id, action, move_interval_ms)
  legend_api.log_info(string.format("[appearance] mount %s char=%s mount=%s action=%s interval=%s",
    tostring(player_name), tostring(character_id), tostring(mount_id), tostring(action), tostring(move_interval_ms)))
end

function on_fashion_equip(player_name, character_id, fashion_id, equipped, effective_atk)
  legend_api.log_info(string.format("[appearance] fashion %s char=%s fashion=%s equipped=%s atk=%s",
    tostring(player_name), tostring(character_id), tostring(fashion_id), tostring(equipped), tostring(effective_atk)))
end

-- M9 skill hooks (sandbox)
function on_learn_skill(player_name, skill_id, level)
  legend_api.log_info("learn_skill " .. tostring(player_name) .. " skill=" .. tostring(skill_id) .. " lv=" .. tostring(level))
end

function on_skill_level_up(player_name, skill_id, old_level, new_level)
  legend_api.log_info("skill_level_up " .. tostring(player_name) .. " skill=" .. tostring(skill_id) .. " " .. tostring(old_level) .. "->" .. tostring(new_level))
end

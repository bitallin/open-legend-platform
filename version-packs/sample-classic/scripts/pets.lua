-- M16 pet hooks
function on_pet_summon(player_name, character_id, pet_id, pet_entity_id)
  legend_api.log_info(string.format("[pet] summon %s char=%s pet=%s entity=%s",
    tostring(player_name), tostring(character_id), tostring(pet_id), tostring(pet_entity_id)))
end

function on_pet_dismiss(player_name, character_id, pet_id, reason)
  legend_api.log_info(string.format("[pet] dismiss %s char=%s pet=%s reason=%s",
    tostring(player_name), tostring(character_id), tostring(pet_id), tostring(reason)))
end

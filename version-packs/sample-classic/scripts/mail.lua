-- M11 mail hooks (sandbox)
function on_mail_send(to_name, subject, gold, from_name)
  log("mail_send", string.format("to=%s subject=%s gold=%d from=%s", tostring(to_name), tostring(subject), tonumber(gold) or 0, tostring(from_name)))
end

function on_mail_claim(player_name, mail_id, gold)
  log("mail_claim", string.format("player=%s mail=%s gold=%d", tostring(player_name), tostring(mail_id), tonumber(gold) or 0))
end

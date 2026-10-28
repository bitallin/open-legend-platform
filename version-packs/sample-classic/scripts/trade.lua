-- M7 stall / trade hooks (sandbox)
function on_stall_list(seller_name, item_id, count, total_price)
  legend_api.log_info("stall_list " .. tostring(seller_name) .. " item=" .. tostring(item_id) .. " x" .. tostring(count) .. " price=" .. tostring(total_price))
end

function on_stall_buy(buyer_name, seller_name, item_id, count, total_price)
  legend_api.log_info("stall_buy " .. tostring(buyer_name) .. " <- " .. tostring(seller_name) .. " item=" .. tostring(item_id) .. " x" .. tostring(count) .. " price=" .. tostring(total_price))
end

function on_stall_cancel(seller_name, item_id, count)
  legend_api.log_info("stall_cancel " .. tostring(seller_name) .. " item=" .. tostring(item_id) .. " x" .. tostring(count))
end

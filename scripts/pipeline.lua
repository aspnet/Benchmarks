local pipelineDepth = 1
local counter = 0
local maxRequests = -1
local method = "GET"

function init(args)

   if args[1] ~= nil then
      pipelineDepth = tonumber(args[1])
   end

   if args[2] ~= nil then
      method = args[2]
   end

   local r = {}
   for i = 1, pipelineDepth, 1 do
      r[i] = wrk.format(method)
   end

   print("Pipeline depth: " .. pipelineDepth)

   if args[3] ~= nil then
      maxRequests = tonumber(args[3])
      print("Max requests: " .. maxRequests)
   end

   req = table.concat(r)
end

function request()
   return req
end

function response()
   if counter == maxRequests then
     wrk.thread:stop()
   end
   counter = counter + 1
end

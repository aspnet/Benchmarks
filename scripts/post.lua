local pipelineDepth = 1

function init(args)
   wrk.method = "POST"
   wrk.body   = "foo=bar&baz=quux"
   wrk.headers["Content-Type"] = "application/x-www-form-urlencoded"

   if args[1] ~= nil then
      pipelineDepth = args[1]
   end

   local r = {}
   for i = 1, pipelineDepth, 1 do
      r[i] = wrk.format(nil)
   end

   print("Pipeline depth: " .. pipelineDepth)

   req = table.concat(r)
end

function request()
   return req
end

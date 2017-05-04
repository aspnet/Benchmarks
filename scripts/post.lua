local pipelineDepth = 1

function readData(filename)
   local f = io.open(filename, "rb")
   local data = f:read("*a")
   f:close()
   return data
end

function init(args)
   wrk.method = "POST"
   wrk.body = readData("scripts/data.txt")
   wrk.headers["Content-Type"] = "text/plain"

   if args[1] ~= nil then
      pipelineDepth = args[1]
   end

   local r = {}
   for i = 1, pipelineDepth, 1 do
      r[i] = wrk.format()
   end

   print("Pipeline depth: " .. pipelineDepth)

   req = table.concat(r)
end

function request()
   return req
end

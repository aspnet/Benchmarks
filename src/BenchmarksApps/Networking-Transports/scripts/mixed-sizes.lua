-- Cycles through response sizes so one connection sees a mix of payloads.
-- Optional argument: comma-separated byte sizes, e.g. --load.variables.scriptArguments "1024,65536".
local sizes = { 1024, 4096, 8192, 16384, 65536, 262144 }
local counter = 0

init = function(args)
    if args[1] ~= nil then
        sizes = {}
        for size in string.gmatch(args[1], "%d+") do
            table.insert(sizes, tonumber(size))
        end
    end
end

request = function()
    counter = counter + 1
    return wrk.format("GET", "/" .. sizes[(counter % #sizes) + 1])
end

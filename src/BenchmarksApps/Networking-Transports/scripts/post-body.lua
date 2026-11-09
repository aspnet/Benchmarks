-- Sends every request as a POST with a fixed-size body; the response size comes from the URL path (/{bytes}).
-- Argument: request body size in bytes, e.g. --load.variables.scriptArguments "8192". Default 1024.
init = function(args)
    local size = tonumber(args[1]) or 1024
    wrk.method = "POST"
    wrk.body = string.rep("x", size)
    wrk.headers["Content-Type"] = "application/octet-stream"
end

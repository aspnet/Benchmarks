-- sends an invalid HTTP header (with space in the name)

local host = "localhost"

init = function(args)
  print("wrk ARGS:", #arguments, arguments[1])
  if #args > 0 then
    host = args[1]
  end
end

-- before each request https://github.com/wg/wrk/blob/a211dd5a7050b1f9e8a9870b95513060e72ac4a0/SCRIPTING#L42
request = function()
  return "GET /hello-world HTTP/1.1\r\n" ..
         "Host: " .. host .. "\r\n" ..
         "Invalid Header: value\r\n" ..
         "\r\n"
end
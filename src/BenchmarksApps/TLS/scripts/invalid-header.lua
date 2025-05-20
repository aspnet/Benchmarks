-- sends an invalid HTTP header (with space in the name)
request = function() -- before each request https://github.com/wg/wrk/blob/a211dd5a7050b1f9e8a9870b95513060e72ac4a0/SCRIPTING#L42
  return "GET /hello-world HTTP/1.1\r\n" ..
         "Host: " .. args[1] .. "\r\n" ..
         "Invalid Header: value\r\n" ..
         "\r\n"
end
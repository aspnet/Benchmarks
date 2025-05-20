request = function()
  return "GET /hello-world HTTP/1.1\r\n" ..
         "Invalid Header: value\r\n" ..
         "\r\n"
end
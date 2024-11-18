-- script firstly tries to authenticate against `/auth` endpoint
-- then retrieves auth token, and uses for subsequent requests changing to `/validateToken` endpoint

antiforgeryTokenHeaderName = "XSRF-TOKEN"

token = nil
path  = "/auth"
httpMethod = "GET"

request = function() -- before each request https://github.com/wg/wrk/blob/a211dd5a7050b1f9e8a9870b95513060e72ac4a0/SCRIPTING#L42
   return wrk.format(httpMethod, path)
end

response = function(status, headers, body) -- after each response https://github.com/wg/wrk/blob/a211dd5a7050b1f9e8a9870b95513060e72ac4a0/SCRIPTING#L43
   if not token and status == 200 then
      path  = "/validateToken"
      httpMethod = "POST"

      -- should parse "Set-Cookie: XSRF-TOKEN=<token>; path=/" header
      headerValue = headers["Set-Cookie"]
      nameWithValue = string.match(headerValue, "(.-);")
      token = string.sub(nameWithValue, string.len(antiforgeryTokenHeaderName) + 2, string.len(nameWithValue) - string.len(antiforgeryTokenHeaderName) + 1)
      wrk.headers[antiforgeryTokenHeaderName] = token
   end
end
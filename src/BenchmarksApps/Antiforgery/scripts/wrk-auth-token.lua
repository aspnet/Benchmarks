-- script firstly tries to authenticate against `/auth` endpoint
-- then retrieves auth token, and uses for subsequent requests changing to `/validateToken` endpoint

tokenRetrieved = false

path  = "/auth"
httpMethod = "GET"

request = function() -- before each request https://github.com/wg/wrk/blob/a211dd5a7050b1f9e8a9870b95513060e72ac4a0/SCRIPTING#L42
   return wrk.format(httpMethod, path)
end

response = function(status, headers, body) -- after each response https://github.com/wg/wrk/blob/a211dd5a7050b1f9e8a9870b95513060e72ac4a0/SCRIPTING#L43
   if not tokenRetrieved and status == 200 then
      tokenRetrieved = true
      path  = "/validateToken"
      httpMethod = "POST"

      -- should parse antiforgery token
      token = headers["XSRF-TOKEN"]
      wrk.headers["XSRF-TOKEN"] = token

      -- should parse cookie header
      -- `set-cookie: .AspNetCore.Antiforgery.<unique-sequence>=<cookie_header>; path=/; samesite=strict; httponly`
      headerValue = headers["Set-Cookie"]
      cookie = string.match(headerValue, "(.-);")
      wrk.headers["Cookie"] = cookie
   end
end
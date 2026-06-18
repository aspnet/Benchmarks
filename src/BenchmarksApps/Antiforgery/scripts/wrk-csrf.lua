-- The header value is taken from the first script argument (wrk ... -- <value>),
-- e.g. "same-origin" (accepted) or "cross-site" (rejected). Defaults to same-origin.

wrk.method = "POST"

local secFetchSite = "same-origin"
if arg and arg[1] then
   secFetchSite = arg[1]
end

wrk.headers["Sec-Fetch-Site"] = secFetchSite

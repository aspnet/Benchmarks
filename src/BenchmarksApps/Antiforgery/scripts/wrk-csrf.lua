-- POSTs to /csrf to exercise the auto-injected cross-origin (Sec-Fetch) CSRF protection.
-- The Sec-Fetch-Site header is supplied per scenario via the wrk `customHeaders` variable
-- ("same-origin" => accepted/200, "cross-site" => rejected/400). wrk delivers script `--`
-- arguments only to init(args), so the header is sent through wrk's native --header instead
-- to guarantee the value reliably reaches the server.

wrk.method = "POST"

-- POSTs a form to /csrf to exercise the auto-injected cross-origin (Sec-Fetch) CSRF protection.
-- The endpoint binds [FromForm], so the request carries a urlencoded body; reading the form is what
-- triggers the (post-#67082 lazy) CSRF verdict. The Sec-Fetch-Site header is supplied per scenario
-- via the wrk `customHeaders` variable ("same-origin" => accepted/200, "cross-site" => rejected/400)
-- so it reliably reaches the server through wrk's native --header.

wrk.method = "POST"
wrk.headers["Content-Type"] = "application/x-www-form-urlencoded"
wrk.body = "name=benchmark"

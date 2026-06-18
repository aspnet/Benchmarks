-- POSTs a form to /csrf to exercise the auto-injected cross-origin (Sec-Fetch) CSRF protection.

wrk.method = "POST"
wrk.headers["Content-Type"] = "application/x-www-form-urlencoded"
wrk.body = "name=benchmark"

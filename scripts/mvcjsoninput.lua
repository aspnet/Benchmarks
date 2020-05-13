local payloadItem = [[{
  "attributes": {
    "created": "2019-04-23T00:45:50+00:00",
    "enabled": true,
    "expires": "2021-04-23T00:45:34+00:00",
    "notBefore": "2019-04-23T00:45:46+00:00",
    "recoveryLevel": "Purgeable",
    "updated": "2019-04-23T00:45:50+00:00"
  },
  "contentType": "test",
  "id": "https://example.com/some-resource/id",
  "managed": true,
  "tags": [ "tag1", "tag2" ]
}]]

function init(args)
    local payloadItems = 8
    if (#args ~= 0)
    then
        payloadItems = tonumber(args[1]) / 350 -- ~350 bytes per payloadItem
    end

    local data = "["
    -- Note that this produces 4kb data. We're leaving the misnamed scenario as is to avoid loosing historical context
    for i = 1, payloadItems, 1 do
        if (i ~= 1)
        then
            data = data .. ","
        end

        data = data .. payloadItem
    end

    data = data .. "]"

   wrk.method = "POST"
   wrk.body = data
   wrk.headers["Content-Type"] = "application/json"

   req = wrk.format()
end

function request()
    return req
end

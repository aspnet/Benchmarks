// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;

namespace Proxy
{

    public class HttpClientPool
    {
        private readonly HttpClient[] _clients;
        private readonly int[] _users;

        public HttpClientPool(int size)
        {
            _clients = new HttpClient[size];
            for (var i = 0; i < size; i++)
            {
                _clients[i] = new HttpClient();
            }

            _users = new int[size];
        }

        public Uri BaseAddress
        {
            get
            {
                return _clients[0].BaseAddress;
            }
            set
            {
                foreach (var client in _clients)
                {
                    client.BaseAddress = value;
                }
            }
        }

        public HttpClient GetInstance()
        {
            var leastUsed = LeastUsed();
            Interlocked.Increment(ref _users[leastUsed]);
            return _clients[leastUsed];
        }

        public void ReturnInstance(HttpClient httpClient)
        {
            for (var i = 0; i < _clients.Length; i++)
            {
                if (_clients[i] == httpClient)
                {
                    Interlocked.Decrement(ref _users[i]);
                }
            }
        }

        private int LeastUsed()
        {
            var leastUsedIndex = 0;
            var leastUsedCount = _users[0];

            for (var i = 1; i < _clients.Length; i++)
            {
                var count = _users[i];
                if (count < leastUsedCount)
                {
                    leastUsedIndex = i;
                    leastUsedCount = count;
                }
            }

            return leastUsedIndex;
        }

        public override string ToString()
        {
            return String.Join(" ", _users);
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Repository
{
    public class InMemoryRepository<T> : IRepository<T> where T: IIdentifiable
    {
        private readonly object _lock = new object();
        private readonly List<T> _items = new List<T>();
        private int _nextId = 1;

        public T Add(T item)
        {
            if (item.Id != 0)
            {
                throw new ArgumentException("item.Id must be 0.");
            }

            lock (_lock)
            {
                var id = _nextId;
                _nextId++;
                item.Id = id;
                _items.Add(item);
                return item;
            }
        }

        public T Find(int id)
        {
            lock (_lock)
            {
                var items = _items.Where(job => job.Id == id);
                Debug.Assert(items.Count() == 0 || items.Count() == 1);
                return items.FirstOrDefault();
            }
        }

        public IEnumerable<T> GetAll()
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }

        public T Remove(int id)
        {
            lock (_lock)
            {
                var item = Find(id);
                if (item == null)
                {
                    throw new ArgumentException($"Could not find item with Id '{id}'.");
                }
                else
                {
                    _items.Remove(item);
                    return item;
                }
            }
        }

        public void Update(T item)
        {
            lock (_lock)
            {
                var oldItem = Find(item.Id);
                _items[_items.IndexOf(oldItem)] = item;
            }
        }
    }
}

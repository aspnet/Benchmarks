// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Repository
{
    public interface IRepository<T> where T: IIdentifiable
    {
        T Add(T item);
        IEnumerable<T> GetAll();
        T Find(int id);
        T Remove(int id);
        void Update(T item);

    }
}

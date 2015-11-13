// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Linq;

namespace Benchmarks.Data
{
    public class ApplicationDbSeeder
    {
        private object _locker = new object();
        private bool _seeded = false;

        public void Seed(ApplicationDbContext db)
        {
            if (!_seeded)
            {
                lock (_locker)
                {
                    if (!_seeded)
                    {
                        var count = db.World.Count();

                        if (count == 0)
                        {
                            var random = new Random();
                            for (int i = 0; i < 10000; i++)
                            {
                                db.World.Add(new World { RandomNumber = random.Next(1, 10001) });
                            }
                            db.SaveChanges();
                        }

                        _seeded = true;
                    }
                }
            }
        }
    }
}
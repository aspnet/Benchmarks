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

        public bool Seed(ApplicationDbContext db)
        {
            if (!_seeded)
            {
                lock (_locker)
                {
                    if (!_seeded)
                    {
                        try
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
                                Console.WriteLine("Database successfully seeded!");
                            }
                            else
                            {
                                Console.WriteLine("Database already seeded!");
                            }

                            _seeded = true;
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("Error trying to seed the database. Have you run 'dnx ef database update'?");
                            Console.Error.WriteLine(ex);

                            return false;
                        }
                    }
                }
            }

            Console.WriteLine("Database already seeded!");
            return true;
        }
    }
}
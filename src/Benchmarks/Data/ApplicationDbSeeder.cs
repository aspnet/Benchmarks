// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Linq;

namespace Benchmarks.Data
{
    public class ApplicationDbSeeder
    {
        private readonly object _locker = new object();
        private bool _seeded;

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
                            var world = db.World.Count();
                            var fortune = db.Fortune.Count();

                            if (world == 0 || fortune == 0)
                            {
                                if (world == 0)
                                {
                                    var random = new Random();
                                    for (int i = 0; i < 10000; i++)
                                    {
                                        db.World.Add(new World { RandomNumber = random.Next(1, 10001) });
                                    }
                                    db.SaveChanges();
                                }

                                if (fortune == 0)
                                {
                                    db.Fortune.Add(new Fortune { Message = "fortune: No such file or directory" });
                                    db.Fortune.Add(new Fortune { Message = "A computer scientist is someone who fixes things that aren't broken." });
                                    db.Fortune.Add(new Fortune { Message = "After enough decimal places, nobody gives a damn." });
                                    db.Fortune.Add(new Fortune { Message = "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1" });
                                    db.Fortune.Add(new Fortune { Message = "A computer program does what you tell it to do, not what you want it to do." });
                                    db.Fortune.Add(new Fortune { Message = "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen" });
                                    db.Fortune.Add(new Fortune { Message = "Any program that runs right is obsolete." });
                                    db.Fortune.Add(new Fortune { Message = "A list is only as strong as its weakest link. — Donald Knuth" });
                                    db.Fortune.Add(new Fortune { Message = "Feature: A bug with seniority." });
                                    db.Fortune.Add(new Fortune { Message = "Computers make very fast, very accurate mistakes." });
                                    db.Fortune.Add(new Fortune { Message = "<script>alert(\"This should not be displayed in a browser alert box.\");</script>" });
                                    db.Fortune.Add(new Fortune { Message = "フレームワークのベンチマーク" });

                                    db.SaveChanges();
                                }

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
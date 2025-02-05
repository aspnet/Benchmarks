// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//using (var ctx = new EFCoreAotBenchmarkContext())
//{
//    await ctx.Database.EnsureDeletedAsync();
//    await ctx.Database.EnsureCreatedAsync();

//    var e0 = new MyEntity0
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum0.Foo,
//        Enum2 = MyEnum0.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot0>
//    {
//        new MyRoot0 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities0.Add(e0);

//    var e1 = new MyEntity1
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum1.Foo,
//        Enum2 = MyEnum1.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot1>
//    {
//        new MyRoot1 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities1.Add(e1);

//    var e2 = new MyEntity2
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum2.Foo,
//        Enum2 = MyEnum2.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot2>
//    {
//        new MyRoot2 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities2.Add(e2);

//    var e3 = new MyEntity3
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum3.Foo,
//        Enum2 = MyEnum3.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot3>
//    {
//        new MyRoot3 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities3.Add(e3);

//    var e4 = new MyEntity4
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum4.Foo,
//        Enum2 = MyEnum4.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot4>
//    {
//        new MyRoot4 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities4.Add(e4);

//    var e5 = new MyEntity5
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum5.Foo,
//        Enum2 = MyEnum5.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot5>
//    {
//        new MyRoot5 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities5.Add(e5);

//    var e6 = new MyEntity6
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum6.Foo,
//        Enum2 = MyEnum6.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot6>
//    {
//        new MyRoot6 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities6.Add(e6);

//    var e7 = new MyEntity7
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum7.Foo,
//        Enum2 = MyEnum7.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot7>
//    {
//        new MyRoot7 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities7.Add(e7);

//    var e8 = new MyEntity8
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum8.Foo,
//        Enum2 = MyEnum8.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot8>
//    {
//        new MyRoot8 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities8.Add(e8);

//    var e9 = new MyEntity9
//    {
//        Name1 = "e1_1",
//        Name2 = "e1_2",
//        Name3 = "e1_3",
//        Name4 = "e1_4",
//        Name5 = "e1_5",
//        Date1 = new DateTime(2000, 1, 1),
//        Date2 = new DateTime(3000, 1, 1),
//        Date3 = new DateTime(4000, 1, 1),
//        Enum1 = MyEnum9.Foo,
//        Enum2 = MyEnum9.Baz,
//        Number1 = 1,
//        Number2 = 2,
//        Number3 = 3,
//        PrimAr1 = [1, 2, 3, 4, 5],
//        PrimAr2 = ["A", "B", "C", "D"],
//        Owned = new List<MyRoot9>
//    {
//        new MyRoot9 { Prop1 = "p1", Prop2 = "p2", Prop3 = "p3", Prop4 = "p4", Prop5 = "p5" }
//    }
//    };
//    ctx.Entities9.Add(e9);
//    ctx.SaveChanges();
//}


using EFCoreAotQueriesBenchmarks;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("starting the app");

using (var ctx = new EFCoreAotBenchmarkContext())
{
    Console.WriteLine("accessing model");

    var model = ctx.Model;

    Console.WriteLine("On model creating ran? - " + EFCoreAotBenchmarkContext.OnModelCreatingRan);

    Console.WriteLine("accessing model - done");

    Console.WriteLine("running queries");

    var result0 = await ctx.Entities0.Where(x => x.Name1.Contains("e")).OrderBy(x => x.Id).Take(10).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result1 = await ctx.Entities1.Where(x => x.Name2.Contains("e")).OrderBy(x => x.Id).Take(11).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result2 = await ctx.Entities2.Where(x => x.Name3.Contains("e")).OrderBy(x => x.Id).Take(12).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result3 = await ctx.Entities3.Where(x => x.Name4.Contains("e")).OrderBy(x => x.Id).Take(13).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result4 = await ctx.Entities4.Where(x => x.Name5.Contains("e")).OrderBy(x => x.Id).Take(14).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result5 = await ctx.Entities5.Where(x => x.Number1 > 5).OrderBy(x => x.Id).Take(15).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result6 = await ctx.Entities6.Where(x => x.Number2 < 5).OrderBy(x => x.Id).Take(3).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result7 = await ctx.Entities7.Where(x => x.Number3 == 7).OrderBy(x => x.Id).Take(17).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result8 = await ctx.Entities8.Where(x => !x.Name1.Contains("e")).OrderBy(x => x.Id).Take(18).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();
    var result9 = await ctx.Entities9.Where(x => !x.Name1.Contains("a")).OrderBy(x => x.Id).Take(4).Select(x => new { x.Id, x.Name1, x.Name2, x.Name3, x.Name4, x.Name5, x.Number1, x.Number2, x.Number3, x.Enum1, x.Enum2, x.Date1, x.Date2, x.Date3, x.PrimAr1, x.PrimAr2, x.Owned }).AsNoTracking().ToListAsync();

    Console.WriteLine(result0.Count);

    Console.WriteLine("result1 name1: " + result1[0].Name1);


    Console.WriteLine("running queries - done");
}

Console.WriteLine("finished running the app");

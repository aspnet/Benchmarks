// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace EFCoreAotQueriesBenchmarks
{
    public class MyEntity1
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum1 Enum1 { get; set; }
        public MyEnum1 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot1> Owned { get; set; } = null!;
    }

    public enum MyEnum1
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot1
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity2
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum2 Enum1 { get; set; }
        public MyEnum2 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot2> Owned { get; set; } = null!;
    }

    public enum MyEnum2
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot2
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity3
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum3 Enum1 { get; set; }
        public MyEnum3 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot3> Owned { get; set; } = null!;
    }

    public enum MyEnum3
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot3
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity4
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum4 Enum1 { get; set; }
        public MyEnum4 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot4> Owned { get; set; } = null!;
    }

    public enum MyEnum4
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot4
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity5
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum5 Enum1 { get; set; }
        public MyEnum5 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot5> Owned { get; set; } = null!;
    }

    public enum MyEnum5
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot5
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity6
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum6 Enum1 { get; set; }
        public MyEnum6 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot6> Owned { get; set; } = null!;
    }

    public enum MyEnum6
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot6
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity7
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum7 Enum1 { get; set; }
        public MyEnum7 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot7> Owned { get; set; } = null!;
    }

    public enum MyEnum7
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot7
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity8
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum8 Enum1 { get; set; }
        public MyEnum8 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot8> Owned { get; set; } = null!;
    }

    public enum MyEnum8
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot8
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity9
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum9 Enum1 { get; set; }
        public MyEnum9 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot9> Owned { get; set; } = null!;
    }

    public enum MyEnum9
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot9
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }

    public class MyEntity0
    {
        public int Id { get; set; }
        public string Name1 { get; set; } = null!;
        public string Name2 { get; set; } = null!;
        public string Name3 { get; set; } = null!;
        public string Name4 { get; set; } = null!;
        public string Name5 { get; set; } = null!;

        public int Number1 { get; set; }
        public int Number2 { get; set; }
        public int Number3 { get; set; }

        public DateTime Date1 { get; set; }
        public DateTime Date2 { get; set; }
        public DateTime Date3 { get; set; }
        public MyEnum0 Enum1 { get; set; }
        public MyEnum0 Enum2 { get; set; }

        public int[] PrimAr1 { get; set; } = null!;
        public string[] PrimAr2 { get; set; } = null!;

        public List<MyRoot0> Owned { get; set; } = null!;
    }

    public enum MyEnum0
    {
        Foo,
        Bar,
        Baz,
    }

    public class MyRoot0
    {
        public string Prop1 { get; set; } = null!;
        public string Prop2 { get; set; } = null!;
        public string Prop3 { get; set; } = null!;
        public string Prop4 { get; set; } = null!;
        public string Prop5 { get; set; } = null!;
    }
}

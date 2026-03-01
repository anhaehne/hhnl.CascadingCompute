using hhnl.CascadingCompute.Shared.Attributes;

namespace Test;

class TestProgram2
{
    public static void Main()
    {
        var serviceInner = new ServiceInner();
        var serviceOuter = new ServiceOuter(serviceInner);

        var result = serviceOuter.AddTwice(1, 2);
        var result2 = serviceOuter.AddTwice(1, 2);

        serviceInner.CascadingCompute.InvalidateAdd(1, 2);

        var result3 = serviceOuter.AddTwice(1, 2);

        var x = 0;

    }
}

public partial class ServiceOuter(ServiceInner serviceInner)
{
    [CascadingCompute]
    public int AddTwice(int a, int b)
    {
        Console.WriteLine($"Calculating {a} + {b} + {b}");

        var result1 = serviceInner.Add(a, b);
        return serviceInner.Add(result1, b);
    }
}

public partial class ServiceInner
{
    [CascadingCompute(autoInvalidateInMilliseconds: 100)]
    public int Add(int a, int b)
    {
        Console.WriteLine($"Calculating {a} + {b}");
        return a + b;
    }
}
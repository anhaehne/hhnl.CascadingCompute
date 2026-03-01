using hhnl.CascadingCompute.Shared.Attributes;

namespace Test;

class TestProgram2
{
    public static void Run()
    {
        var serviceInner = new ServiceInner();
        var serviceOuter = new ServiceOuter(serviceInner);

        var result = serviceOuter.AddTwice(1, 2);
        var result2 = serviceOuter.AddTwice(1, 2);

        serviceInner.InvalidateAdd(1, 2);

        var result3 = serviceOuter.AddTwice(1, 2);
    }
}

public partial class ServiceOuter(IServiceInner serviceInner)
{
    [CascadingCompute]
    public int AddTwice(int a, int b)
    {
        Console.WriteLine($"Calculating {a} + {b} + {b}");

        var result1 = serviceInner.Add(a, b);
        return serviceInner.Add(result1, b);
    }
}

public partial interface IServiceInner
{
    [CascadingCompute]
    int Add(int a, int b);

    void InvalidateAdd(int a, int b);
}

public partial class ServiceInner : IServiceInner
{
    [CascadingCompute]
    public int Add(int a, int b)
    {
        Console.WriteLine($"Calculating {a} + {b}");
        return a + b;
    }

    public void InvalidateAdd(int a, int b)
        => CascadingCompute.InvalidateAdd(a, b);
}
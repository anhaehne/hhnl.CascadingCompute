using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

namespace Test;

class TestProgram2
{
    public static void Run()
    {
        var userContext = new UserContext();
        var serviceInner = new ServiceInner(userContext);
        var serviceMiddle = new ServiceMiddle(serviceInner, () => userContext.UserId);
        var serviceOuter = new ServiceOuter(serviceMiddle, userContext);

        var resultUser0 = serviceOuter.AddTwice(1, 2);
        serviceOuter.AddTwice(1, 2);

        userContext.UserId = 1;

        var resultUser1 = serviceOuter.AddTwice(1, 2);

        userContext.UserId = 0;
        serviceInner.InvalidateAdd(1, 2);
        var result3 = serviceOuter.AddTwice(1, 2);
    }
}

public partial class ServiceOuter(ServiceMiddle serviceOuter, UserContext userContext)
{
    [CascadingCompute]
    public int AddTwice(int a, int b)
    {
        Console.WriteLine($"[ServiceOuter] Add({a}, {b}) - UserId: {userContext.UserId} ");
        return serviceOuter.Add(a, b);
    }
}

public partial interface IServiceMiddle
{
    [CascadingCompute]
    int Add(int a, int b);

}

public partial class ServiceMiddle(IServiceInner serviceInner, Func<long> getUserId) : IServiceMiddle
{
    [CascadingCompute]
    public int Add(int a, int b)
    {
        Console.WriteLine($"[ServiceMiddle] Add({a}, {b}) - UserId: {GetUserId()} ");
        return serviceInner.Add(a, b);
    }

    public Func<long> GetUserId { get; set; } = getUserId;
}

public partial interface IServiceInner
{
    [CascadingCompute]
    int Add(int a, int b);
}


public partial class ServiceInner(UserContext userContext) : IServiceInner
{
    [CascadingCompute]
    public int Add(int a, int b)
    {
        Console.WriteLine($"[ServiceInner] Add({a}, {b}) - UserId: {userContext.UserId} ");
        return a + b;
    }

    public void InvalidateAdd(int a, int b)
        => Invalidation.InvalidateAdd(a, b);
}

public class UserContext : ICacheContextProvider<long>
{
    public long UserId { get; set; }

    public long GetCacheContext() => UserId;
}
using Test;

TestProgram2.Main();




//using hhnl.CascadingCompute.Caching;
//using hhnl.CascadingCompute.Shared.Attributes;


//var serviceInner = new ServiceInner();
//var serviceOuter = new ServiceOuter(serviceInner);

//var result = serviceOuter.CascadingCompute.AddTwice(1, 2);
//var result2 = serviceOuter.CascadingCompute.AddTwice(1, 2);

//serviceInner.CascadingCompute.InvalidateAdd(3, 2);

//var result3 = serviceOuter.CascadingCompute.AddTwice(1, 2);

//var x = 0;

//public partial class ServiceOuter(ServiceInner serviceInner)
//{
//    public CascadingComputeWrapper CascadingCompute => field ??= new CascadingComputeWrapper(this);

//    [CascadingCompute]
//    public int AddTwice(int a, int b)
//    {
//        var result1 = serviceInner.CascadingCompute.Add(a, b);
//        return serviceInner.CascadingCompute.Add(result1, b);
//    }

//    public class CascadingComputeWrapper(ServiceOuter implementation)
//    {
//        private static readonly ValueCache<ServiceOuter, (int a, int b), int> _addTwiceIntIntCache = new();

//        public int AddTwice(int a, int b)
//        {
//            return _addTwiceIntIntCache.GetOrAdd(implementation, (a, b), static (s, p) => s.AddTwice(p.a, p.b));
//        }

//        public void InvalidateAddTwice(int a, int b)
//        {
//            _addTwiceIntIntCache.Invalidate((a, b));
//        }
//    }
//}

//public class ServiceInner
//{
//    public CascadingComputeWrapper CascadingCompute => field ??= new CascadingComputeWrapper(this);

//    [CascadingCompute(autoInvalidateInMilliseconds: 100)]
//    public int Add(int a, int b)
//    {
//        Console.WriteLine($"Calculating {a} + {b}");
//        return a + b;
//    }

//    public class CascadingComputeWrapper(ServiceInner implementation)
//    {
//        private static readonly ValueCache<ServiceInner, (int a, int b), int> _addIntIntCache = new();

//        public int Add(int a, int b)
//        {
//            return _addIntIntCache.GetOrAdd(implementation, (a, b), static (s, p) => s.Add(p.a, p.b));
//        }

//        public void InvalidateAdd(int a, int b)
//        {
//            _addIntIntCache.Invalidate((a, b));
//        }
//    }
//}

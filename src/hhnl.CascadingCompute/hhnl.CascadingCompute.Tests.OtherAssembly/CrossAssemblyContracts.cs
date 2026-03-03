using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests.OtherAssembly.CrossAssembly.Contracts;

public partial interface ICrossAssemblyService
{
    [CascadingCompute]
    int Multiply(int left, int right);

    void InvalidateMultiply(int left, int right);
}

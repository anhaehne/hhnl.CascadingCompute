using Microsoft.AspNetCore.Mvc;

namespace hhnl.CascadingCompute.AspNetCore;

public abstract class CascadingComputeServiceController<TService>(TService service) : ControllerBase
{
    protected readonly TService _service = service;
}

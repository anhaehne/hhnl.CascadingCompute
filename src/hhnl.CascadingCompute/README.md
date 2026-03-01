# hhnl.CascadingCompute

`hhnl.CascadingCompute` adds generated caching wrappers for methods marked with `[CascadingCompute]`.

## What it does

When a method is marked with `[CascadingCompute]`, source generators create:

- a `CascadingCompute` wrapper property
- cached method calls
- `Invalidate<MethodName>(...)` methods
- `Invalidate<MethodName>(Func<..., bool> predicate)` methods
- `InvalidateAll<MethodName>()` methods
- `InvalidateAll()`

The library also supports cascading invalidation through `ValueCache` dependencies.

## Installation

Install the main package:

- `hhnl.CascadingCompute`

The generator package is brought in automatically by the main package.

## Basic usage

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public sealed partial class WeatherService
{
    private readonly Dictionary<int, int> _cityForecasts = new();
    private int _baseOffset = 5;

    [CascadingCompute]
    public int GetForecast(int cityId)
    {
        // expensive database lookup
        var baseValue = _cityForecasts.TryGetValue(cityId, out var value) ? value : 0;

        // expensive calculation
        return baseValue + _baseOffset;
    }

    public void SetForecast(int cityId, int value)
    {
        _cityForecasts[cityId] = value;
        CascadingCompute.InvalidateGetForecast(cityId);
    }

    public void SetBaseOffset(int offset)
    {
        _baseOffset = offset;
        CascadingCompute.InvalidateAll();
    }
}
```

You can then use:

```csharp
var service = new WeatherService();

service.SetForecast(10, 20);

var a = service.GetForecast(10); // computes (25)
var b = service.GetForecast(10); // cached (25)

service.SetForecast(10, 30); // updates value + invalidates cache entry
var c = service.GetForecast(10); // recomputes (35)

service.CascadingCompute.InvalidateGetForecast(cityId => cityId > 100); // invalidates matching entries by predicate

service.CascadingCompute.InvalidateAllGetForecast(); // invalidates all GetForecast cache entries

service.SetBaseOffset(2); // changes shared input + invalidates all cache entries
var d = service.GetForecast(10); // recomputes (32)

service.CascadingCompute.InvalidateAll();
```

Predicate invalidation allows selective clearing for one method:

```csharp
service.CascadingCompute.InvalidateGetForecast(cityId => cityId is 10 or 20);
```

### Parameter requirements and behavior
Two calls to the same method are considered the same cache entry if all parameters and context parameters (see below) are equal (== operator).
If you have to pass complex types as parameters, make sure to implement equality members (Equals/GetHashCode) or use reference types with stable references.
If you pass a list of items, you can use `EquatableSet<T>`. This class uses a hash set internally and implements equality based on the set of items, ignoring order and duplicates.
Be aware that all parameters are stored in memory until the cache entry is invalidated, so avoid passing large objects or collections as parameters.

## Interface support

`[CascadingCompute]` also works on partial interfaces.

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public partial interface IExchangeRateService
{
    [CascadingCompute]
    decimal GetRate(string fromCurrency, string toCurrency);

    // If you want to externally trigger invalidation, you manually define the invalidate method in the interface. 
    // Otherwise invalidation can only be triggered by code with access to the implementation.
    // This is not required for the cascade invalidation to work, only if you want to trigger invalidation from code with access to the interface only.
    void InvalidateGetRate(string fromCurrency, string toCurrency);
}

public sealed partial class ExchangeRateService : IExchangeRateService
{
    public decimal GetRate(string fromCurrency, string toCurrency)
    {
        // expensive lookup
        return 1.12m;
    }

    public void InvalidateGetRate(string fromCurrency, string toCurrency)
        => CascadingCompute.InvalidateGetRate(fromCurrency, toCurrency);
}
```

```csharp
IExchangeRateService service = new ExchangeRateService();

var first = service.GetRate("EUR", "USD"); // computes
var second = service.GetRate("EUR", "USD"); // cached

service.InvalidateGetRate("EUR", "USD");
var third = service.GetRate("EUR", "USD"); // recomputes
```

## Cascading cache invalidation example

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public sealed partial class PriceService
{
    [CascadingCompute]
    public decimal GetPrice(int productId) => productId * 1.5m;
}

public sealed partial class BasketService
{
    private readonly PriceService _priceService;

    public BasketService(PriceService priceService)
    {
        _priceService = priceService;
    }

    [CascadingCompute]
    public decimal GetBasketTotal(int productId, int amount)
        => _priceService.GetPrice(productId) * amount;
}
```

```csharp
var priceService = new PriceService();
var basketService = new BasketService(priceService);

var first = basketService.GetBasketTotal(42, 2);   // computes
var second = basketService.GetBasketTotal(42, 2);  // cached

// Invalidate inner cache entry
priceService.CascadingCompute.InvalidateGetPrice(42);

// Dependent outer cache entries are invalidated automatically
var third = basketService.GetBasketTotal(42, 2);   // recomputes
```

## Ignoring parameters in cache keys

Use ignore attributes to remove parameters from cache key generation and generated invalidate signatures.

### Parameter-only ignore

```csharp
using hhnl.CascadingCompute.Attributes;
using hhnl.CascadingCompute.Shared.Attributes;

public sealed partial class UserService
{
    [CascadingCompute]
    public int GetUser(int id, [CascadingComputeIgnore] string traceId)
    {
        return id;
    }
}
```

### Rule-based ignore (method/type/interface/assembly)

```csharp
using hhnl.CascadingCompute.Attributes;

[CascadingComputeIgnoreParameter("traceId")]
public sealed partial class UserService
{
    [CascadingCompute]
    public int GetUser(int id, string traceId) => id;
}
```

### Class-level parameter ignore

```csharp
using hhnl.CascadingCompute.Attributes;
using hhnl.CascadingCompute.Shared.Attributes;

[CascadingComputeIgnoreParameter("traceId")]
public sealed partial class UserService
{
    [CascadingCompute]
    public int GetUser(int id, string traceId) => id;

    // Generated invalidate method only needs `id`
    // service.CascadingCompute.InvalidateGetUser(id);
}
```

### Assembly-level parameter ignore

```csharp
using hhnl.CascadingCompute.Attributes;

[assembly: CascadingComputeIgnoreParameter(typeof(System.Threading.CancellationToken))]
```

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public sealed partial class ReportService
{
    [CascadingCompute]
    public string Build(int id, CancellationToken cancellationToken)
        => $"report-{id}";
}

// `CancellationToken` is ignored in cache key/invalidate signature:
// service.CascadingCompute.InvalidateBuild(id);
```

You can also ignore by type:

```csharp
[CascadingComputeIgnoreParameter(typeof(CancellationToken))]
```

`CancellationToken` is ignored by default in generated cache keys/invalidate signatures.

## Cache context (`ICacheContextProvider<TContext>`)

`ICacheContextProvider<TContext>` allows adding additional context values to generated cache keys without changing method signatures.

This is useful for multi-tenant or user-specific caches (for example, current user id or tenant id).

```csharp
using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

public sealed class CurrentUserContextProvider : ICacheContextProvider<string>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCacheContext()
        => _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "anonymous";
}

public sealed partial class ProfileService
{
    // All fields/properties of type `ICacheContextProvider<T>` are automatically included in generated cache keys.
    private readonly CurrentUserContextProvider _userContext;

    public ProfileService(CurrentUserContextProvider userContext)
    {
        _userContext = userContext;
    }

    [CascadingCompute]
    public string GetDisplayName(int userId)
    {
        // expensive lookup
        return $"user-{userId}";
    }
}
```

`GetDisplayName(5)` called by two different users creates two different cache entries because `_userContext.GetCacheContext()` is automatically included in the generated cache key.

When using predicate invalidation with cache context providers, generated predicates include context values after method parameters:

```csharp
// signature example: InvalidateGetDisplayName(Func<int, string, bool> predicate)
service.CascadingCompute.InvalidateGetDisplayName((userId, contextUser) => userId == 5 && contextUser == "alice");
```

## Auto invalidation

Use `[AutoInvalidate(milliseconds)]` on `[CascadingCompute]` methods to expire entries automatically.

## Cache entry lifetime observers (`CacheEntryLifetimeObserverAttribute`)

Use `CacheEntryLifetimeObserverAttribute` when you want to react to cache entry lifecycle events.

You can attach an observer attribute to a `[CascadingCompute]` method. The observer receives callbacks when:

- a cache entry is created (`OnCacheEntryCreated`)
- a cache entry is invalidated (`OnCacheEntryInvalidated`)

This is useful for logging, metrics, telemetry, or diagnostics.

```csharp
using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

public sealed class LogCacheEntryLifetimeAttribute : CacheEntryLifetimeObserverAttribute
{
    public override void OnCacheEntryCreated<TResult>(ICacheEntry<TResult> cacheEntry)
    {
        Console.WriteLine($"[cache-created] key={cacheEntry.Key}");
    }

    public override void OnCacheEntryInvalidated<TResult>(ICacheEntry<TResult> cacheEntry)
    {
        Console.WriteLine($"[cache-invalidated] key={cacheEntry.Key}");
    }
}

public sealed partial class ProductService
{
    [CascadingCompute]
    [LogCacheEntryLifetime]
    public decimal GetPrice(int productId)
        => productId * 1.5m;
}
```

Notes:

- `CacheEntryLifetimeObserverAttribute` is method-level only.
- The observer type must be instantiable by generated code (for example, parameterless constructor).

## Requirements

- C# project with source generators enabled
- target methods must be:
  - instance methods
  - non-`void`
  - non-`ref`/`out` parameter methods
- containing class/interface must be `partial`

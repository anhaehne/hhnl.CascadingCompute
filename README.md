# hhnl.CascadingCompute

`hhnl.CascadingCompute` is a caching library that adds caching on a method level and handles dependency tracking.

## Installation

Install the main package:

- `hhnl.CascadingCompute`

The generator package is brought in automatically by the main package.

## Basic usage

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public partial class WeatherService
{
    private readonly Dictionary<int, int> _cityForecasts = new();

    [CascadingCompute]
    public int GetForecast(int cityId)
    {
        // expensive database lookup
        var forecast = _cityForecasts.TryGetValue(cityId, out var value) ? value : 0;
        return forecast;
    }

    public void SetForecast(int cityId, int value)
    {
        _cityForecasts[cityId] = value;
        Invalidation.InvalidateGetForecast(cityId);
    }
}
```

You can then use:

```csharp
var service = new WeatherService();

service.SetForecast(10, 20);

var a = service.GetForecast(10); // computes (20)
var b = service.GetForecast(10); // cached (20)

service.SetForecast(10, 30); // updates value + invalidates cache entry
var c = service.GetForecast(10); // recomputes (30)
```

### Parameter requirements and behavior
Two calls to the same method are considered the same cache entry if all parameters and context parameters (see below) are equal (== operator).
If you have to pass complex types as parameters, make sure to implement the == operator or use reference types with stable references.
If you pass a list of items, you can use `EquatableSet<T>`. This class uses a hash set internally and implements equality based on the set of items, ignoring order and duplicates.
Be aware that all parameters are stored in memory until the cache entry is invalidated, so avoid passing large objects or collections as parameters.

## Invalidation

Since invalidation is the hard part in caching, this library tries to make it as easy as possible.

To invalidate a cache entry, call the invalidate method on the `Invalidation` property that corresponds to you method. So for a method `Add(int a, int b)` call `Invalidation.InvalidateAdd(a, b)` to invalidate the cache entry.

This library also handles dependency tracking between cache entries and makes sure dependent entries are invalidated as well.

### Manual invalidation

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public partial class WeatherService
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

        // Invalidates the cache entry for the given city id.
        Invalidation.InvalidateGetForecast(cityId);
    }

    public void SetBaseOffset(int offset)
    {
        _baseOffset = offset;

        // Invalidates all cache entries.
        Invalidation.InvalidateGetForecast(_ => true);
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

service.SetBaseOffset(2); // changes shared input + invalidates all cache entries
var d = service.GetForecast(10); // recomputes (32)
```

Predicate invalidation allows selective clearing for one method:

```csharp
// inside WeatherService
Invalidation.InvalidateGetForecast(cityId => cityId is 10 or 20);
```
### Automatic invalidation

Use `[AutoInvalidate(minutes: 30)]` on `[CascadingCompute]` methods to expire entries automatically.

### Cascading cache invalidation example

Nested calls to `[CascadingCompute]` methods create dependent cache entries. 

If method `A()` calls method `B()`, then `CacheEntry<A>` depends on `CacheEntry<B>`.
If `CacheEntry<B>` is invalidated, `CacheEntry<A>` is invalidated automatically to make sure everything is recomputed.

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public partial class PriceService
{
    [CascadingCompute]
    public decimal GetPrice(int productId) => productId * 1.5m;

    public void InvalidateGetPrice(int productId)
        => Invalidation.InvalidateGetPrice(productId);
}

public partial class BasketService
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

var first = basketService.GetBasketTotal(productId: 42, amount: 2);   // computes
var second = basketService.GetBasketTotal(productId: 42, amount: 2);  // cached

// Invalidate inner cache entry
priceService.InvalidateGetPrice(productId: 42);

// Dependent outer cache entries are invalidated automatically
var third = basketService.GetBasketTotal(productId: 42, amount: 2);   // recomputes
```


## Interface support

`[CascadingCompute]` also works on partial interfaces.

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public partial interface IExchangeRateService
{
    [CascadingCompute]
    decimal GetRate(string fromCurrency, string toCurrency);
}

public partial class ExchangeRateService : IExchangeRateService
{
    public decimal GetRate(string fromCurrency, string toCurrency)
    {
        // expensive lookup
        return 1.12m;
    }

    public void InvalidateGetRate(string fromCurrency, string toCurrency)
        => Invalidation.InvalidateGetRate(fromCurrency, toCurrency);
}
```

```csharp
IExchangeRateService service = new ExchangeRateService();

var first = service.GetRate("EUR", "USD"); // computes
var second = service.GetRate("EUR", "USD"); // cached

((ExchangeRateService)service).InvalidateGetRate("EUR", "USD");
var third = service.GetRate("EUR", "USD"); // recomputes
```

## Ignoring parameters in cache keys

Use ignore attributes to remove parameters from cache key generation and generated invalidate signatures.

### Parameter-only ignore

```csharp
using hhnl.CascadingCompute.Attributes;
using hhnl.CascadingCompute.Shared.Attributes;

public partial class UserService
{
    [CascadingCompute]
    public int GetUser(int id, [CascadingComputeIgnore] string traceId)
    {
        return id;
    }
}
```

### Class-level parameter ignore

```csharp
using hhnl.CascadingCompute.Attributes;

[CascadingComputeIgnoreParameter("traceId")]
public partial class UserService
{
    [CascadingCompute]
    public int GetUser(int id, string traceId) => id;

    // Generated invalidate method only needs `id`
    // Invalidation.InvalidateGetUser(id);
}
```

### Assembly-level parameter ignore

```csharp
using hhnl.CascadingCompute.Attributes;

[assembly: CascadingComputeIgnoreParameter("traceId")]
```

```csharp
using hhnl.CascadingCompute.Shared.Attributes;

public partial class ReportService
{
    [CascadingCompute]
    public string Build(int id, string traceId)
        => $"report-{id}";
}

```

You can also ignore by type:

```csharp
[CascadingComputeIgnoreParameter(typeof(MyType))]
```

`CancellationToken` is ignored by default in generated cache keys/invalidate signatures.

## Cache context (`ICacheContextProvider<TContext>`)

`ICacheContextProvider<TContext>` allows adding additional context values to generated cache keys without changing method signatures.

This is useful for multi-tenant or user-specific caches (for example, current user id or tenant id).

```csharp
using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

public class CurrentUserContextProvider : ICacheContextProvider<string>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCacheContext()
        => _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "anonymous";
}

public partial class ProfileService
{
    // All fields/properties of type `ICacheContextProvider<T>` are automatically included in generated cache keys.
    private readonly CurrentUserContextProvider _userContext;
    private readonly Dictionary<string, string> _displayNames = new();

    public ProfileService(CurrentUserContextProvider userContext)
    {
        _userContext = userContext;
    }

    [CascadingCompute]
    public string GetDisplayName()
    {
        var userName = _userContext.GetCacheContext();

        // Expensive db lookup
        if(_displayNames.TryGetValue(_userName, out var displayName))
            return displayName;

        // expensive lookup
        return _userName;
    }

    public void SetDisplayName(string displayName)
    {
        var userName = _userContext.GetCacheContext();
        _displayNames[userName] = displayName;

        // You don't have to pass the username, the context is automatically included from _userContext.
        Invalidation.InvalidateGetDisplayName();
    }
}
```

`GetDisplayName()` called by two different users creates two different cache entries because `_userContext.GetCacheContext()` is automatically included in the generated cache key.

When using predicate invalidation with cache context providers, generated predicates include context values after method parameters:

```csharp
// signature example: InvalidateGetDisplayName(Func<string, bool> predicate)
// inside ProfileService
Invalidation.InvalidateGetDisplayName((userName) => userName == "alice");
```

### Cache context, taints and tolerations

With cache context providers, invalidation stays scoped to the active context (for example tenant/user), even across nested cascading calls.

As an example consider the following scenario.
We have the 3 services below. The `UserSettingService` calls the `UserAccessor` to get the current user. 
The `UserAccessor` implements `ICacheContextProvider<long>` to provide the user id as cache context. 

```
  ┌───────────────────────┐     ┌───────────────────────┐     ┌───────────────────────┐     ┌───────────────────┐    
  │ ViewModelService      │     │ MovieService          │     │ UserSettingsService   │     │ UserAccessor      │    
  │───────────────────────│     │───────────────────────│     │───────────────────────│     │───────────────────│    
  │ [CascadingCompute]    │     │ [CascadingCompute]    │     │ [CascadingCompute]    │     │                   │    
  │ GetMovieViewModel(int)├────►│ GetMovieViewModel(int)├────►│ GetUserLanguage()     ├────►│ GetCacheContext() │    
  └──────────▲────────────┘     └──────────▲────────────┘     └──────────▲────────────┘     └───────────────────┘    
             │                             │                             │                                           
  ┌──────────▼────────────┐     ┌──────────▼────────────┐     ┌──────────▼────────────┐                              
  │CacheEntry:            │     │CacheEntry:            │     │CacheEntry:            │                              
  │- GetMovieViewModel(1) │     │- GetMovie(1)          │     │- GetUserLanguage()    │                              
  │  Taints:              │     │  Taints:              │     │  Taints:              │                              
  │  UserAccessor|long: 1 ◄─────┼─ UserAccessor|long: 1 ◄─────┼─ UserAccessor|long: 1 │                              
  └───────────────────────┘     └───────────────────────┘     └───────────────────────┘                              
```
To make sure all cache entries that depend on the use id (directly and indirectly) are only valid when the user id is the same, a `taint` is applied to the cache entries.
The next time `GetMovieViewModel(1)` is called, we not only check if the parameters match but also the taints. Since the user id is not yet known in the `ViewModelService`,
we can't serve the cached value. The same applies to the `MovieService`. 

In the `UserSettingsService` the `UserAccessor` is referenced directly so it is used to apply a toleration to the current context.
This toleration (`UserAccessor|long: 1`) allows the cache entry to be used. To allow the cache entries from the `ViewModelService` to be used directly we have to create the tolerations here.


We can do that by referencing the `UserAccessor` in the `ViewModelService`. This will create the toleration and allow the service to use it's tainted cache entries.
Tolerations will always flow along the call chain. Even if the cache entry in the `ViewModelService` is invalidated, the cache entry in the `MovieService` can be used since the toleration is passed from the `ViewModelService` to the `MovieService`.

In short:
- Reference `ICacheContextProvider<>` in your first entry point to make sure it is available to all dependencies.
- Tolerations flow down to dependencies.
- Taints flow up to dependent cache entries.

#### Invalidation
When invalidating a cache entry via the `Invalidate{MethodName}({Parameters})` method, the current context (`taints`) are used to determine which cache entries should be invalidated.
When invalidating via the predicate methods (`Invalidate{MethodName}(Func<{Parameters}, bool>)`), all cache entries are checked against the predicate, regardless of the taints.

## Cache entry lifetime observers (`ICacheEntryLifetimeObserver`)

Use `ICacheEntryLifetimeObserver` when you want to react to cache entry lifecycle events.

There are two ways to attach an observer. 
- Have a field/property/primary constructor parameter that implement `ICacheEntryLifetimeObserver`.
- Create an `Attribute` based on `CacheEntryLifetimeObserverAttribute` and attach that property to the method.

`ICacheEntryLifetimeObserver` get notified when:
- a cache entry is created (`OnCacheEntryCreated`)
- a cache entry is invalidated (`OnCacheEntryInvalidated`)

This is useful for logging, metrics, telemetry, or diagnostics or implementing custom invalidation logic.

Example:
```csharp
using hhnl.CascadingCompute.Shared.Attributes;
using hhnl.CascadingCompute.Shared.Interfaces;

public class LogCacheEntryLifetimeAttribute : CacheEntryLifetimeObserverAttribute
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

public partial class ProductService
{
    [CascadingCompute]
    [LogCacheEntryLifetime]
    public decimal GetPrice(int productId)
        => productId * 1.5m;
}
```
using hhnl.CascadingCompute.Caching;
using hhnl.CascadingCompute.Shared.Attributes;

namespace hhnl.CascadingCompute.Tests;

[TestClass]
public class CascadingComputeUtilsTests
{
    [TestMethod]
    public async Task ExecuteCascadingComputeAsAsyncEnumerable_should_throw_when_no_dependency_is_registered()
    {
        // Arrange
        var enumerable = CascadingComputeUtils.ExecuteCascadingComputeAsAsyncEnumerable(_ => Task.FromResult(1));

        await using var enumerator = enumerable.GetAsyncEnumerator();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await enumerator.MoveNextAsync();
        });
    }

    [TestMethod]
    public async Task ExecuteCascadingComputeAsAsyncEnumerable_should_retrieve_next_item_on_invalidate()
    {
        // Arrange
        var service = new StubService();
        var enumerable = CascadingComputeUtils.ExecuteCascadingComputeAsAsyncEnumerable(_ => service.CountUpAsync());

        await using var enumerator = enumerable.GetAsyncEnumerator();

        // Act 1
        var movedToFirst = await enumerator.MoveNextAsync();

        // Assert 1
        Assert.IsTrue(movedToFirst);
        Assert.AreEqual(1, enumerator.Current);

        // Act 2
        var secondMoveTask = enumerator.MoveNextAsync();
        service!.Invalidate();
        var movedToSecond = await secondMoveTask;

        // Assert 2
        Assert.IsTrue(movedToSecond);
        Assert.AreEqual(2, enumerator.Current);
    }

    [TestMethod]
    public async Task ExecuteCascadingComputeAsAsyncEnumerable_should_yield_first_item_when_dependency_check_is_disabled()
    {
        // Arrange
        var enumerable = CascadingComputeUtils.ExecuteCascadingComputeAsAsyncEnumerable(
            _ => Task.FromResult(5),
            throwWithoutDepdendency: false);

        await using var enumerator = enumerable.GetAsyncEnumerator();

        // Act 1
        var movedToFirst = await enumerator.MoveNextAsync();

        // Assert 1
        Assert.IsTrue(movedToFirst);
        Assert.AreEqual(5, enumerator.Current);
    }

    [TestMethod]
    public async Task ExecuteCascadingComputeAsAsyncEnumerable_should_stop_immediately_when_token_is_already_canceled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var enumerable = CascadingComputeUtils.ExecuteCascadingComputeAsAsyncEnumerable(
            _ => Task.FromResult(1),
            cancellationToken: cts.Token);

        await using var enumerator = enumerable.GetAsyncEnumerator(cts.Token);

        // Act 1
        var moved = await enumerator.MoveNextAsync();

        // Assert 1
        Assert.IsFalse(moved);
    }

    [TestMethod]
    public async Task ExecuteCascadingComputeAsAsyncEnumerable_should_forward_cancellation_token_to_function()
    {
        // Arrange
        CancellationToken observedToken = default;
        using var cts = new CancellationTokenSource();

        var enumerable = CascadingComputeUtils.ExecuteCascadingComputeAsAsyncEnumerable(
            ct =>
            {
                observedToken = ct;
                return Task.FromResult(1);
            },
            throwWithoutDepdendency: false,
            cancellationToken: cts.Token);

        await using var enumerator = enumerable.GetAsyncEnumerator(cts.Token);

        // Act 1
        var moved = await enumerator.MoveNextAsync();

        // Assert 1
        Assert.IsTrue(moved);
        Assert.AreEqual(cts.Token, observedToken);
    }
}

public partial class StubService
{
    private int counter = 1;

    [CascadingCompute]
    public async Task<int> CountUpAsync() => counter++;


    public void Invalidate() => Invalidation.InvalidateCountUpAsync();
}
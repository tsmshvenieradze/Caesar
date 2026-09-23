namespace Caesar.Tests.UnitType;

public class UnitTests
{
    [Fact]
    public void All_units_are_equal()
    {
        var a = Unit.Value;
        var b = default(Unit);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(null));
        Assert.Equal(0, a.GetHashCode());
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(0, ((IComparable)a).CompareTo(b));
    }

    [Fact]
    public void Comparison_operators_treat_units_as_equal()
    {
        var left = Unit.Value;
        var right = default(Unit);

        Assert.False(left < right);
        Assert.False(left > right);
        Assert.True(left <= right);
        Assert.True(left >= right);
    }

    [Fact]
    public async Task Unit_Task_is_completed_with_Value()
    {
        Assert.True(Unit.Task.IsCompletedSuccessfully);
        Assert.Equal(Unit.Value, await Unit.Task);
    }

    [Fact]
    public void ToString_returns_empty_tuple()
    {
        Assert.Equal("()", Unit.Value.ToString());
    }
}

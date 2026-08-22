using Snookering.Core.Mathematics;
using Xunit;

namespace Snookering.Core.Tests.Mathematics;

public class Vec2Tests
{
    [Fact]
    public void Arithmetic_BehavesComponentwise()
    {
        var a = new Vec2(3.0, -2.0);
        var b = new Vec2(1.5, 4.0);

        Assert.Equal(new Vec2(4.5, 2.0), a + b);
        Assert.Equal(new Vec2(1.5, -6.0), a - b);
        Assert.Equal(new Vec2(6.0, -4.0), a * 2.0);
        Assert.Equal(new Vec2(1.5, -1.0), a / 2.0);
        Assert.Equal(new Vec2(-3.0, 2.0), -a);
    }

    [Fact]
    public void DotAndCross_MatchDefinitions()
    {
        var a = new Vec2(2.0, 3.0);
        var b = new Vec2(-1.0, 4.0);

        Assert.Equal(2.0 * -1.0 + 3.0 * 4.0, a.Dot(b));
        Assert.Equal(2.0 * 4.0 - 3.0 * -1.0, a.Cross(b));
    }

    [Fact]
    public void Length_OfThreeFourTriangle_IsFive()
    {
        Assert.Equal(5.0, new Vec2(3.0, 4.0).Length);
        Assert.Equal(25.0, new Vec2(3.0, 4.0).LengthSquared);
    }

    [Fact]
    public void Normalized_ReturnsUnitVector_AndZeroForZero()
    {
        var n = new Vec2(3.0, 4.0).Normalized();
        Assert.Equal(0.6, n.X, 15);
        Assert.Equal(0.8, n.Y, 15);
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
    }

    [Fact]
    public void Perp_IsCounterClockwiseRotation()
    {
        Assert.Equal(new Vec2(0.0, 1.0), new Vec2(1.0, 0.0).Perp);
        Assert.Equal(new Vec2(-1.0, 0.0), new Vec2(0.0, 1.0).Perp);
    }
}

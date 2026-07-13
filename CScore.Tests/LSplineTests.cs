using CSmath;
using Xunit;

namespace CScore.Tests
{
    public class LSplineTests
    {
        [Fact]
        public void DuplicateXThrowsInsteadOfProducingNaN()
        {
            Assert.Throws<ArgumentException>(() =>
                new LSpline(new[] { -0.0035, 0.0, 0.0 }, new[] { -20.0, 0.0, 0.0 }));
        }
    }
}

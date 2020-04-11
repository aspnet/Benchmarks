using Xunit;

namespace BenchmarksBot
{
    public class Tests
    {
        [Fact]
        public void ShouldMatchExitingScenario()
        {
            var tags = Tags.Match("GRPCUnary");
            Assert.NotEmpty(tags);
        }

        [Fact]
        public void ShouldNotMatchScenario()
        {
            var tags = Tags.Match("NoGRPC");
            Assert.Empty(tags);
        }
    }
}

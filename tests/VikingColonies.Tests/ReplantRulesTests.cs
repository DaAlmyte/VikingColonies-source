using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ReplantRulesTests
    {
        [Theory]
        [InlineData(false, true, true, ReplantState.Off)]
        [InlineData(false, false, false, ReplantState.Off)]
        [InlineData(true, true, false, ReplantState.NeedsCultivator)]
        [InlineData(true, true, true, ReplantState.On)]
        [InlineData(true, false, false, ReplantState.On)]
        public void Replanting_needs_the_setting_and_a_cultivator_when_required(bool replantOn, bool needsCultivator, bool hasCultivator, ReplantState expected)
        {
            Assert.Equal(expected, ReplantRules.State(replantOn, needsCultivator, hasCultivator));
        }
    }
}

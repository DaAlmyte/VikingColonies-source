using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The Town Hall's overview line about the storehouse (1.5.0, the review round's pick).</summary>
    public class StorehouseWarningsTests
    {
        [Fact]
        public void No_storehouse_or_no_couriers_says_nothing()
        {
            Assert.Equal(StorehouseWarning.None, StorehouseWarnings.For(hasStorehouse: false, storeFull: true, couriers: 2, holding: 1));
            Assert.Equal(StorehouseWarning.None, StorehouseWarnings.For(hasStorehouse: true, storeFull: true, couriers: 0, holding: 0));
        }

        [Fact]
        public void A_full_store_with_couriers_says_full_whether_or_not_they_hold_loads()
        {
            Assert.Equal(StorehouseWarning.Full, StorehouseWarnings.For(hasStorehouse: true, storeFull: true, couriers: 1, holding: 0));
            Assert.Equal(StorehouseWarning.Full, StorehouseWarnings.For(hasStorehouse: true, storeFull: true, couriers: 3, holding: 2));
        }

        [Fact]
        public void Couriers_holding_loads_while_the_store_has_room_say_so()
        {
            Assert.Equal(StorehouseWarning.Holding, StorehouseWarnings.For(hasStorehouse: true, storeFull: false, couriers: 2, holding: 1));
            Assert.Equal(StorehouseWarning.None, StorehouseWarnings.For(hasStorehouse: true, storeFull: false, couriers: 2, holding: 0));
        }

        [Fact]   // the roster can lag a hire behind a status: a courier holding a load is a courier
        public void A_holding_courier_counts_even_before_the_roster_has_the_hire()
        {
            Assert.Equal(StorehouseWarning.Holding, StorehouseWarnings.For(hasStorehouse: true, storeFull: false, couriers: 0, holding: 1));
            Assert.Equal(StorehouseWarning.Full, StorehouseWarnings.For(hasStorehouse: true, storeFull: true, couriers: 0, holding: 1));
        }
    }
}

using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class CitizenLineTests
    {
        [Fact]
        public void Jobless_citizen_shows_the_status_once()
        {
            Assert.Equal("<b>Erik</b>  —  Jobless", CitizenLine.Format("Erik", "", "Jobless", ""));
        }

        [Fact]
        public void Worker_shows_job_status_and_workplace()
        {
            Assert.Equal("<b>Ulf</b>  —  Lumberjack, Working  @ Lumberjack's Hut",
                CitizenLine.Format("Ulf", "Lumberjack", "Working", "Lumberjack's Hut"));
        }

        [Fact]
        public void Status_equal_to_the_job_title_is_not_repeated()
        {
            Assert.Equal("<b>Knut</b>  —  Jobless", CitizenLine.Format("Knut", "Jobless", "Jobless", ""));
        }
    }
}

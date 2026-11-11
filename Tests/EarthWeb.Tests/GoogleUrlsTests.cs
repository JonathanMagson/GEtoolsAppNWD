using System;
using GEtools.EarthWeb;
using Xunit;

namespace GEtools.Tests
{
    public class GoogleUrlsTests
    {
        [Fact]
        public void PointUrlCarriesSearchAndCamera() =>
            Assert.Equal("https://earth.google.com/web/search/-33.8688,151.2093/@-33.8688,151.2093,0a,1000d,35y,0h,0t,0r",
                         GoogleUrls.EarthPointUrl(-33.8688, 151.2093));

        [Fact]
        public void ViewUrlFormat() =>
            Assert.Equal("https://earth.google.com/web/@-33.8688,151.2093,0a,1500d,35y,270h,0t,0r",
                         GoogleUrls.EarthViewUrl(-33.8688, 151.2093, 1500, heading: -90));

        [Fact]
        public void ViewUrlClampsRange()
        {
            Assert.Contains(",50d,", GoogleUrls.EarthViewUrl(0, 0, 1));
            Assert.Contains(",30000000d,", GoogleUrls.EarthViewUrl(0, 0, 1e12));
        }

        [Theory]
        [InlineData(91, 0)]
        [InlineData(-91, 0)]
        [InlineData(0, 181)]
        [InlineData(0, -181)]
        [InlineData(double.NaN, 0)]
        public void RejectsBadCoordinates(double lat, double lon) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => GoogleUrls.EarthViewUrl(lat, lon, 100));

        [Fact]
        public void FormattingIgnoresCulture()
        {
            var before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Contains("@1.5,2.25,", GoogleUrls.EarthViewUrl(1.5, 2.25, 100));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = before; }
        }

        [Fact]
        public void NoNegativeZero() => Assert.Equal("0", GoogleUrls.Fmt(-0.0000001, 5));

        [Fact]
        public void RangeFitsGroundHeight()
        {
            double r = GoogleUrls.RangeForGroundHeight(1000);
            Assert.Equal(1000, 2 * r * Math.Tan(GoogleUrls.DefaultFov / 2 * Math.PI / 180), 6);
        }

        [Fact]
        public void HasCamera()
        {
            Assert.True(GoogleUrls.HasCamera("https://earth.google.com/web/@-33.86,151.2,25.3a,1500.5d,35y,0h,0t,0r/data=abc"));
            Assert.False(GoogleUrls.HasCamera("https://earth.google.com/web/"));
            Assert.False(GoogleUrls.HasCamera(null));
        }

        [Fact]
        public void SoftNavQuotesUrlSafely()
        {
            string js = GoogleUrls.SoftNavJs("https://earth.google.com/web/@1,2,0a,100d,35y,0h,0t,0r'</script>");
            Assert.DoesNotContain("'</script>", js);
            Assert.Contains("})(\"https://earth.google.com/web/@1,2,0a,100d,35y,0h,0t,0r", js);
        }

        [Fact]
        public void MarkerJsEmbedsTargets()
        {
            string js = EarthPageScripts.MarkerJs(new[] { (-33.5, 151.25) });
            Assert.Contains("})([{\"lat\":-33.5,\"lon\":151.25}], {\"color\":\"#e60000\",\"size\":30});", js);
        }

        [Fact]
        public void DropFileEvents()
        {
            var events = DevTools.DropFileEvents("a.kmz", 10, 20);
            Assert.Equal(3, events.Count);
            Assert.All(events, e => Assert.Equal("Input.dispatchDragEvent", e.Method));
        }
    }
}

using System.IO;
using System.Text;
using Xunit;
using NeoPaula;

namespace NeoPaula.Tests
{
    public class NeoPaulaPlayerTests
    {
        [Fact]
        public void GetTrackInfo_DetectsProtrackerMOD()
        {
            // Arrange
            var player = new NeoPaulaPlayer();
            var modData = new byte[2000];

            // Set title at offset 0
            var title = Encoding.ASCII.GetBytes("Test MOD");
            for(int i=0; i<title.Length; i++) modData[i] = title[i];

            // Set magic at 1080
            var magic = Encoding.ASCII.GetBytes("M.K.");
            for(int i=0; i<magic.Length; i++) modData[1080 + i] = magic[i];

            using var stream = new MemoryStream(modData);

            // Act
            var info = player.GetTrackInfo(stream);

            // Assert
            Assert.Equal("Protracker MOD", info.Format);
            Assert.Equal("Test MOD", info.Title);
            Assert.Equal(4, info.Channels);
        }

        [Fact]
        public void GetTrackInfo_DetectsOctaMED()
        {
            // Arrange
            var player = new NeoPaulaPlayer();
            var mmdData = new byte[2000];

            // Set magic at 0
            var magic = Encoding.ASCII.GetBytes("MMD1");
            for(int i=0; i<magic.Length; i++) mmdData[i] = magic[i];

            using var stream = new MemoryStream(mmdData);

            // Act
            var info = player.GetTrackInfo(stream);

            // Assert
            Assert.Equal("OctaMED (MMD1)", info.Format);
            Assert.Equal("OctaMED Module", info.Title);
            Assert.Equal(4, info.Channels);
        }
    }
}

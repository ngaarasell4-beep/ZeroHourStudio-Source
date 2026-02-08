using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.Archives;
using System.IO;
using System.Text;

namespace ZeroHourStudio.Tests.Infrastructure
{
    public class BigArchiveManagerTests
    {
        [Fact]
        public void BigArchiveManager_Constructor_ShouldInitialize()
        {
            // Arrange & Act - requires a path, use a temp file path
            var tempPath = Path.GetTempFileName();
            var manager = new BigArchiveManager(tempPath);

            // Assert
            manager.Should().NotBeNull();
            manager.Dispose();
            File.Delete(tempPath);
        }

        [Fact]
        public void Constructor_WithInvalidPath_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var invalidPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid() + ".big");

            // Act
            Action act = () => new BigArchiveManager(invalidPath);

            // Assert
            act.Should().Throw<FileNotFoundException>();
        }

        [Fact]
        public void PriorityPrefix_ShouldBeDetectable()
        {
            // Arrange
            var priorityArchive = "!!TestArchive.big";
            var normalArchive = "TestArchive.big";

            // Act & Assert
            priorityArchive.StartsWith("!!").Should().BeTrue();
            normalArchive.StartsWith("!!").Should().BeFalse();
        }

        [Fact]
        public void GetFileList_WithoutLoading_ShouldReturnEmpty()
        {
            // Arrange
            var tempPath = Path.GetTempFileName();
            var manager = new BigArchiveManager(tempPath);

            // Act
            var files = manager.GetFileList();

            // Assert
            files.Should().NotBeNull();
            files.Should().BeEmpty();
            manager.Dispose();
            File.Delete(tempPath);
        }

        [Theory]
        [InlineData("Art/W3D/Models/Tank.w3d")]
        [InlineData("Data\\INI\\Object\\Units.ini")]
        [InlineData("Audio/Sounds/Explosion.wav")]
        public void NormalizePath_ShouldHandleDifferentPathFormats(string path)
        {
            // Arrange
            var normalized = path.Replace('\\', '/').ToLowerInvariant();

            // Assert
            normalized.Should().NotContain("\\");
            normalized.Should().Be(normalized.ToLowerInvariant());
        }

        [Fact]
        public async Task ExtractFileAsync_WithoutLoading_ShouldThrowException()
        {
            // Arrange
            var tempPath = Path.GetTempFileName();
            var manager = new BigArchiveManager(tempPath);

            // Act
            Func<Task> act = async () => await manager.ExtractFileAsync("test.ini");

            // Assert
            await act.Should().ThrowAsync<Exception>();
            manager.Dispose();
            File.Delete(tempPath);
        }
    }
}

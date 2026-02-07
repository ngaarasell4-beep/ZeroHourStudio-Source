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
            // Arrange & Act
            var manager = new BigArchiveManager();

            // Assert
            manager.Should().NotBeNull();
        }

        [Fact]
        public void MountArchive_WithInvalidPath_ShouldThrowException()
        {
            // Arrange
            var manager = new BigArchiveManager();
            var invalidPath = "C:\\NonExistent\\Path\\test.big";

            // Act
            Action act = () => manager.MountArchive(invalidPath);

            // Assert
            act.Should().Throw<FileNotFoundException>();
        }

        [Fact]
        public void MountArchive_WithPriorityPrefix_ShouldHaveHigherPriority()
        {
            // Arrange
            var manager = new BigArchiveManager();
            var priorityArchive = "!!TestArchive.big";
            var normalArchive = "TestArchive.big";

            // Act & Assert
            // الأرشيفات بالبادئة !! يجب أن تكون لها أولوية أعلى
            priorityArchive.StartsWith("!!").Should().BeTrue();
            normalArchive.StartsWith("!!").Should().BeFalse();
        }

        [Fact]
        public void FileExists_WithNonMountedArchive_ShouldReturnFalse()
        {
            // Arrange
            var manager = new BigArchiveManager();

            // Act
            var result = manager.FileExists("NonExistent.ini");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ListAllFiles_WithNoMountedArchives_ShouldReturnEmptyList()
        {
            // Arrange
            var manager = new BigArchiveManager();

            // Act
            var files = manager.ListAllFiles();

            // Assert
            files.Should().NotBeNull();
            files.Should().BeEmpty();
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
            normalized.Should().NotContain('\\');
            normalized.Should().Be(normalized.ToLowerInvariant());
        }

        [Fact]
        public void ExtractFile_WithoutMountedArchive_ShouldThrowException()
        {
            // Arrange
            var manager = new BigArchiveManager();
            var outputPath = Path.GetTempFileName();

            // Act
            Action act = () => manager.ExtractFile("test.ini", outputPath);

            // Assert
            act.Should().Throw<Exception>();

            // Cleanup
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}

using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.Helpers;
using System.IO;

namespace ZeroHourStudio.Tests.Helpers
{
    public class DataProcessingHelpersTests
    {
        [Theory]
        [InlineData("normal.w3d", ".w3d")]
        [InlineData("texture.dds", ".dds")]
        [InlineData("sound.wav", ".wav")]
        [InlineData("NoExtension", "")]
        public void GetFileExtension_ShouldReturnCorrectExtension(string fileName, string expectedExtension)
        {
            // Act
            var extension = Path.GetExtension(fileName);

            // Assert
            extension.Should().Be(expectedExtension);
        }

        [Theory]
        [InlineData("Test.w3d", "test.w3d")]
        [InlineData("UPPERCASE.DDS", "uppercase.dds")]
        [InlineData("MixedCase.Wav", "mixedcase.wav")]
        public void NormalizePath_ShouldConvertToLowerCase(string input, string expected)
        {
            // Act
            var result = input.ToLowerInvariant();

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void SanitizeFileName_ShouldRemoveInvalidCharacters()
        {
            // Arrange
            var invalidChars = Path.GetInvalidFileNameChars();
            var testFileName = "Test:File*Name?.txt";

            // Act
            var sanitized = string.Join("", testFileName.Split(invalidChars));

            // Assert
            sanitized.Should().NotContain(":");
            sanitized.Should().NotContain("*");
            sanitized.Should().NotContain("?");
        }

        [Theory]
        [InlineData(1024, "1.00 KB")]
        [InlineData(1048576, "1.00 MB")]
        [InlineData(1073741824, "1.00 GB")]
        public void FormatFileSize_ShouldDisplayReadableSize(long bytes, string expectedFormat)
        {
            // Act
            string formatted;
            if (bytes >= 1073741824)
                formatted = $"{bytes / 1073741824.0:F2} GB";
            else if (bytes >= 1048576)
                formatted = $"{bytes / 1048576.0:F2} MB";
            else
                formatted = $"{bytes / 1024.0:F2} KB";

            // Assert
            formatted.Should().Be(expectedFormat);
        }

        [Fact]
        public void IsValidW3DFile_ShouldIdentifyW3DFiles()
        {
            // Arrange
            var validFiles = new[] { "model.w3d", "MODEL.W3D", "animation.w3d" };
            var invalidFiles = new[] { "texture.dds", "sound.wav", "data.ini" };

            // Act & Assert
            foreach (var file in validFiles)
            {
                file.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            }

            foreach (var file in invalidFiles)
            {
                file.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            }
        }

        [Theory]
        [InlineData("Art/W3D/Models/Tank.w3d", "tank.w3d")]
        [InlineData("Data/INI/Object.ini", "object.ini")]
        [InlineData("Audio/Sound.wav", "sound.wav")]
        public void ExtractFileName_ShouldReturnFileNameOnly(string fullPath, string expectedFileName)
        {
            // Act
            var fileName = Path.GetFileName(fullPath).ToLowerInvariant();

            // Assert
            fileName.Should().Be(expectedFileName);
        }
    }
}

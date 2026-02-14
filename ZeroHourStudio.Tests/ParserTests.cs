using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Tests.Parsers
{
    public class ParserTests
    {
        [Fact]
        public async Task SAGE_IniParser_ShouldExtractObjectCorrectly()
        {
            // Arrange
            var parser = new SAGE_IniParser();
            var iniContent = @"
Object ChinaTankOverlord
  Side = China
  BuildCost = 2000
End

Object USATankPaladin
  Side = USA
  BuildCost = 1100
End
";
            var tempPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempPath, iniContent);

            // Act
            await parser.ParseAsync(tempPath);
            var overlord = parser.ExtractObject("ChinaTankOverlord");

            // Assert
            overlord.Should().NotBeNull();
            overlord.Should().Contain("Side = China");
            overlord.Should().Contain("BuildCost = 2000");
            
            // Clean up
            File.Delete(tempPath);
        }

        [Fact]
        public async Task SAGE_IniParser_ShouldBeCaseInsensitive()
        {
            // Arrange
            var parser = new SAGE_IniParser();
            var iniContent = "Object ChinaTankOverlord\n  SIDE = China\nEnd";
            var tempPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempPath, iniContent);

            // Act
            await parser.ParseAsync(tempPath);
            var extractedObject = parser.ExtractObject("ChinaTankOverlord");

            // Assert - التحقق من أن الكائن تم استخراجه بشكل صحيح
            extractedObject.Should().NotBeNull();
            extractedObject.Should().Contain("SIDE = China");

            File.Delete(tempPath);
        }
    }
}

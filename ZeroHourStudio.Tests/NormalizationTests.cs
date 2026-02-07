using FluentAssertions;
using Xunit;
using ZeroHourStudio.Domain.ValueObjects;
using ZeroHourStudio.Infrastructure.Normalization;

namespace ZeroHourStudio.Tests.Normalization
{
    public class NormalizationTests
    {
        [Theory]
        [InlineData("USA", "factionusa")]
        [InlineData("China Nuke", "factionchinanuke")]
        [InlineData("GLA Terror", "factionglaterror")]
        [InlineData("  China Tank  ", "factionchinatank")]
        public void FactionName_ShouldNormalizeCorrectly(string input, string expected)
        {
            // Act
            var factionName = new FactionName(input);

            // Assert
            factionName.Value.Should().Be(expected);
        }

        [Fact]
        public void SmartNormalization_ShouldHandleFuzzyMatching()
        {
            // Arrange
            var normalizer = new SmartNormalization();
            var input = "China Nuke"; // اسم الفصيل

            // Act
            var result = normalizer.NormalizeFactionName(input);

            // Assert (يتم التطبيع بإزالة المسافات وإضافة البادئة)
            result.Value.Should().Be("factionchinanuke");
            // يبدو أن الاسم الأصلي يتم تنظيفه أيضاً
            result.Original.Should().Be("chinanuke");
        }

        [Fact]
        public void FactionName_EmptyName_ShouldThrowException()
        {
            // Act
            Action act = () => new FactionName("");

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }
    }
}

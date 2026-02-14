using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.Caching;
using System.Threading;

namespace ZeroHourStudio.Tests.Infrastructure
{
    public class CacheManagerTests
    {
        [Fact]
        public void CacheString_ShouldStoreValue()
        {
            // Arrange
            var cache = new CacheManager();
            var key = "test_key";
            var value = "test_value";

            // Act
            cache.CacheString(key, value);

            // Assert
            var result = cache.GetCachedString(key);
            result.Should().Be(value);
        }

        [Fact]
        public void GetCachedString_WithNonExistentKey_ShouldReturnNull()
        {
            // Arrange
            var cache = new CacheManager();

            // Act
            var result = cache.GetCachedString("non_existent");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void CacheString_WithShortExpiration_ShouldExpireAfterTime()
        {
            // Arrange
            var cache = new CacheManager(TimeSpan.FromMilliseconds(100));
            var key = "expiring_key";
            var value = "expiring_value";

            // Act
            cache.CacheString(key, value);
            Thread.Sleep(150); // انتظر حتى تنتهي الصلاحية

            // Assert
            cache.GetCachedString(key).Should().BeNull();
        }

        [Fact]
        public void Clear_ShouldRemoveAllEntries()
        {
            // Arrange
            var cache = new CacheManager();
            cache.CacheString("key1", "value1");
            cache.CacheString("key2", "value2");
            cache.CacheString("key3", "value3");

            // Act
            cache.Clear();

            // Assert
            cache.GetCachedString("key1").Should().BeNull();
            cache.GetCachedString("key2").Should().BeNull();
            cache.GetCachedString("key3").Should().BeNull();
        }

        [Fact]
        public void CacheFile_ShouldStoreAndRetrieveBytes()
        {
            // Arrange
            var cache = new CacheManager();
            var key = "file_key";
            var value = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            cache.CacheFile(key, value);

            // Assert
            var result = cache.GetCachedFile(key);
            result.Should().NotBeNull();
            result.Should().BeEquivalentTo(value);
        }

        [Fact]
        public void HasCachedString_ShouldReturnCorrectStatus()
        {
            // Arrange
            var cache = new CacheManager();
            cache.CacheString("exists", "value");

            // Act & Assert
            cache.HasCachedString("exists").Should().BeTrue();
            cache.HasCachedString("missing").Should().BeFalse();
        }

        [Theory]
        [InlineData("cache_key_1", "value_100")]
        [InlineData("cache_key_2", "value_200")]
        [InlineData("cache_key_3", "value_300")]
        public void CacheString_MultipleTimes_ShouldOverwritePrevious(string key, string value)
        {
            // Arrange
            var cache = new CacheManager();

            // Act
            cache.CacheString(key, "old_value");
            cache.CacheString(key, value);

            // Assert
            var result = cache.GetCachedString(key);
            result.Should().Be(value);
        }
    }
}

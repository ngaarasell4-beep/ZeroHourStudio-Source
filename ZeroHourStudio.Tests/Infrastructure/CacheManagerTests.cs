using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.Caching;
using System.Threading;

namespace ZeroHourStudio.Tests.Infrastructure
{
    public class CacheManagerTests
    {
        [Fact]
        public void Add_ShouldStoreValue()
        {
            // Arrange
            var cache = new CacheManager();
            var key = "test_key";
            var value = "test_value";

            // Act
            cache.Add(key, value, TimeSpan.FromMinutes(5));

            // Assert
            cache.TryGet(key, out string? result).Should().BeTrue();
            result.Should().Be(value);
        }

        [Fact]
        public void TryGet_WithNonExistentKey_ShouldReturnFalse()
        {
            // Arrange
            var cache = new CacheManager();

            // Act
            var exists = cache.TryGet("non_existent", out string? result);

            // Assert
            exists.Should().BeFalse();
            result.Should().BeNull();
        }

        [Fact]
        public void Add_WithExpiration_ShouldExpireAfterTime()
        {
            // Arrange
            var cache = new CacheManager();
            var key = "expiring_key";
            var value = "expiring_value";

            // Act
            cache.Add(key, value, TimeSpan.FromMilliseconds(100));
            Thread.Sleep(150); // انتظر حتى تنتهي الصلاحية

            // Assert
            cache.TryGet(key, out string? result).Should().BeFalse();
        }

        [Fact]
        public void Remove_ShouldDeleteKey()
        {
            // Arrange
            var cache = new CacheManager();
            var key = "removable_key";
            var value = "removable_value";
            cache.Add(key, value, TimeSpan.FromMinutes(5));

            // Act
            cache.Remove(key);

            // Assert
            cache.TryGet(key, out string? result).Should().BeFalse();
        }

        [Fact]
        public void Clear_ShouldRemoveAllEntries()
        {
            // Arrange
            var cache = new CacheManager();
            cache.Add("key1", "value1", TimeSpan.FromMinutes(5));
            cache.Add("key2", "value2", TimeSpan.FromMinutes(5));
            cache.Add("key3", "value3", TimeSpan.FromMinutes(5));

            // Act
            cache.Clear();

            // Assert
            cache.TryGet("key1", out string? result1).Should().BeFalse();
            cache.TryGet("key2", out string? result2).Should().BeFalse();
            cache.TryGet("key3", out string? result3).Should().BeFalse();
        }

        [Fact]
        public void Add_WithComplexObject_ShouldStoreAndRetrieve()
        {
            // Arrange
            var cache = new CacheManager();
            var key = "complex_object";
            var value = new TestComplexObject
            {
                Id = 123,
                Name = "Test Object",
                Items = new List<string> { "Item1", "Item2", "Item3" }
            };

            // Act
            cache.Add(key, value, TimeSpan.FromMinutes(5));

            // Assert
            cache.TryGet(key, out TestComplexObject? result).Should().BeTrue();
            result.Should().NotBeNull();
            result!.Id.Should().Be(123);
            result.Name.Should().Be("Test Object");
            result.Items.Should().HaveCount(3);
        }

        [Theory]
        [InlineData("cache_key_1", 100)]
        [InlineData("cache_key_2", 200)]
        [InlineData("cache_key_3", 300)]
        public void Add_MultipleTimes_ShouldOverwritePrevious(string key, int value)
        {
            // Arrange
            var cache = new CacheManager();

            // Act
            cache.Add(key, value - 10, TimeSpan.FromMinutes(5));
            cache.Add(key, value, TimeSpan.FromMinutes(5)); // الكتابة فوق القيمة السابقة

            // Assert
            cache.TryGet(key, out int result).Should().BeTrue();
            result.Should().Be(value);
        }

        private class TestComplexObject
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<string> Items { get; set; } = new();
        }
    }
}

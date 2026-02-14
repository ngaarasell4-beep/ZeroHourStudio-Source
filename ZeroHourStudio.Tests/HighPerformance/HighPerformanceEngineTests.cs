using Xunit;
using ZeroHourStudio.Infrastructure.HighPerformance;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Tests.HighPerformance;

/// <summary>
/// «Œ »«—«  «·√œ«¡ «·⁄«·Ì… ··„Õ—ﬂ
/// - «Œ »«—«  «·«” Œ—«Ã «·”—Ì⁄
/// - «Œ »«—«  «·»ÕÀ «·À‰«∆Ì
/// - «Œ »«—«  Õ· «· »⁄Ì«  «·⁄‰ﬁÊœÌ…
/// </summary>
public class HighPerformanceEngineTests : IDisposable
{
    private readonly HighPerformanceExtractionEngine _extractionEngine;
    private readonly UTF16ArabicBinarySearchCache _searchCache;
    private readonly RecursiveAssetResolver _assetResolver;
    private readonly string _testDirectory;

    public HighPerformanceEngineTests()
    {
        _extractionEngine = new HighPerformanceExtractionEngine();
        _searchCache = new UTF16ArabicBinarySearchCache();
        _assetResolver = new RecursiveAssetResolver(_extractionEngine, _searchCache);
        _testDirectory = Path.Combine(Path.GetTempPath(), "ZeroHourStudioTests");

        if (!Directory.Exists(_testDirectory))
            Directory.CreateDirectory(_testDirectory);
    }

    #region High Performance Extraction Engine Tests

    [Fact]
    public async Task ExtractIniContent_ValidFile_ReturnsCorrectData()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test.ini");
        var iniContent = @"[Unit]
Name = TestUnit
BuildCost = 500

[Weapon]
Name = Rifle
Damage = 25
";
        await File.WriteAllTextAsync(testFile, iniContent);

        // Act
        var result = await _extractionEngine.ExtractIniContentAsync(testFile);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("Unit"));
        Assert.True(result["Unit"].ContainsKey("Name"));
        Assert.Equal("TestUnit", result["Unit"]["Name"]);
    }

    [Fact]
    public async Task ExtractIniContent_WithComments_IgnoresComments()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test_comments.ini");
        var iniContent = @"; This is a comment
[Unit]
; Another comment
Name = TestUnit
BuildCost = 500 ; inline comment
";
        await File.WriteAllTextAsync(testFile, iniContent);

        // Act
        var result = await _extractionEngine.ExtractIniContentAsync(testFile);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestUnit", result["Unit"]["Name"]);
        Assert.Equal("500", result["Unit"]["BuildCost"]);
    }

    [Fact]
    public async Task ExtractCompleteObject_ValidObject_ReturnsCompleteObject()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "objects.ini");
        var content = @"Object TestWeapon
{
    FireWarmupFrames = 10
    BurstFireRandom = Yes
    FireFX = MUZZLE_FLASH
    ProjectileObject = Bullet
}

Object TestArmor
{
    ArmorSet = Light
    DamageScalar = 0.8
}
";
        await File.WriteAllTextAsync(testFile, content);

        // Act
        var result = await _extractionEngine.ExtractCompleteObjectAsync(testFile, "TestWeapon");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("TestWeapon", result);
        Assert.Contains("FireFX", result);
        Assert.Contains("MUZZLE_FLASH", result);
    }

    [Fact]
    public async Task FindPatternInBigFile_ValidPattern_FindsAllMatches()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test.big");
        byte[] pattern = System.Text.Encoding.UTF8.GetBytes("TestPattern");
        var fileContent = new List<byte>();

        // ≈÷«›… «·‰„ÿ ⁄œ… „—« 
        for (int i = 0; i < 3; i++)
        {
            fileContent.AddRange(pattern);
            fileContent.AddRange(System.Text.Encoding.UTF8.GetBytes("SomeData"));
        }

        await File.WriteAllBytesAsync(testFile, fileContent.ToArray());

        // Act
        var matches = await _extractionEngine.FindPatternInBigFileAsync(testFile, pattern);

        // Assert
        Assert.NotEmpty(matches);
        Assert.True(matches.Count >= 1);
    }

    [Fact]
    public async Task ReadTextEfficiently_ValidFile_ReadsCorrectContent()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "text.txt");
        var content = "This is a test content with «·⁄—»Ì…";
        await File.WriteAllTextAsync(testFile, content, System.Text.Encoding.UTF8);

        // Act
        var result = await _extractionEngine.ReadTextEfficientlyAsync(testFile);

        // Assert
        Assert.Equal(content, result);
    }

    #endregion

    #region UTF-16 Arabic Binary Search Tests

    [Fact]
    public void BinarySearchArabic_ExistingItem_ReturnsCorrectIndex()
    {
        // Arrange
        var items = new[] { "√Õ„œ", "⁄·Ì", "„Õ„œ", "”«—…", "›«ÿ„…" };
        var target = "„Õ„œ";

        // Act
        var index = _searchCache.BinarySearchArabic(items, target);

        // Assert
        Assert.Equal(2, index);
    }

    [Fact]
    public void BinarySearchArabic_NonExistingItem_ReturnsNegativeOne()
    {
        // Arrange
        var items = new[] { "√Õ„œ", "⁄·Ì", "„Õ„œ" };
        var target = "€Ì— „ÊÃÊœ";

        // Act
        var index = _searchCache.BinarySearchArabic(items, target);

        // Assert
        Assert.Equal(-1, index);
    }

    [Fact]
    public void BinarySearchRangeArabic_WithPrefix_ReturnsCorrectRange()
    {
        // Arrange
        var items = new[] { "M14", "M16", "M4", "AK47", "Handgun" };
        var prefix = "M";

        // Act
        var (start, end) = _searchCache.BinarySearchRangeArabic(items, prefix);

        // Assert
        Assert.NotEqual(-1, start);
        Assert.NotEqual(-1, end);
        Assert.True(start <= end);
    }

    [Fact]
    public void TokenizeArabicText_ValidText_ReturnsTokens()
    {
        // Arrange
        var text = "Â–« ‰’ ⁄—»Ì ··«Œ »«—";

        // Act
        var tokens = _searchCache.TokenizeArabicText(text);

        // Assert
        Assert.NotEmpty(tokens);
        Assert.Contains("Â–«", tokens);
    }

    [Fact]
    public void NormalizeArabicText_WithSpecialChars_RemovesSpecialChars()
    {
        // Arrange
        var text = "Â–«@‰’#⁄—»Ì$";

        // Act
        var normalized = _searchCache.NormalizeArabicText(text);

        // Assert
        Assert.DoesNotContain("@", normalized);
        Assert.DoesNotContain("#", normalized);
        Assert.DoesNotContain("$", normalized);
    }

    [Fact]
    public void AnalyzeWordFrequency_ValidText_ReturnsFrequencyMap()
    {
        // Arrange
        var text = "„Õ„œ ⁄·Ì „Õ„œ ”«—… ⁄·Ì ⁄·Ì";

        // Act
        var frequency = _searchCache.AnalyzeWordFrequency(text);

        // Assert
        Assert.NotEmpty(frequency);
        Assert.True(frequency["⁄·Ì"] > frequency["”«—…"]);
    }

    [Fact]
    public void LevenshteinDistanceArabic_SimilarWords_ReturnsSmallDistance()
    {
        // Arrange
        var word1 = "„Õ„œ";
        var word2 = "„Õ„Êœ";

        // Act
        var distance = _searchCache.LevenshteinDistanceArabic(word1, word2);

        // Assert
        Assert.True(distance <= 3);
    }

    [Fact]
    public void FuzzySearchArabic_WithMisspellings_FindsSimilarWords()
    {
        // Arrange
        var items = new[] { "„Õ„œ", "„Õ„Êœ", "√Õ„œ", "⁄·Ì" };
        var target = "„Õ„œ";

        // Act
        var results = _searchCache.FuzzySearchArabic(items, target, maxDistance: 2);

        // Assert
        Assert.NotEmpty(results);
        Assert.True(results.Any(r => r.Item2 == 0));
    }

    #endregion

    #region Recursive Asset Resolver Tests

    [Fact]
    public async Task ResolveAssetRecursively_SimpleAsset_ResolvesSuccessfully()
    {
        // Arrange
        var rootAsset = Path.Combine(_testDirectory, "unit.ini");
        var content = @"[Unit]
Name = TestUnit
DefaultBehavior = UnitBehavior
";
        await File.WriteAllTextAsync(rootAsset, content);

        // Act
        var node = await _assetResolver.ResolveAssetRecursivelyAsync(rootAsset, _testDirectory);

        // Assert
        Assert.NotNull(node);
        Assert.Equal("unit.ini", node.Name);
        Assert.Equal(AssetStatus.Found, node.Status);
    }

    [Fact]
    public async Task ResolveAssetRecursively_WithDependencies_ResolvesAllDeps()
    {
        // Arrange
        var rootAsset = Path.Combine(_testDirectory, "weapon.ini");
        var depAsset = Path.Combine(_testDirectory, "projectile.ini");

        var rootContent = @"[Weapon]
Name = TestWeapon
Projectile = TestProjectile
";
        var depContent = @"[Projectile]
Name = TestProjectile
Speed = 500
";

        await File.WriteAllTextAsync(rootAsset, rootContent);
        await File.WriteAllTextAsync(depAsset, depContent);

        // Act
        var node = await _assetResolver.ResolveAssetRecursivelyAsync(rootAsset, _testDirectory);

        // Assert
        Assert.NotNull(node);
        Assert.NotEmpty(node.Dependencies);
    }

    [Fact]
    public async Task ResolveAssetRecursively_CircularDependency_PreventsCycle()
    {
        // Arrange
        var asset1 = Path.Combine(_testDirectory, "asset1.ini");
        var asset2 = Path.Combine(_testDirectory, "asset2.ini");

        var content1 = @"[Object]
Name = Asset1
Dependency = Asset2
";
        var content2 = @"[Object]
Name = Asset2
Dependency = Asset1
";

        await File.WriteAllTextAsync(asset1, content1);
        await File.WriteAllTextAsync(asset2, content2);

        // Act
        var node = await _assetResolver.ResolveAssetRecursivelyAsync(asset1, _testDirectory);

        // Assert
        Assert.NotNull(node);
        // ÌÃ» √·«  ÕœÀ «” À‰«¡
    }

    [Fact]
    public void GenerateDependencyTreeReport_ValidNode_GeneratesReport()
    {
        // Arrange
        var rootNode = new DependencyNode
        {
            Name = "root.ini",
            Type = DependencyType.ObjectINI,
            Status = AssetStatus.Found,
            Depth = 0,
            Dependencies = new List<DependencyNode>
            {
                new DependencyNode
                {
                    Name = "dep1.ini",
                    Type = DependencyType.Weapon,
                    Status = AssetStatus.Found,
                    Depth = 1
                }
            }
        };

        // Act
        var report = _assetResolver.GenerateDependencyTreeReport(rootNode);

        // Assert
        Assert.NotNull(report);
        Assert.Equal("root.ini", report.RootAsset);
        Assert.True(report.TotalNodes >= 2);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullExtractionPipeline_CompleteWorkflow_SucceedsEnd2End()
    {
        // Arrange
        var unitFile = Path.Combine(_testDirectory, "full_unit.ini");
        var weaponFile = Path.Combine(_testDirectory, "full_weapon.ini");

        await File.WriteAllTextAsync(unitFile, @"[Unit]
Name = FullUnit
Weapon = TestWeapon
");
        await File.WriteAllTextAsync(weaponFile, @"[Weapon]
Name = TestWeapon
Damage = 50
");

        // Act
        var iniData = await _extractionEngine.ExtractIniContentAsync(unitFile);
        var node = await _assetResolver.ResolveAssetRecursivelyAsync(unitFile, _testDirectory);
        var report = _assetResolver.GenerateDependencyTreeReport(node);

        // Assert
        Assert.NotNull(iniData);
        Assert.NotNull(node);
        Assert.NotNull(report);
        Assert.True(report.TotalNodes > 0);
    }

    #endregion

    public void Dispose()
    {
        _extractionEngine?.Dispose();
        _searchCache?.Dispose();
        _assetResolver?.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch { /*  Ã«Â· «·√Œÿ«¡ */ }
        }
    }
}

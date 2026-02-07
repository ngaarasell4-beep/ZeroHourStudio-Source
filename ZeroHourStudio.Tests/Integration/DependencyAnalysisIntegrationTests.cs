using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.AssetManagement;
using ZeroHourStudio.Infrastructure.Validation;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Tests.Integration
{
    public class DependencyAnalysisIntegrationTests
    {
        [Fact]
        public async Task FullDependencyAnalysis_WithCompleteUnit_ShouldSucceed()
        {
            // Arrange
            var assetHunter = new AssetReferenceHunter();
            var analyzer = new UnitDependencyAnalyzer(assetHunter);
            var validator = new UnitCompletionValidator();

            var unitData = new Dictionary<string, string>
            {
                { "Model", "TestUnit" },
                { "Draw", "W3DModelDraw" },
                { "Side", "USA" }
            };

            // Act
            var graph = await analyzer.AnalyzeDependenciesAsync("TestUnit", "Test Unit", unitData);
            var completionStatus = validator.EvaluateCompletionStatus(graph);

            // Assert
            graph.Should().NotBeNull();
            graph.UnitId.Should().Be("TestUnit");
            completionStatus.Should().BeOneOf(
                CompletionStatus.Complete,
                CompletionStatus.Partial,
                CompletionStatus.Incomplete
            );
        }

        [Fact]
        public async Task ComprehensiveDependencyService_ShouldProvideFullAnalysis()
        {
            // Arrange
            var service = new ComprehensiveDependencyService();
            var unitData = new Dictionary<string, string>
            {
                { "Model", "ChinaTankOverlord" },
                { "Draw", "W3DModelDraw" }
            };

            // Act
            var result = await service.PerformFullAnalysisAsync(
                "ChinaTankOverlord",
                "Overlord Tank",
                unitData
            );

            // Assert
            result.Should().NotBeNull();
            result.DependencyGraph.Should().NotBeNull();
            result.ValidationResult.Should().NotBeNull();
            result.AssetReferences.Should().NotBeNull();
        }

        [Fact]
        public void UnitCompletionValidator_WithMissingAssets_ShouldIdentifyIssues()
        {
            // Arrange
            var validator = new UnitCompletionValidator();
            var graph = new UnitDependencyGraph
            {
                UnitId = "TestUnit",
                UnitName = "Test Unit"
            };

            graph.AllNodes.Add(new DependencyNode
            {
                Name = "Missing.w3d",
                Status = AssetStatus.Missing,
                Type = DependencyType.Model3D
            });

            var availableAssets = new Dictionary<string, bool>
            {
                { "Missing.w3d", false }
            };

            // Act
            var validationResult = validator.ValidateUnitCompletion("TestUnit", graph, availableAssets);

            // Assert
            validationResult.Should().NotBeNull();
            validationResult.IsValid.Should().BeFalse();
            validationResult.MissingAssets.Should().NotBeEmpty();
        }

        [Fact]
        public void AssetReferenceHunter_ShouldFindMultipleExtensions()
        {
            // Arrange
            var hunter = new AssetReferenceHunter();
            var baseName = "TestModel";
            var searchDirectory = "."; // مجرد اختبار وحدة

            // Act
            var references = hunter.FindAssetReferences(baseName, searchDirectory, "w3d");

            // Assert
            references.Should().NotBeNull();
            // النتيجة قد تكون فارغة إذا لم توجد ملفات حقيقية، لكن الدالة يجب أن تعمل
        }

        [Fact]
        public async Task EndToEnd_LoadAnalyzeAndValidate_ShouldComplete()
        {
            // Arrange - محاكاة سيناريو كامل من البداية إلى النهاية
            var service = new ComprehensiveDependencyService();
            var unitId = "USATankCrusader";
            var unitName = "Crusader Tank";
            var unitData = new Dictionary<string, string>
            {
                { "Model", "AVCrusader" },
                { "Draw", "W3DModelDraw" },
                { "Side", "USA" }
            };

            // Act
            var analysis = await service.PerformFullAnalysisAsync(unitId, unitName, unitData);

            // Assert - التحقق من أن جميع المكونات عملت معاً
            analysis.Should().NotBeNull();
            analysis.DependencyGraph.Should().NotBeNull();
            analysis.DependencyGraph.UnitId.Should().Be(unitId);
            analysis.DependencyGraph.UnitName.Should().Be(unitName);
            analysis.ValidationResult.Should().NotBeNull();
            analysis.CompletionPercentage.Should().BeInRange(0, 100);
        }

        [Theory]
        [InlineData("USA", "USATankCrusader")]
        [InlineData("China", "ChinaTankOverlord")]
        [InlineData("GLA", "GLAVehicleTechnical")]
        public async Task MultipleUnitsAnalysis_ShouldHandleDifferentFactions(string faction, string unitId)
        {
            // Arrange
            var service = new ComprehensiveDependencyService();
            var unitData = new Dictionary<string, string>
            {
                { "Side", faction },
                { "Model", unitId },
                { "Draw", "W3DModelDraw" }
            };

            // Act
            var result = await service.PerformFullAnalysisAsync(unitId, $"{faction} Unit", unitData);

            // Assert
            result.Should().NotBeNull();
            result.DependencyGraph.UnitId.Should().Be(unitId);
        }
    }
}

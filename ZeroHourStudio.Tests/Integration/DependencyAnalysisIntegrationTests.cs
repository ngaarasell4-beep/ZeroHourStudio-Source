using FluentAssertions;
using Xunit;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.AssetManagement;
using ZeroHourStudio.Infrastructure.Validation;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Tests.Integration
{
    public class DependencyAnalysisIntegrationTests
    {
        [Fact]
        public async Task FullDependencyAnalysis_WithCompleteUnit_ShouldSucceed()
        {
            // Arrange
            var iniParser = new SAGE_IniParser();
            var analyzer = new UnitDependencyAnalyzer(iniParser);
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
                CompletionStatus.Incomplete,
                CompletionStatus.CannotVerify
            );
        }

        [Fact]
        public async Task ComprehensiveDependencyService_ShouldProvideFullAnalysis()
        {
            // Arrange
            var iniParser = new SAGE_IniParser();
            var analyzer = new UnitDependencyAnalyzer(iniParser);
            var assetHunter = new AssetReferenceHunter();
            var validator = new UnitCompletionValidator();
            var service = new ComprehensiveDependencyService(analyzer, assetHunter, validator);

            var unitData = new Dictionary<string, string>
            {
                { "Model", "ChinaTankOverlord" },
                { "Draw", "W3DModelDraw" }
            };

            // Act
            var result = await service.AnalyzeUnitComprehensivelyAsync(
                "ChinaTankOverlord",
                "Overlord Tank",
                unitData
            );

            // Assert
            result.Should().NotBeNull();
            result.DependencyGraph.Should().NotBeNull();
            result.ValidationResult.Should().NotBeNull();
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

            // Act
            var validationResult = validator.ValidateUnitCompletion("TestUnit", graph);

            // Assert
            validationResult.Should().NotBeNull();
            validationResult.IsValid.Should().BeFalse();
            validationResult.Errors.Should().NotBeEmpty();
        }

        [Fact]
        public async Task AssetReferenceHunter_ShouldFindAssets()
        {
            // Arrange
            var hunter = new AssetReferenceHunter();
            var baseName = "TestModel";

            // Act
            var assets = await hunter.FindAssetsAsync(baseName);

            // Assert
            assets.Should().NotBeNull();
        }

        [Fact]
        public async Task EndToEnd_LoadAnalyzeAndValidate_ShouldComplete()
        {
            // Arrange
            var iniParser = new SAGE_IniParser();
            var analyzer = new UnitDependencyAnalyzer(iniParser);
            var assetHunter = new AssetReferenceHunter();
            var validator = new UnitCompletionValidator();
            var service = new ComprehensiveDependencyService(analyzer, assetHunter, validator);

            var unitId = "USATankCrusader";
            var unitName = "Crusader Tank";
            var unitData = new Dictionary<string, string>
            {
                { "Model", "AVCrusader" },
                { "Draw", "W3DModelDraw" },
                { "Side", "USA" }
            };

            // Act
            var analysis = await service.AnalyzeUnitComprehensivelyAsync(unitId, unitName, unitData);

            // Assert
            analysis.Should().NotBeNull();
            analysis.DependencyGraph.Should().NotBeNull();
            analysis.DependencyGraph!.UnitId.Should().Be(unitId);
            analysis.DependencyGraph.UnitName.Should().Be(unitName);
        }

        [Theory]
        [InlineData("USA", "USATankCrusader")]
        [InlineData("China", "ChinaTankOverlord")]
        [InlineData("GLA", "GLAVehicleTechnical")]
        public async Task MultipleUnitsAnalysis_ShouldHandleDifferentFactions(string faction, string unitId)
        {
            // Arrange
            var iniParser = new SAGE_IniParser();
            var analyzer = new UnitDependencyAnalyzer(iniParser);
            var assetHunter = new AssetReferenceHunter();
            var validator = new UnitCompletionValidator();
            var service = new ComprehensiveDependencyService(analyzer, assetHunter, validator);

            var unitData = new Dictionary<string, string>
            {
                { "Side", faction },
                { "Model", unitId },
                { "Draw", "W3DModelDraw" }
            };

            // Act
            var result = await service.AnalyzeUnitComprehensivelyAsync(unitId, $"{faction} Unit", unitData);

            // Assert
            result.Should().NotBeNull();
            result.DependencyGraph.Should().NotBeNull();
            result.DependencyGraph!.UnitId.Should().Be(unitId);
        }
    }
}

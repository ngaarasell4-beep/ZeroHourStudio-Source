using FluentAssertions;
using Moq;
using Xunit;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.AssetManagement;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using System.Collections.Generic;

namespace ZeroHourStudio.Tests.Services
{
    public class DependencyAnalyzerTests
    {
        private readonly SAGE_IniParser _iniParser;
        private readonly UnitDependencyAnalyzer _analyzer;

        public DependencyAnalyzerTests()
        {
            _iniParser = new SAGE_IniParser();
            _analyzer = new UnitDependencyAnalyzer(_iniParser);
        }

        [Fact]
        public async Task AnalyzeDependenciesAsync_WithValidUnit_ShouldReturnGraph()
        {
            // Arrange
            var unitData = new Dictionary<string, string>
            {
                { "Model", "Normal.w3d" },
                { "Draw", "W3DModelDraw" }
            };

            // Act
            var result = await _analyzer.AnalyzeDependenciesAsync("TestUnit", "Test Unit", unitData);

            // Assert
            result.Should().NotBeNull();
            result.UnitId.Should().Be("TestUnit");
            result.UnitName.Should().Be("Test Unit");
            result.AllNodes.Should().NotBeEmpty();
        }

        [Fact]
        public async Task AnalyzeDependenciesAsync_WithoutModel_ShouldHandleGracefully()
        {
            // Arrange
            var unitData = new Dictionary<string, string>
            {
                { "Draw", "W3DModelDraw" }
                // لا يوجد Model!
            };

            // Act
            var result = await _analyzer.AnalyzeDependenciesAsync("TestUnit", "Test Unit", unitData);

            // Assert
            result.Should().NotBeNull();
            result.UnitId.Should().Be("TestUnit");
        }

        [Fact]
        public void DependencyNode_ShouldCalculateSizeCorrectly()
        {
            // Arrange
            var node = new DependencyNode
            {
                Name = "Test.w3d",
                Type = DependencyType.Model3D,
                Status = AssetStatus.Found,
                SizeInBytes = 1024 * 1024 // 1 MB
            };

            // Assert
            node.SizeInBytes.Should().Be(1024 * 1024);
            node.Status.Should().Be(AssetStatus.Found);
        }

        [Fact]
        public void UnitDependencyGraph_ShouldTrackFoundAndMissingCount()
        {
            // Arrange
            var graph = new UnitDependencyGraph
            {
                UnitId = "TestUnit",
                UnitName = "Test Unit"
            };

            graph.AllNodes.Add(new DependencyNode { Status = AssetStatus.Found });
            graph.AllNodes.Add(new DependencyNode { Status = AssetStatus.Found });
            graph.AllNodes.Add(new DependencyNode { Status = AssetStatus.Missing });

            // Act
            graph.FoundCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Found);
            graph.MissingCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Missing);

            // Assert
            graph.FoundCount.Should().Be(2);
            graph.MissingCount.Should().Be(1);
        }

        [Theory]
        [InlineData(DependencyType.Model3D, "model.w3d")]
        [InlineData(DependencyType.Texture, "texture.dds")]
        [InlineData(DependencyType.Audio, "sound.wav")]
        [InlineData(DependencyType.Model3D, "anim.w3d")]
        public void DependencyNode_ShouldHandleDifferentAssetTypes(DependencyType type, string path)
        {
            // Arrange & Act
            var node = new DependencyNode
            {
                Name = path,
                Type = type,
                Status = AssetStatus.Found
            };

            // Assert
            node.Type.Should().Be(type);
            node.Name.Should().Be(path);
        }

        [Fact]
        public void GetCompletionPercentage_AllFound_ShouldReturn100()
        {
            // Arrange
            var graph = new UnitDependencyGraph();
            for (int i = 0; i < 10; i++)
                graph.AllNodes.Add(new DependencyNode { Name = $"f{i}.w3d", Status = AssetStatus.Found });
            graph.FoundCount = 10;
            graph.MissingCount = 0;

            // Act
            double percentage = graph.GetCompletionPercentage();

            // Assert
            percentage.Should().Be(100.0);
        }

        [Fact]
        public void GetCompletionPercentage_HalfMissing_ShouldReturn50()
        {
            // Arrange
            var graph = new UnitDependencyGraph();
            for (int i = 0; i < 5; i++)
                graph.AllNodes.Add(new DependencyNode { Name = $"f{i}.w3d", Status = AssetStatus.Found });
            for (int i = 0; i < 5; i++)
                graph.AllNodes.Add(new DependencyNode { Name = $"m{i}.w3d", Status = AssetStatus.Missing });
            graph.FoundCount = 5;
            graph.MissingCount = 5;

            // Act
            double percentage = graph.GetCompletionPercentage();

            // Assert
            percentage.Should().Be(50.0);
        }

        [Fact]
        public void GetCompletionPercentage_NoAssets_ShouldReturn0()
        {
            // Arrange
            var graph = new UnitDependencyGraph
            {
                FoundCount = 0,
                MissingCount = 0
            };

            // Act
            double percentage = graph.GetCompletionPercentage();

            // Assert
            percentage.Should().Be(0.0);
        }
    }
}

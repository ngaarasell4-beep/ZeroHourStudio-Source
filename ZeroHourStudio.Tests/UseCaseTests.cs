using FluentAssertions;
using Moq;
using Xunit;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.UseCases;
using ZeroHourStudio.Application.Interfaces;

namespace ZeroHourStudio.Tests.UseCases
{
    public class UseCaseTests
    {
        [Fact]
        public async Task AnalyzeDependenciesUseCase_ShouldReturnSuccess()
        {
            // Arrange
            var mockAnalyzer = new Mock<IUnitDependencyAnalyzer>();
            var mockValidator = new Mock<IUnitCompletionValidator>();
            
            mockAnalyzer.Setup(x => x.AnalyzeDependenciesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .ReturnsAsync(new UnitDependencyGraph { UnitId = "TestUnit", UnitName = "Test Unit" });

            mockValidator.Setup(x => x.ValidateUnitCompletion(
                It.IsAny<string>(), It.IsAny<UnitDependencyGraph>(), It.IsAny<Dictionary<string, bool>>()))
                .Returns(new ValidationResult { UnitId = "TestUnit", IsValid = true });

            mockValidator.Setup(x => x.EvaluateCompletionStatus(It.IsAny<UnitDependencyGraph>()))
                .Returns(CompletionStatus.Complete);

            var useCase = new AnalyzeUnitDependenciesUseCase(mockAnalyzer.Object, mockValidator.Object);
            
            var request = new AnalyzeDependenciesRequest
            {
                UnitId = "TestUnit",
                UnitName = "Test Unit",
                UnitData = new Dictionary<string, string> { { "Model", "Normal.w3d" } }
            };

            // Act
            var response = await useCase.ExecuteAsync(request);

            // Assert
            response.Success.Should().BeTrue();
            response.DependencyGraph.Should().NotBeNull();
        }
    }
}

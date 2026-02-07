using FluentAssertions;
using Moq;
using Xunit;
using ZeroHourStudio.UI.WPF.ViewModels;
using ZeroHourStudio.UI.WPF.Models;
using ZeroHourStudio.Application.Interfaces;
using System.Collections.ObjectModel;

namespace ZeroHourStudio.Tests.ViewModels
{
    public class MainViewModelTests
    {
        private readonly MainViewModel _viewModel;

        public MainViewModelTests()
        {
            _viewModel = new MainViewModel();
        }

        [Fact]
        public void SearchText_WhenChanged_ShouldFilterUnits()
        {
            // Arrange
            _viewModel.AllUnits = new ObservableCollection<UnitDisplayModel>
            {
                new UnitDisplayModel { UnitId = "USATankCrusader", UnitName = "Crusader Tank" },
                new UnitDisplayModel { UnitId = "ChinaTankOverlord", UnitName = "Overlord Tank" },
                new UnitDisplayModel { UnitId = "GLAVehicleTechnical", UnitName = "Technical" }
            };

            // Act
            _viewModel.SearchText = "crusader";

            // Assert - يجب أن يظهر فقط الوحدة التي تحتوي على Crusader
            _viewModel.FilteredUnits.Should().HaveCount(1);
            _viewModel.FilteredUnits.First().UnitId.Should().Be("USATankCrusader");
        }

        [Fact]
        public void SearchText_EmptyString_ShouldShowAllUnits()
        {
            // Arrange
            _viewModel.AllUnits = new ObservableCollection<UnitDisplayModel>
            {
                new UnitDisplayModel { UnitId = "Unit1", UnitName = "Test Unit 1" },
                new UnitDisplayModel { UnitId = "Unit2", UnitName = "Test Unit 2" }
            };

            // Act
            _viewModel.SearchText = "";

            // Assert
            _viewModel.FilteredUnits.Should().HaveCount(2);
        }

        [Fact]
        public void IsLoading_WhenSet_ShouldNotifyPropertyChanged()
        {
            // Arrange
            bool propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsLoading))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.IsLoading = true;

            // Assert
            propertyChangedRaised.Should().BeTrue();
            _viewModel.IsLoading.Should().BeTrue();
        }

        [Fact]
        public void SelectedUnit_WhenChanged_ShouldUpdateDependencies()
        {
            // Arrange
            var unit = new UnitDisplayModel
            {
                UnitId = "ChinaTankOverlord",
                UnitName = "Overlord Tank"
            };

            // Act
            _viewModel.SelectedUnit = unit;

            // Assert
            _viewModel.SelectedUnit.Should().NotBeNull();
            _viewModel.SelectedUnit.UnitId.Should().Be("ChinaTankOverlord");
        }

        [Fact]
        public void ProgressPercentage_ShouldBeBetweenZeroAndHundred()
        {
            // Act
            _viewModel.ProgressPercentage = 50;

            // Assert
            _viewModel.ProgressPercentage.Should().BeInRange(0, 100);
        }

        [Fact]
        public void StatusMessage_WhenSet_ShouldNotifyPropertyChanged()
        {
            // Arrange
            bool propertyChangedRaised = false;
            string? capturedPropertyName = null;
            _viewModel.PropertyChanged += (s, e) =>
            {
                capturedPropertyName = e.PropertyName;
                propertyChangedRaised = true;
            };

            // Act
            _viewModel.StatusMessage = "Loading units...";

            // Assert
            propertyChangedRaised.Should().BeTrue();
            capturedPropertyName.Should().Be(nameof(MainViewModel.StatusMessage));
            _viewModel.StatusMessage.Should().Be("Loading units...");
        }

        [Fact]
        public void FilteredUnits_InitialState_ShouldBeEmpty()
        {
            // Arrange
            var newViewModel = new MainViewModel();

            // Assert
            newViewModel.FilteredUnits.Should().NotBeNull();
            newViewModel.FilteredUnits.Should().BeEmpty();
        }

        [Theory]
        [InlineData("tank", 2)] // يجب أن يجد Crusader Tank و Overlord Tank
        [InlineData("usa", 1)]  // يجب أن يجد USATankCrusader
        [InlineData("xyz", 0)]  // لا يوجد
        public void SearchText_WithDifferentQueries_ShouldFilterCorrectly(string searchText, int expectedCount)
        {
            // Arrange
            _viewModel.AllUnits = new ObservableCollection<UnitDisplayModel>
            {
                new UnitDisplayModel { UnitId = "USATankCrusader", UnitName = "Crusader Tank" },
                new UnitDisplayModel { UnitId = "ChinaTankOverlord", UnitName = "Overlord Tank" },
                new UnitDisplayModel { UnitId = "GLAVehicleTechnical", UnitName = "Technical" }
            };

            // Act
            _viewModel.SearchText = searchText;

            // Assert
            _viewModel.FilteredUnits.Should().HaveCount(expectedCount);
        }
    }
}

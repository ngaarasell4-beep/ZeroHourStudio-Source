using FluentAssertions;
using Xunit;
using ZeroHourStudio.UI.WPF.ViewModels;
using ZeroHourStudio.Domain.Entities;
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
        public void SelectedUnit_WhenChanged_ShouldNotifyPropertyChanged()
        {
            // Arrange
            bool propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedUnit))
                    propertyChangedRaised = true;
            };

            var unit = new SageUnit
            {
                TechnicalName = "ChinaTankOverlord",
                Side = "China"
            };

            // Act
            _viewModel.SelectedUnit = unit;

            // Assert
            propertyChangedRaised.Should().BeTrue();
            _viewModel.SelectedUnit.Should().NotBeNull();
            _viewModel.SelectedUnit!.TechnicalName.Should().Be("ChinaTankOverlord");
        }

        [Fact]
        public void ProgressValue_ShouldAcceptValues()
        {
            // Act
            _viewModel.ProgressValue = 50;

            // Assert
            _viewModel.ProgressValue.Should().Be(50);
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
        public void Units_InitialState_ShouldBeEmpty()
        {
            // Arrange
            var newViewModel = new MainViewModel();

            // Assert
            newViewModel.Units.Should().NotBeNull();
            newViewModel.Units.Should().BeEmpty();
        }

        [Fact]
        public void SearchText_WhenSet_ShouldNotifyPropertyChanged()
        {
            // Arrange
            bool propertyChangedRaised = false;
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SearchText))
                    propertyChangedRaised = true;
            };

            // Act
            _viewModel.SearchText = "crusader";

            // Assert
            propertyChangedRaised.Should().BeTrue();
            _viewModel.SearchText.Should().Be("crusader");
        }

        [Fact]
        public void Units_ShouldAcceptSageUnits()
        {
            // Arrange
            var unit = new SageUnit
            {
                TechnicalName = "USATankCrusader",
                Side = "USA",
                ModelW3D = "AVCrusader.w3d"
            };

            // Act
            _viewModel.Units.Add(unit);

            // Assert
            _viewModel.Units.Should().HaveCount(1);
            _viewModel.Units.First().TechnicalName.Should().Be("USATankCrusader");
        }
    }
}

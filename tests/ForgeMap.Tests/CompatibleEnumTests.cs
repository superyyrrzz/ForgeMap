using FluentAssertions;
using ForgeMap;
using Xunit;

namespace ForgeMap.Tests.CompatibleEnumSource
{
    public enum Priority { Low, Medium, High }

    public class TaskEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Priority Priority { get; set; }
    }
}

namespace ForgeMap.Tests.CompatibleEnumDest
{
    public enum Priority { Low, Medium, High }

    public class TaskDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Priority Priority { get; set; }
    }
}

namespace ForgeMap.Tests
{
    [ForgeMap]
    public partial class CompatibleEnumForger
    {
        public partial CompatibleEnumDest.TaskDto Forge(CompatibleEnumSource.TaskEntity source);
    }

    public class CompatibleEnumMappingTests
    {
        private readonly CompatibleEnumForger _forger = new();

        [Fact]
        public void Forge_CompatibleEnums_MapsCorrectly()
        {
            var source = new CompatibleEnumSource.TaskEntity
            {
                Id = 1,
                Name = "Test",
                Priority = CompatibleEnumSource.Priority.High
            };

            var result = _forger.Forge(source);

            result.Id.Should().Be(1);
            result.Name.Should().Be("Test");
            result.Priority.Should().Be(CompatibleEnumDest.Priority.High);
        }

        [Theory]
        [InlineData(CompatibleEnumSource.Priority.Low, CompatibleEnumDest.Priority.Low)]
        [InlineData(CompatibleEnumSource.Priority.Medium, CompatibleEnumDest.Priority.Medium)]
        [InlineData(CompatibleEnumSource.Priority.High, CompatibleEnumDest.Priority.High)]
        public void Forge_CompatibleEnums_AllValues_MapCorrectly(
            CompatibleEnumSource.Priority sourcePriority,
            CompatibleEnumDest.Priority expectedPriority)
        {
            var source = new CompatibleEnumSource.TaskEntity
            {
                Id = 1,
                Name = "Test",
                Priority = sourcePriority
            };

            var result = _forger.Forge(source);
            result.Priority.Should().Be(expectedPriority);
        }
    }
}

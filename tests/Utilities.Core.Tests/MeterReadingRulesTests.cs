namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class MeterReadingRulesTests
{
    [Fact]
    public void Assess_WithPreviousReadingAndTariff_CalculatesFractionalConsumptionAndAmount()
    {
        var assessment = MeterReadingRules.Assess(18.75, 11.25, 2.4m);

        Assert.Equal(7.5, assessment.Consumption);
        Assert.Equal(2.4m, assessment.AppliedRatePerUnit);
        Assert.Equal(18m, assessment.Amount);
        Assert.False(assessment.RequiresReview);
    }

    [Fact]
    public void Assess_LowerReading_RequiresReviewAndDoesNotCreateNegativeConsumption()
    {
        var assessment = MeterReadingRules.Assess(9.5, 10.25, 3m);

        Assert.True(assessment.RequiresReview);
        Assert.Equal(ReadingWarning.LowerThanPreviousReading, assessment.Warning);
        Assert.Null(assessment.Consumption);
        Assert.Null(assessment.Amount);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    public void Assess_InvalidCurrentValue_Throws(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeterReadingRules.Assess(value, null, null));
    }

    [Fact]
    public void Assess_WithoutPreviousValueOrTariff_LeavesCalculationSnapshotsEmpty()
    {
        var assessment = MeterReadingRules.Assess(4.25, null, 1.2m);

        Assert.Null(assessment.Consumption);
        Assert.Null(assessment.Amount);
        Assert.Equal(1.2m, assessment.AppliedRatePerUnit);
    }
}

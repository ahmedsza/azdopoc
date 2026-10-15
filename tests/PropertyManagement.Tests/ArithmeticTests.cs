using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PropertyManagement.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class ArithmeticTests
{
    [TestMethod]
    [DataRow(2, 3, 5)]
    [DataRow(-2, -3, -5)]
    [DataRow(0, 7, 7)]
    public void Add_ReturnsExpectedSum(int left, int right, int expected)
    {
        var actual = left + right;

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(8, 3, 5)]
    [DataRow(3, 8, -5)]
    [DataRow(7, 7, 0)]
    public void Subtract_ReturnsExpectedDifference(int left, int right, int expected)
    {
        var actual = left - right;

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(4, 3, 12)]
    [DataRow(-4, 3, -12)]
    [DataRow(4, 0, 0)]
    public void Multiply_ReturnsExpectedProduct(int left, int right, int expected)
    {
        var actual = left * right;

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(10.0, 2.0, 5.0)]
    [DataRow(7.0, 2.0, 3.5)]
    [DataRow(-9.0, 3.0, -3.0)]
    [DataRow(0.0, 5.0, 0.0)]
    public void Divide_ReturnsExpectedQuotient(double dividend, double divisor, double expected)
    {
        var actual = dividend / divisor;

        Assert.AreEqual(expected, actual, 0.000001);
    }

    [TestMethod]
    public void Divide_IntegerByZero_ThrowsDivideByZeroException()
    {
        var divisor = 0;

        Assert.ThrowsExactly<DivideByZeroException>(() => { _ = 10 / divisor; });
    }
}

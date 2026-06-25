using Bookstore.Models;
using Bookstore.Tests.Helpers;

namespace Bookstore.Tests.Unit;

public class PricingServiceTests
{
    private readonly PricingService _pricing = new();

    [Fact]
    public void Silver_TwoBooks_ExampleFromRequirements()
    {
        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        var result = _pricing.CalculateOrderTotal(order, TestData.RegularDay);

        Assert.Equal(3000m, result.Subtotal);
        Assert.Equal(0.05m, result.DiscountRate);
        Assert.Equal(150m, result.DiscountAmount);
        Assert.Equal(2850m, result.SubtotalAfterDiscount);
        Assert.Equal(302m, result.DeliveryFee);
        Assert.Equal(285m, result.Vat);
        Assert.Equal(3437m, result.Total);
    }

    [Fact]
    public void Gold_FiveThousand_FreeDeliveryAndCorrectTotal()
    {
        var book = new Book("978-gold", "Дорогая", "Автор", BookCategory.FICTION,
            5000m, 500, 2020);
        var order = TestData.DraftOrder(TestData.GoldCustomer(), new CartItem(book, 1));

        var result = _pricing.CalculateOrderTotal(order, TestData.RegularDay);

        Assert.Equal(0m, result.DeliveryFee);
        Assert.Equal(4950m, result.Total);
    }

    [Fact]
    public void Bronze_Welcome15_AppliesPromoDiscount()
    {
        var book = new Book("978-welcome", "Книга", "Автор", BookCategory.FICTION,
            2000m, 400, 2020);
        var order = TestData.DraftOrder(TestData.BronzeCustomer(), new CartItem(book, 1));
        order.PromoCode = "WELCOME15";

        var result = _pricing.CalculateOrderTotal(order, TestData.RegularDay);

        Assert.Equal(0.15m, result.DiscountRate);
        Assert.Equal(300m, result.DiscountAmount);
        Assert.Equal(1700m, result.SubtotalAfterDiscount);
    }

    [Fact]
    public void Student25_AllTextbooks_ReturnsDiscount()
    {
        var items = new List<CartItem> { new(TestData.Textbook, 1) };
        var rate = _pricing.ApplicablePromoDiscount("STUDENT25", items);
        Assert.Equal(0.25m, rate);
    }

    [Fact]
    public void Student25_MixedCategories_ReturnsZero()
    {
        var items = new List<CartItem>
        {
            new(TestData.Textbook, 1),
            new(TestData.FictionBook, 1),
        };
        var rate = _pricing.ApplicablePromoDiscount("STUDENT25", items);
        Assert.Equal(0m, rate);
    }

    [Fact]
    public void Summer20_AnyOrder_Returns20Percent()
    {
        var items = new List<CartItem> { new(TestData.FictionBook, 1) };
        Assert.Equal(0.20m, _pricing.ApplicablePromoDiscount("SUMMER20", items));
    }

    [Fact]
    public void UnknownPromo_ThrowsPricingException()
    {
        var items = new List<CartItem> { new(TestData.FictionBook, 1) };
        var ex = Assert.Throws<PricingException>(
            () => _pricing.ApplicablePromoDiscount("UNKNOWN", items));
        Assert.Contains("Неизвестный промокод", ex.Message);
    }

    [Fact]
    public void RareBooks_ExcludedFromDiscount()
    {
        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 1),
            new CartItem(TestData.RareBook, 1));

        var result = _pricing.CalculateOrderTotal(order, TestData.RegularDay);

        Assert.Equal(1500m, result.DiscountableSubtotal);
        Assert.Equal(12000m, result.NonDiscountableSubtotal);
        Assert.Equal(75m, result.DiscountAmount);
    }

    [Fact]
    public void Delivery_ChildrenBook_IsFree()
    {
        var items = new List<CartItem> { new(TestData.ChildrenBook, 1) };
        Assert.Equal(0m, _pricing.CalculateDeliveryFee(items, 100m));
    }

    [Fact]
    public void Delivery_SubtotalAtThreshold_IsFree()
    {
        var items = new List<CartItem> { new(TestData.FictionBook, 1) };
        Assert.Equal(0m, _pricing.CalculateDeliveryFee(items, 3000m));
    }

    [Fact]
    public void Delivery_SubtotalBelowThreshold_BaseFeeApplied()
    {
        var items = new List<CartItem> { new(TestData.FictionBook, 1) };
        Assert.Equal(300m, _pricing.CalculateDeliveryFee(items, 1000m));
    }

    [Fact]
    public void Delivery_WeightOver1Kg_AddsPerBlockFee()
    {
        var heavy = new Book("978-heavy", "Тяжёлая", "Автор", BookCategory.FICTION,
            1000m, 1200, 2020);
        var items = new List<CartItem> { new(heavy, 1) };
        Assert.Equal(302m, _pricing.CalculateDeliveryFee(items, 1000m));
    }

    [Fact]
    public void Delivery_ExactlyOneKg_NoExtraFee()
    {
        var book = new Book("978-kg", "Килограмм", "Автор", BookCategory.FICTION,
            1000m, 1000, 2020);
        var items = new List<CartItem> { new(book, 1) };
        Assert.Equal(300m, _pricing.CalculateDeliveryFee(items, 1000m));
    }

    [Fact]
    public void Vat_CalculatedAt10Percent()
    {
        Assert.Equal(285m, _pricing.CalculateVat(2850m));
    }

    [Fact]
    public void BlackFriday_Discount30Percent()
    {
        var order = TestData.DraftOrder(
            TestData.BronzeCustomer(),
            new CartItem(TestData.FictionBook, 1));
        var rate = _pricing.BestDiscountRate(order, new DateTime(2025, 11, 28));
        Assert.Equal(0.30m, rate);
    }

    [Theory]
    [InlineData(11, 24, true)]
    [InlineData(11, 30, true)]
    [InlineData(11, 23, false)]
    [InlineData(12, 1, false)]
    [InlineData(11, 28, true)]
    public void IsBlackFriday_BoundaryDates(int month, int day, bool expected)
    {
        Assert.Equal(expected, PricingService.IsBlackFriday(new DateTime(2025, month, day)));
    }

    [Fact]
    public void BestDiscount_PromoBeatsGoldTier()
    {
        var order = TestData.DraftOrder(
            TestData.GoldCustomer(),
            new CartItem(TestData.FictionBook, 1));
        order.PromoCode = "SUMMER20";
        Assert.Equal(0.20m, _pricing.BestDiscountRate(order, TestData.RegularDay));
    }

    [Fact]
    public void BestDiscount_GoldTierNoPromo()
    {
        var order = TestData.DraftOrder(
            TestData.GoldCustomer(),
            new CartItem(TestData.FictionBook, 1));
        Assert.Equal(0.10m, _pricing.BestDiscountRate(order, TestData.RegularDay));
    }

    [Fact]
    public void ValidateOrder_EmptyOrder_ThrowsPricingException()
    {
        var order = TestData.DraftOrder(TestData.BronzeCustomer());
        var ex = Assert.Throws<PricingException>(() => _pricing.ValidateOrder(order));
        Assert.Contains("пустым", ex.Message);
    }

    [Fact]
    public void ValidateOrder_BlockedCustomer_ThrowsPricingException()
    {
        var order = TestData.DraftOrder(
            TestData.BlockedCustomer(),
            new CartItem(TestData.FictionBook, 1));
        var ex = Assert.Throws<PricingException>(() => _pricing.ValidateOrder(order));
        Assert.Contains("заблокирован", ex.Message);
    }

    [Fact]
    public void ValidateOrder_QuantityAbove20_ThrowsPricingException()
    {
        var order = TestData.DraftOrder(
            TestData.BronzeCustomer(),
            new CartItem(TestData.FictionBook, 21));
        var ex = Assert.Throws<PricingException>(() => _pricing.ValidateOrder(order));
        Assert.Contains("лимит количества", ex.Message);
    }

    [Fact]
    public void ValidateOrder_QuantityZero_ThrowsPricingException()
    {
        var order = TestData.DraftOrder(
            TestData.BronzeCustomer(),
            new CartItem(TestData.FictionBook, 0));
        var ex = Assert.Throws<PricingException>(() => _pricing.ValidateOrder(order));
        Assert.Contains("положительным", ex.Message);
    }

    [Fact]
    public void ValidateOrder_SubtotalAboveBronzeLimit_ThrowsPricingException()
    {
        var expensive = new Book("978-exp", "Дорого", "Автор", BookCategory.FICTION,
            100001m, 100, 2020);
        var order = TestData.DraftOrder(
            TestData.BronzeCustomer(),
            new CartItem(expensive, 1));
        var ex = Assert.Throws<PricingException>(() => _pricing.ValidateOrder(order));
        Assert.Contains("превышает лимит", ex.Message);
    }

    [Fact]
    public void ValidateOrder_GoldWithinHighLimit_Passes()
    {
        var book = new Book("978-gold-ok", "Дорогая", "Автор", BookCategory.FICTION,
            200000m, 500, 2020);
        var order = TestData.DraftOrder(
            TestData.GoldCustomer(),
            new CartItem(book, 1));
        _pricing.ValidateOrder(order);
    }
}

using Bookstore.Models;
using Bookstore.Tests.Helpers;
using CsCheck;

namespace Bookstore.Tests.Property;

public class PricingPropertyTests
{
    private static readonly PricingService Pricing = new();


    private static readonly Gen<BookCategory> GenNonRareCategory =
        Gen.OneOf(
            Gen.Const(BookCategory.FICTION),
            Gen.Const(BookCategory.NON_FICTION),
            Gen.Const(BookCategory.TEXTBOOK),
            Gen.Const(BookCategory.CHILDREN));

    private static readonly Gen<Book> GenBook =
        from category in GenNonRareCategory
        from price in Gen.Decimal[1m, 5000m]
        from weight in Gen.Int[100, 2000]
        select new Book(
            Isbn: "978-prop-test",
            Title: "Prop Book",
            Author: "Author",
            Category: category,
            BasePrice: Math.Round(price, 2),
            WeightGrams: weight,
            PublicationYear: 2020);

    private static readonly Gen<CartItem> GenCartItem =
        from book in GenBook
        from qty in Gen.Int[1, 5]
        select new CartItem(book, qty);

    private static readonly Gen<Order> GenValidOrder =
        from items in GenCartItem.List[1, 3]
        select TestData.DraftOrder(TestData.BronzeCustomer(), items.ToArray());


    [Fact]
    public void Total_EqualsSum_OfComponents()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            var expected = b.SubtotalAfterDiscount + b.DeliveryFee + b.Vat;
            Assert.Equal(expected, b.Total);
        });
    }

    [Fact]
    public void DiscountAmount_DoesNotExceed_DiscountableSubtotal()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            Assert.True(b.DiscountAmount <= b.DiscountableSubtotal,
                $"Скидка {b.DiscountAmount} превысила облагаемую базу {b.DiscountableSubtotal}");
        });
    }

    [Fact]
    public void SubtotalAfterDiscount_IsNonNegative()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            Assert.True(b.SubtotalAfterDiscount >= 0m,
                $"SubtotalAfterDiscount отрицательный: {b.SubtotalAfterDiscount}");
        });
    }

    [Fact]
    public void DeliveryFee_IsNonNegative()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            Assert.True(b.DeliveryFee >= 0m);
        });
    }

    [Fact]
    public void Vat_MatchesRateTimesSubtotalAfterDiscount()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            var expected = Pricing.CalculateVat(b.SubtotalAfterDiscount);
            Assert.Equal(expected, b.Vat);
        });
    }

    [Fact]
    public void Subtotal_EqualsSumOfLineTotals()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            var expectedSubtotal = order.Items.Sum(i => i.LineTotal);
            Assert.Equal(expectedSubtotal, b.Subtotal);
        });
    }

    [Fact]
    public void DiscountRate_IsMaxOfTierAndOtherApplicableRates()
    {
        GenValidOrder.Sample(order =>
        {
            var rate = Pricing.BestDiscountRate(order, TestData.RegularDay);
            var tierRate = PricingService.TierDiscounts[order.Customer.Tier];
            Assert.Equal(tierRate, rate);
        });
    }

    [Fact]
    public void Total_IsGreaterOrEqual_SubtotalAfterDiscount()
    {
        GenValidOrder.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            Assert.True(b.Total >= b.SubtotalAfterDiscount,
                "Total не может быть меньше суммы после скидки");
        });
    }

    [Fact]
    public void OrderWithChildrenBook_AlwaysHasFreeDelivery()
    {
        var genOrderWithChildren =
            from qty in Gen.Int[1, 5]
            select TestData.DraftOrder(
                TestData.BronzeCustomer(),
                new CartItem(TestData.ChildrenBook, qty));

        genOrderWithChildren.Sample(order =>
        {
            var b = Pricing.CalculateOrderTotal(order, TestData.RegularDay);
            Assert.Equal(0m, b.DeliveryFee);
        });
    }
}

using Bookstore.Interfaces;
using Bookstore.Models;
using Bookstore.Tests.Helpers;
using Moq;

namespace Bookstore.Tests.Mocks;

public class OrderServiceCheckoutTests
{
    private readonly Mock<IBookRepository> _books = new();
    private readonly Mock<IInventoryService> _inventory = new();
    private readonly Mock<IPaymentGateway> _payment = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IClock> _clock = new();
    private readonly OrderService _service;

    public OrderServiceCheckoutTests()
    {
        _clock.Setup(c => c.Now()).Returns(TestData.RegularDay);
        _service = new OrderService(
            _books.Object,
            _inventory.Object,
            _payment.Object,
            _notifications.Object,
            _clock.Object);
    }

    private void SetupSuccessfulPayment(string transactionId = "TX-100")
    {
        _inventory.Setup(i => i.GetStock(It.IsAny<string>())).Returns(10);
        _inventory.Setup(i => i.Reserve(It.IsAny<string>(), It.IsAny<int>())).Returns("RES-1");
        _payment.Setup(p => p.Charge(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new PaymentResult(PaymentStatus.SUCCESS, TransactionId: transactionId));
    }

    [Fact]
    public void Checkout_Successful_OrderPaidAndResultReturned()
    {
        SetupSuccessfulPayment("TX-100");
        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        var result = _service.Checkout(order, TestData.MoscowAddress);

        Assert.Equal(OrderStatus.PAID, result.Order.Status);
        Assert.Equal("TX-100", result.PaymentTransactionId);
        Assert.Equal(3437m, result.Total);
        _notifications.Verify(
            n => n.SendOrderConfirmation(It.IsAny<Customer>(), It.IsAny<Order>()),
            Times.Once);
    }

    [Fact]
    public void Checkout_InsufficientStock_ThrowsInventoryException_OrderStaysDraft()
    {
        _inventory.Setup(i => i.GetStock(It.IsAny<string>())).Returns(1);
        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        Assert.Throws<InventoryException>(() => _service.Checkout(order, TestData.MoscowAddress));

        Assert.Equal(OrderStatus.DRAFT, order.Status);
        _inventory.Verify(i => i.Reserve(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _payment.Verify(p => p.Charge(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void Checkout_ReservationFailure_ReleasesAlreadyReserved()
    {
        _inventory.Setup(i => i.GetStock(It.IsAny<string>())).Returns(10);
        _inventory.SetupSequence(i => i.Reserve(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("RES-1")
            .Throws(new InventoryException("Сбой склада"));

        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 1),
            new CartItem(TestData.FictionBook2, 1));

        Assert.Throws<InventoryException>(() => _service.Checkout(order, TestData.MoscowAddress));

        _inventory.Verify(i => i.Release("RES-1"), Times.Once);
        Assert.Equal(OrderStatus.DRAFT, order.Status);
    }

    [Fact]
    public void Checkout_PaymentDeclined_ReleasesReservationsAndCancelsOrder()
    {
        _inventory.Setup(i => i.GetStock(It.IsAny<string>())).Returns(10);
        _inventory.Setup(i => i.Reserve(It.IsAny<string>(), It.IsAny<int>())).Returns("RES-1");
        _payment.Setup(p => p.Charge(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new PaymentResult(PaymentStatus.DECLINED, DeclineReason: "Недостаточно средств"));

        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        Assert.Throws<PaymentDeclinedException>(
            () => _service.Checkout(order, TestData.MoscowAddress));

        Assert.Equal(OrderStatus.CANCELLED, order.Status);
        _inventory.Verify(i => i.Release("RES-1"), Times.AtLeastOnce);
    }

    [Fact]
    public void Checkout_PaymentGatewayThrows_ReleasesReservationsAndCancelsOrder()
    {
        _inventory.Setup(i => i.GetStock(It.IsAny<string>())).Returns(10);
        _inventory.Setup(i => i.Reserve(It.IsAny<string>(), It.IsAny<int>())).Returns("RES-1");
        _payment.Setup(p => p.Charge(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new PaymentException("Шлюз недоступен"));

        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        Assert.Throws<PaymentException>(() => _service.Checkout(order, TestData.MoscowAddress));

        _inventory.Verify(i => i.Release("RES-1"), Times.Once);
        Assert.Equal(OrderStatus.CANCELLED, order.Status);
    }

    [Fact]
    public void Checkout_NotificationFails_OrderStaysPaid()
    {
        SetupSuccessfulPayment("TX-200");
        _notifications.Setup(n =>
                n.SendOrderConfirmation(It.IsAny<Customer>(), It.IsAny<Order>()))
            .Throws(new Exception("SMTP down"));

        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        var result = _service.Checkout(order, TestData.MoscowAddress);

        Assert.Equal(OrderStatus.PAID, result.Order.Status);
        Assert.Equal("TX-200", result.Order.PaymentTransactionId);
    }

    [Fact]
    public void Checkout_NotDraftStatus_ThrowsInvalidOrderStateException()
    {
        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 1));
        order.Status = OrderStatus.PAID;

        Assert.Throws<InvalidOrderStateException>(
            () => _service.Checkout(order, TestData.MoscowAddress));
    }

    [Fact]
    public void Checkout_EmptyOrder_ThrowsPricingException()
    {
        var order = TestData.DraftOrder(TestData.BronzeCustomer());

        Assert.Throws<PricingException>(() => _service.Checkout(order, TestData.MoscowAddress));
    }

    [Fact]
    public void Checkout_BlockedCustomer_ThrowsPricingException()
    {
        var order = TestData.DraftOrder(
            TestData.BlockedCustomer(),
            new CartItem(TestData.FictionBook, 1));

        Assert.Throws<PricingException>(() => _service.Checkout(order, TestData.MoscowAddress));
    }

    [Fact]
    public void Checkout_SetsShippingAddress()
    {
        SetupSuccessfulPayment();
        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));

        _service.Checkout(order, TestData.MoscowAddress);

        Assert.Equal(TestData.MoscowAddress, order.ShippingAddress);
    }

    [Fact]
    public void Cancel_DraftOrder_SetsCancelled()
    {
        var order = TestData.DraftOrder(
            TestData.BronzeCustomer(),
            new CartItem(TestData.FictionBook, 1));

        _service.Cancel(order);

        Assert.Equal(OrderStatus.CANCELLED, order.Status);
    }

    [Fact]
    public void Cancel_PaidOrder_RefundsAndSetsRefunded()
    {
        SetupSuccessfulPayment("TX-REF");
        _payment.Setup(p => p.Refund(It.IsAny<string>(), It.IsAny<decimal>()))
            .Returns(new PaymentResult(PaymentStatus.SUCCESS));

        var order = TestData.DraftOrder(
            TestData.SilverCustomer(),
            new CartItem(TestData.FictionBook, 2));
        _service.Checkout(order, TestData.MoscowAddress);

        _service.Cancel(order);

        Assert.Equal(OrderStatus.REFUNDED, order.Status);
        _payment.Verify(p => p.Refund("TX-REF", It.IsAny<decimal>()), Times.Once);
    }

    [Fact]
    public void Cancel_DeliveredOrder_ThrowsInvalidOrderStateException()
    {
        var order = TestData.DraftOrder(
            TestData.BronzeCustomer(),
            new CartItem(TestData.FictionBook, 1));
        order.Status = OrderStatus.DELIVERED;

        Assert.Throws<InvalidOrderStateException>(() => _service.Cancel(order));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bookstore.Interfaces;
using Bookstore.Models;
using Bookstore.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Bookstore.Tests.Api;

public class BookstoreApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public BookstoreApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }


    private WebApplicationFactory<Program> BuildFactory(
        Action<IServiceCollection>? configure = null,
        Action<Dictionary<string, Customer>>? seedCustomers = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var booksMock = new Mock<IBookRepository>();
                var inventoryMock = new Mock<IInventoryService>();
                var paymentMock = new Mock<IPaymentGateway>();
                var notificationsMock = new Mock<INotificationService>();
                var clockMock = new Mock<IClock>();

                clockMock.Setup(c => c.Now()).Returns(TestData.RegularDay);
                booksMock.Setup(b => b.GetByIsbn(It.IsAny<string>()))
                    .Throws(new BookNotFoundException("Книга не найдена"));

                RemoveService<IBookRepository>(services);
                RemoveService<IInventoryService>(services);
                RemoveService<IPaymentGateway>(services);
                RemoveService<INotificationService>(services);
                RemoveService<IClock>(services);

                services.AddSingleton(booksMock.Object);
                services.AddSingleton(inventoryMock.Object);
                services.AddSingleton(paymentMock.Object);
                services.AddSingleton(notificationsMock.Object);
                services.AddSingleton(clockMock.Object);
                configure?.Invoke(services);
            });
        });
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor is not null) services.Remove(descriptor);
    }

    private static object CheckoutBody() => new
    {
        shipping_address = new
        {
            country = "RU",
            city = "Москва",
            postal_code = "101000",
            street = "Тверская, 1",
        }
    };
    private (HttpClient client, Dictionary<string, Customer> customers) CreateClient(
        Action<IServiceCollection>? configure = null)
    {
        Dictionary<string, Customer>? capturedCustomers = null;

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var booksMock = new Mock<IBookRepository>();
                var inventoryMock = new Mock<IInventoryService>();
                var paymentMock = new Mock<IPaymentGateway>();
                var notificationsMock = new Mock<INotificationService>();
                var clockMock = new Mock<IClock>();

                clockMock.Setup(c => c.Now()).Returns(TestData.RegularDay);
                booksMock.Setup(b => b.GetByIsbn(TestData.FictionBook.Isbn))
                    .Returns(TestData.FictionBook);
                booksMock.Setup(b => b.GetByIsbn(It.Is<string>(s => s != TestData.FictionBook.Isbn)))
                    .Throws(new BookNotFoundException("Книга не найдена"));

                inventoryMock.Setup(i => i.GetStock(It.IsAny<string>())).Returns(100);
                inventoryMock.Setup(i => i.Reserve(It.IsAny<string>(), It.IsAny<int>()))
                    .Returns("RES-1");
                paymentMock.Setup(p => p.Charge(
                        It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(new PaymentResult(PaymentStatus.SUCCESS, TransactionId: "TX-OK"));

                RemoveService<IBookRepository>(services);
                RemoveService<IInventoryService>(services);
                RemoveService<IPaymentGateway>(services);
                RemoveService<INotificationService>(services);
                RemoveService<IClock>(services);

                services.AddSingleton(booksMock.Object);
                services.AddSingleton(inventoryMock.Object);
                services.AddSingleton(paymentMock.Object);
                services.AddSingleton(notificationsMock.Object);
                services.AddSingleton(clockMock.Object);

                configure?.Invoke(services);
            });
        });

        var client = factory.CreateClient();
        var customers = factory.Services.GetRequiredService<Dictionary<string, Customer>>();
        capturedCustomers = customers;

        return (client, customers);
    }


    [Fact]
    public async Task CreateOrder_ValidCustomer_Returns201()
    {
        var (client, customers) = CreateClient();
        customers["user1"] = TestData.SilverCustomer("user1");

        var response = await client.PostAsJsonAsync("/orders", new { customer_id = "user1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DRAFT", body.GetProperty("status").GetString());
        Assert.Equal("user1", body.GetProperty("customer_id").GetString());
        Assert.True(body.TryGetProperty("order_id", out _));
        Assert.True(body.TryGetProperty("items_count", out _));
        Assert.True(body.TryGetProperty("subtotal", out _));
    }

    [Fact]
    public async Task CreateOrder_BlockedCustomer_Returns403()
    {
        var (client, customers) = CreateClient();
        customers["blocked"] = TestData.BlockedCustomer("blocked");

        var response = await client.PostAsJsonAsync("/orders", new { customer_id = "blocked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task CreateOrder_UnknownCustomer_Returns404()
    {
        var (client, _) = CreateClient();
        var response = await client.PostAsJsonAsync("/orders", new { customer_id = "ghost" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_InvalidBody_Returns422()
    {
        var (client, _) = CreateClient();
        var response = await client.PostAsJsonAsync("/orders", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }


    [Fact]
    public async Task GetOrder_UnknownId_Returns404()
    {
        var (client, _) = CreateClient();
        var response = await client.GetAsync("/orders/unknown-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_ExistingOrder_Returns200WithSchema()
    {
        var (client, customers) = CreateClient();
        customers["u"] = TestData.SilverCustomer("u");
        var createResp = await client.PostAsJsonAsync("/orders", new { customer_id = "u" });
        var orderId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("order_id").GetString();

        var response = await client.GetAsync($"/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DRAFT", body.GetProperty("status").GetString());
    }


    [Fact]
    public async Task AddItem_UnknownBook_Returns404()
    {
        var (client, customers) = CreateClient();
        customers["u2"] = TestData.SilverCustomer("u2");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "u2" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/items",
            new { isbn = "missing-isbn-1234", quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_InvalidBody_Returns422()
    {
        var (client, customers) = CreateClient();
        customers["u3"] = TestData.SilverCustomer("u3");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "u3" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();

        var response = await client.PostAsJsonAsync($"/orders/{orderId}/items", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }


    [Fact]
    public async Task Checkout_EmptyOrder_Returns422()
    {
        var (client, customers) = CreateClient();
        customers["empty"] = TestData.SilverCustomer("empty");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "empty" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/checkout", CheckoutBody());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_InvalidAddress_Returns422()
    {
        var (client, customers) = CreateClient();
        customers["addr"] = TestData.SilverCustomer("addr");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "addr" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();

        var response = await client.PostAsJsonAsync($"/orders/{orderId}/checkout", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_InsufficientStock_Returns409()
    {
        var (client, customers) = CreateClient(services =>
        {
            RemoveService<IInventoryService>(services);
            var inv = new Mock<IInventoryService>();
            inv.Setup(i => i.GetStock(It.IsAny<string>())).Returns(0);
            services.AddSingleton(inv.Object);
        });
        customers["stock"] = TestData.SilverCustomer("stock");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "stock" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();
        await client.PostAsJsonAsync($"/orders/{orderId}/items",
            new { isbn = TestData.FictionBook.Isbn, quantity = 2 });

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/checkout", CheckoutBody());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_AlreadyPaid_Returns409()
    {
        var (client, customers) = CreateClient();
        customers["paid"] = TestData.SilverCustomer("paid");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "paid" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();
        await client.PostAsJsonAsync($"/orders/{orderId}/items",
            new { isbn = TestData.FictionBook.Isbn, quantity = 2 });
        await client.PostAsJsonAsync($"/orders/{orderId}/checkout", CheckoutBody());

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/checkout", CheckoutBody());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_PaymentDeclined_Returns402()
    {
        var (client, customers) = CreateClient(services =>
        {
            RemoveService<IPaymentGateway>(services);
            var pay = new Mock<IPaymentGateway>();
            pay.Setup(p => p.Charge(
                    It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new PaymentResult(PaymentStatus.DECLINED, DeclineReason: "Карта отклонена"));
            services.AddSingleton(pay.Object);
        });
        customers["decl"] = TestData.SilverCustomer("decl");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "decl" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();
        await client.PostAsJsonAsync($"/orders/{orderId}/items",
            new { isbn = TestData.FictionBook.Isbn, quantity = 2 });

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/checkout", CheckoutBody());

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_GatewayError_Returns502()
    {
        var (client, customers) = CreateClient(services =>
        {
            RemoveService<IPaymentGateway>(services);
            var pay = new Mock<IPaymentGateway>();
            pay.Setup(p => p.Charge(
                    It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new PaymentException("Шлюз недоступен"));
            services.AddSingleton(pay.Object);
        });
        customers["gw"] = TestData.SilverCustomer("gw");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "gw" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();
        await client.PostAsJsonAsync($"/orders/{orderId}/items",
            new { isbn = TestData.FictionBook.Isbn, quantity = 2 });

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/checkout", CheckoutBody());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_Successful_ReturnsCorrectSchema()
    {
        var (client, customers) = CreateClient();
        customers["ok"] = TestData.SilverCustomer("ok");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "ok" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();
        await client.PostAsJsonAsync($"/orders/{orderId}/items",
            new { isbn = TestData.FictionBook.Isbn, quantity = 2 });

        var response = await client.PostAsJsonAsync(
            $"/orders/{orderId}/checkout", CheckoutBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PAID", body.GetProperty("status").GetString());
        Assert.Equal(3437m, body.GetProperty("total").GetDecimal());
        Assert.Equal("TX-OK", body.GetProperty("transaction_id").GetString());
        Assert.True(body.TryGetProperty("order_id", out _));
    }


    [Fact]
    public async Task Cancel_DraftOrder_Returns200WithCancelledStatus()
    {
        var (client, customers) = CreateClient();
        customers["c"] = TestData.SilverCustomer("c");
        var orderId = (await (await client.PostAsJsonAsync(
            "/orders", new { customer_id = "c" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("order_id").GetString();

        var response = await client.PostAsJsonAsync($"/orders/{orderId}/cancel", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CANCELLED", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_UnknownOrder_Returns404()
    {
        var (client, _) = CreateClient();
        var response = await client.PostAsJsonAsync("/orders/no-such-order/cancel", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

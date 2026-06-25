using Bookstore.Models;

namespace Bookstore.Tests.Helpers;

public static class TestData
{
    public static readonly Book FictionBook = new(
        Isbn: "978-test-0001",
        Title: "Тестовая художественная",
        Author: "Автор",
        Category: BookCategory.FICTION,
        BasePrice: 1500m,
        WeightGrams: 600,
        PublicationYear: 2020);

    public static readonly Book FictionBook2 = new(
        Isbn: "978-test-0002",
        Title: "Вторая книга",
        Author: "Автор 2",
        Category: BookCategory.FICTION,
        BasePrice: 1500m,
        WeightGrams: 600,
        PublicationYear: 2021);

    public static readonly Book Textbook = new(
        Isbn: "978-test-0003",
        Title: "Учебник",
        Author: "Иванов",
        Category: BookCategory.TEXTBOOK,
        BasePrice: 900m,
        WeightGrams: 800,
        PublicationYear: 2021);

    public static readonly Book ChildrenBook = new(
        Isbn: "978-test-0004",
        Title: "Сказки",
        Author: "Петрова",
        Category: BookCategory.CHILDREN,
        BasePrice: 500m,
        WeightGrams: 300,
        PublicationYear: 2020);

    public static readonly Book RareBook = new(
        Isbn: "978-test-0005",
        Title: "Редкое издание",
        Author: "Коллекционер",
        Category: BookCategory.RARE,
        BasePrice: 12000m,
        WeightGrams: 1200,
        PublicationYear: 1965);

    public static readonly DateTime RegularDay = new(2025, 6, 15);

    public static readonly Address MoscowAddress = new(
        Country: "RU",
        City: "Москва",
        PostalCode: "101000",
        Street: "Тверская, 1");

    public static Customer BronzeCustomer(string id = "bronze") => new()
    {
        CustomerId = id,
        Name = "Бронза",
        Email = $"{id}@test.com",
        Tier = CustomerTier.BRONZE,
    };

    public static Customer SilverCustomer(string id = "silver") => new()
    {
        CustomerId = id,
        Name = "Серебро",
        Email = $"{id}@test.com",
        Tier = CustomerTier.SILVER,
    };

    public static Customer GoldCustomer(string id = "gold") => new()
    {
        CustomerId = id,
        Name = "Золото",
        Email = $"{id}@test.com",
        Tier = CustomerTier.GOLD,
    };

    public static Customer BlockedCustomer(string id = "blocked") => new()
    {
        CustomerId = id,
        Name = "Заблокирован",
        Email = $"{id}@test.com",
        IsBlocked = true,
    };

    public static Order DraftOrder(Customer customer, params CartItem[] items)
    {
        var order = new Order("test-order", customer);
        order.Items.AddRange(items);
        return order;
    }
}
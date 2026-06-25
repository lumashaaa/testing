# Тесты

```
tests/
└── Bookstore.Tests/
    ├── Helpers/
    │   └── TestData.cs          общие тестовые фикстуры (книги, покупатели, заказы)
    ├── Unit/
    │   └── PricingServiceTests.cs    модульные тесты PricingService
    ├── Mocks/
    │   └── OrderServiceCheckoutTests.cs  тесты OrderService с подменой зависимостей (Moq)
    ├── Api/
    │   └── BookstoreApiTests.cs      HTTP-тесты через WebApplicationFactory
    └── Property/
        └── PricingPropertyTests.cs   property-based тесты (CsCheck)
```

## Запуск

```bash
dotnet test tests/Bookstore.Tests
```

## Мутационное тестирование (Stryker.NET)

```bash
dotnet tool install -g dotnet-stryker
dotnet stryker --project src/Bookstore/Bookstore.csproj
```

Отчёт: `StrykerOutput/reports/mutation-report.html`

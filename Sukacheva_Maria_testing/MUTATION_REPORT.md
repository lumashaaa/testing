# Отчёт по мутационному тестированию (Stryker.NET)

## Цель

Модуль `src/Bookstore/PricingService.cs`.

## Запуск

```bash
dotnet tool install -g dotnet-stryker
dotnet stryker --project src/Bookstore/Bookstore.csproj \
               --test-project tests/Bookstore.Tests/Bookstore.Tests.csproj
```

Отчёт открывается в браузере: `StrykerOutput/reports/mutation-report.html`

## Анализ выживших мутантов

| Файл | Строка | Мутация | Риск | Закрывающий тест |
|------|--------|---------|------|-----------------|
| `PricingService.cs` | `>= FreeDeliveryThreshold` | замена `>=` на `>` | Средний | `Delivery_SubtotalAtThreshold_IsFree` |
| `PricingService.cs` | `weight > 1000` | замена `>` на `>=` | Средний | `Delivery_ExactlyOneKg_NoExtraFee` |
| `PricingService.cs` | `BlackFridayStart` / `BlackFridayEnd` | мутация компаратора `>=` → `>` и `<=` → `<` | Высокий | `IsBlackFriday_BoundaryDates` (Theory) |
| `PricingService.cs` | `Math.Max(Math.Max(...))` | замена на `Math.Min` | Высокий | `BlackFriday_Discount30Percent`, `BestDiscount_PromoBeatsGoldTier` |
| `PricingService.cs` | `VatRate = 0.10m` | изменение константы | Средний | `Vat_CalculatedAt10Percent`, property `Vat_MatchesRateTimesSubtotalAfterDiscount` |

## Итог
ожидаемый Mutation Score ≥ 80%.  
Граничные случаи (порог доставки = 3000, вес ровно 1000 г, даты 24 и 30 ноября)  вынесены в отдельные тесты для убийства мутантов операторов сравнения.

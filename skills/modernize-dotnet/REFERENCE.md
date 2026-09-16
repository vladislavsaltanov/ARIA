---
name: modernize-dotnet-reference
description: Справочник skill modernize-dotnet. Не загружать напрямую.
---

# Modernize Dotnet — Reference

## 0. Главное правило

Твои обучающие данные в основном содержат код эпохи .NET Core 3 – .NET 6 и C# 8–10.
**Считай эти паттерны устаревшими по умолчанию.** Прежде чем выдать код, мысленно
прогоняй его через чек-лист из раздела 10. Если для конструкции есть более новый
идиоматичный аналог — используй его. Не спрашивай разрешения «можно ли использовать
C# 14» — по умолчанию проект таргетит `net10.0`, где C# 14 включён автоматически.

Если пользователь явно указал старую версию (`net8.0`, `netstandard2.0`) — уважай это,
но всё равно применяй максимум доступных фич для этой версии и упомяни, что можно
улучшить после апгрейда.

---

## 1. Базовая настройка проекта (всегда предлагай именно так)

```xml
<!-- Directory.Build.props — общие настройки для всего solution -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsAotCompatible>true</IsAotCompatible> <!-- для библиотек и сервисов, где это реально -->
  </PropertyGroup>
</Project>
```

```xml
<!-- Directory.Packages.props — Central Package Management, версии только здесь -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.*" />
  </ItemGroup>
</Project>
```

- `LangVersion` **не указывай** — для `net10.0` по умолчанию уже C# 14.
- Всегда `Nullable enable`. Никаких `#nullable disable` «чтобы компилировалось».
- `.editorconfig` с `dotnet_diagnostic.*` вместо комментариев «здесь надо бы...».
- Для скриптов/утилит/примеров — **file-based apps** (см. раздел 6), а не консольный csproj.

---

## 2. Язык: C# 12

| Вместо этого (старое) | Пиши это (C# 12) |
| --- | --- |
| Класс с полями `_x` + конструктор, копирующий параметры в поля | **Primary constructor**: `public sealed class OrderService(IOrderRepository repo, ILogger<OrderService> logger)` |
| `new List<int> { 1, 2 }`, `new[] { 1, 2 }`, `Array.Empty<T>()`, `Enumerable.Empty<T>()` | **Collection expressions**: `[1, 2]`, `[]`, `[..first, ..second, extra]` |
| `Func<int, int, int> f = (a, b) => a + b;` с перегрузками ради дефолтов | **Default lambda parameters**: `var f = (int a, int b = 10) => a + b;` |
| `using Point = System.ValueTuple<int,int>;` / без алиасов | **Alias any type**: `using Point = (int X, int Y);` `using Handlers = Dictionary<string, Func<Task>>;` |
| `in` для больших структур с неявными копиями | `ref readonly` параметры там, где нужна семантика «только чтение, без копии» |
| `fixed` буферы / `stackalloc` руками | `[InlineArray(N)] struct Buffer<T> { private T _e0; }` |
| Комментарий `// experimental` | `[Experimental("LIB001")]` |

Правила:

- Primary constructor параметры **не** объявляй `readonly`-полями заново без нужды; если нужен
  захват в поле — `private readonly IFoo _foo = foo;` (явно, один раз).
- Для DTO/сообщений/событий — `record` / `record struct`, а не class с get/set.
- Коллекции в сигнатурах принимай как `IReadOnlyList<T>` / `ReadOnlySpan<T>` / `IEnumerable<T>`,
  возвращай конкретные неизменяемые (`ImmutableArray<T>`, `FrozenDictionary<,>`, `FrozenSet<>`).

---

## 3. Язык: C# 13

```csharp
// params для любых коллекций — больше не только массивы
public static int Sum(params ReadOnlySpan<int> values) { ... }
public void Log(params IEnumerable<string> lines) { ... }

// Новый тип блокировки вместо lock(new object())
private readonly Lock _gate = new();          // System.Threading.Lock
public void Add(Item i) { lock (_gate) { ... } }   // компилятор сам использует EnterScope()

// partial-свойства и индексаторы — для source generators / разделения генерируемого и ручного кода
public partial string Name { get; set; }

// Приоритет перегрузок, чтобы новые API не ломали существующих вызывающих
[OverloadResolutionPriority(1)]
public void Write(ReadOnlySpan<char> text) { ... }
public void Write(string text) { ... }

// ref struct в generic-ах и интерфейсах
public interface IParser<TSelf> where TSelf : allows ref struct { ... }
public ref struct Utf8Reader : IDisposable { ... }   // ref struct теперь может реализовывать интерфейсы

// Индекс с конца в инициализаторах объектов
var t = new Timer { Buffer = { [^1] = 0 } };

// Escape-последовательность
const string Esc = "\e[0m";
```

Правила:

- **Никогда** `lock (this)`, `lock (typeof(X))`, `lock (someString)`. Только `System.Threading.Lock`
  или `SemaphoreSlim` для async.
- `ref`/`unsafe` теперь допустимы внутри async и итераторов (не пересекая `await`/`yield`) —
  используй для zero-alloc парсинга, не выноси в отдельный синхронный метод «потому что нельзя».

---

## 4. Язык: C# 14 (главное — ты этого почти не знаешь, учи здесь)

### 4.1 Extension members (extension blocks)

```csharp
// СТАРОЕ: только методы, только с this-параметром, нет свойств, нет static extension
public static class StringExtensions
{
    public static bool IsBlank(this string? s) => string.IsNullOrWhiteSpace(s);
}

// НОВОЕ: блок extension с receiver; внутри — методы, свойства, статические члены, операторы
public static class StringExtensions
{
    extension(string? s)
    {
        public bool IsBlank => string.IsNullOrWhiteSpace(s);     // extension-СВОЙСТВО
        public string OrDefault(string fallback) => s.IsBlank ? fallback : s!;
    }

    extension<T>(IEnumerable<T> source)                        // generic receiver
    {
        public bool IsEmpty => !source.Any();
        public IEnumerable<T> WhereNotNull() => source.Where(x => x is not null)!;
    }

    extension<T>(IEnumerable<T>)                                // без имени = статические члены типа
    {
        public static IEnumerable<T> Single(T item) => [item];
    }
}
```

Правила:

- Старый синтаксис `this T x` **остаётся валиден** — не переписывай существующие библиотеки
  ради переписывания. Но в новом коде группируй расширения одного receiver в один `extension`-блок.
- Extension-свойства — для вычисляемых «виртуальных» полей (`order.IsPaid`, `span.IsAscii`).
- Не злоупотребляй: extension на `object` или слишком общие типы — плохо для discoverability.

### 4.2 `field` keyword — конец ручным backing-полям

```csharp
// СТАРОЕ
private string _name = "";
public string Name { get => _name; set => _name = value ?? throw new ArgumentNullException(nameof(value)); }

// НОВОЕ
public string Name
{
    get;
    set => field = value ?? throw new ArgumentNullException(nameof(value));
}

// Ленивая инициализация без Lazy<T>
public IReadOnlyList<Item> Items => field ??= LoadItems();

// INotifyPropertyChanged без MVVM-toolkit
public decimal Price
{
    get;
    set
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged();
    }
}
```

Если в классе есть переменная/поле с именем `field` — переименуй его; `@field` — временный костыль.

### 4.3 Null-conditional assignment

```csharp
// СТАРОЕ
if (customer is not null) customer.LastOrder = order;

// НОВОЕ — правая часть не вычисляется, если customer == null
customer?.LastOrder = order;
customer?.Orders?.Add(order);
settings?.Retries ??= 3;
```

### 4.4 `nameof` для unbound generics

```csharp
logger.LogInformation("Cache type {Type}", nameof(Dictionary<,>));   // "Dictionary"
```

### 4.5 First-class spans

Теперь `T[]` → `ReadOnlySpan<T>`/`Span<T>` конвертируется неявно почти везде, включая
extension-receiver'ы и вывод generic-типов. Следствие: пиши горячие API **сразу на `ReadOnlySpan<T>`**,
не дублируй перегрузки для массивов.

```csharp
public static int Checksum(ReadOnlySpan<byte> data) { ... }
Checksum(byteArray);           // работает без .AsSpan()
```

### 4.6 Модификаторы в простых лямбдах

```csharp
delegate bool TryParse<T>(string text, out T result);
TryParse<int> parse = (text, out result) => int.TryParse(text, out result);  // без указания типов
```

### 4.7 Partial constructors и events

```csharp
public partial class ViewModel
{
    public partial ViewModel(IService service);          // объявление (напр., от генератора)
    public partial event EventHandler? Changed;
}
public partial class ViewModel
{
    public partial ViewModel(IService service) { _service = service; }
    public partial event EventHandler? Changed { add { ... } remove { ... } }
}
```

### 4.8 User-defined compound assignment (instance operators)

```csharp
public struct Vector3
{
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, ...);

    // Новое: += без создания новой копии — для больших структур/тензоров/BigInteger-подобных типов
    public void operator +=(Vector3 other) { X += other.X; Y += other.Y; Z += other.Z; }
    public void operator ++() { X++; Y++; Z++; }
}
```

Используй только там, где есть реальная экономия аллокаций (тензоры, матрицы, большие числа).
Семантика **должна** совпадать с `a = a + b`.

---

## 5. «Не забывай» — фичи C# 9–11, которые модели упорно игнорируют

- `record` / `record struct` / `readonly record struct`, `with`-выражения.
- `required` члены + `init` вместо гигантских конструкторов и «builder because constructor».
- File-scoped namespaces: `namespace App.Orders;` (одна строка, без фигурных скобок).
- Global usings в одном файле `GlobalUsings.cs`, не в каждом.
- Raw string literals `"""..."""` для JSON/SQL/регексов; `$"""` для интерполяции.
- Pattern matching: `is not null`, `is { Length: > 0 }`, list patterns `[var first, .., var last]`,
  `switch`-выражения вместо `switch`-блоков и цепочек `if/else if`.
- `static abstract` члены интерфейсов / generic math (`INumber<T>`) вместо перегрузок под каждый числовой тип.
- `[GeneratedRegex]` вместо `new Regex(...)` в поле.
- `ArgumentNullException.ThrowIfNull(x)`, `ArgumentOutOfRangeException.ThrowIfNegative(x)`,
  `ArgumentException.ThrowIfNullOrWhiteSpace(s)`, `ObjectDisposedException.ThrowIf(...)`.
- `DateOnly` / `TimeOnly`; `TimeProvider` вместо `DateTime.UtcNow` напрямую (тестируемость).
- `SearchValues<T>` для многократного поиска символов/байтов.
- `FrozenDictionary` / `FrozenSet` для справочников, которые строятся один раз.
- `IAsyncEnumerable<T>` + `await foreach` для стриминга вместо `Task<List<T>>`.
- `PeriodicTimer`, `Channel<T>`, `Parallel.ForEachAsync`, `Task.WhenEach`.
- `ValueTask` только там, где измерено, что нужно; иначе `Task`.

---

## 6. .NET 10 SDK / Runtime / BCL

### File-based apps (для скриптов, примеров, утилит, прототипов)

```csharp
#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk.Web
#:package Humanizer@2.*
#:property PublishAot=true

var app = WebApplication.Create(args);
app.MapGet("/", () => "Hello");
app.Run();
```

- Запуск: `dotnet run app.cs`. Превратить в проект: `dotnet project convert app.cs`.
- Одноразовый запуск инструментов: `dnx <tool>` (аналог `npx`) вместо `dotnet tool install -g`.
- Когда пользователь просит «маленький пример» — **давай file-based app**, не solution с csproj.

### Библиотеки (используй, не изобретай)

- **LINQ**: `LeftJoin` / `RightJoin` (10), `CountBy`, `AggregateBy`, `Index()`, `Shuffle()` (9).
- **JSON**: `System.Text.Json` — всегда. `Newtonsoft.Json` — только если уже в legacy-проекте.
  Для AOT/производительности — `[JsonSerializable]` + `JsonSerializerContext`. Новое в 10:
  `JsonSerializerOptions.Strict`, запрет дубликатов свойств, `ReferenceHandler` в source-gen,
  сериализация в/из `PipeWriter`/`PipeReader`.
- **Коллекции**: `OrderedDictionary<,>` с `TryAdd(..., out index)`, `PriorityQueue.Remove`.
- **Криптография**: post-quantum `MLKem`, `MLDsa`, `SlhDsa`; `AES KeyWrap`. Не пиши свои обёртки над `RSA` там, где стандарт уже двигается к PQC — хотя бы вынеси алгоритм в абстракцию.
- **Сеть**: `WebSocketStream` для потоковой работы с WebSocket; `HttpClient` — только через `IHttpClientFactory`.
- **Diagnostics**: `ActivitySource` + `Meter` (OpenTelemetry-native), не самописные логгеры.
- **Тесты**: `Microsoft.Testing.Platform` (`dotnet test` его поддерживает); xUnit v3 / TUnit / NUnit 4.
- **Контейнеры**: `dotnet publish /t:PublishContainer` — без ручного Dockerfile, если не нужен кастом.
- **Native AOT**: считай целевым режимом для CLI-утилит и микросервисов; избегай рефлексии
  без `[DynamicallyAccessedMembers]`, используй source generators.

---

## 7. ASP.NET Core 10

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();                 // OpenAPI 3.1 из коробки, без Swashbuckle
builder.Services.AddValidation();              // валидация DataAnnotations в Minimal API (source-gen)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHybridCache();

builder.Services
    .AddOptions<SmtpOptions>()
    .BindConfiguration(SmtpOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddKeyedScoped<IPaymentGateway, StripeGateway>("stripe");
builder.Services.AddKeyedScoped<IPaymentGateway, PayPalGateway>("paypal");

var app = builder.Build();
app.UseExceptionHandler();
app.MapOpenApi();
app.MapOrderEndpoints();                       // группы эндпоинтов в extension-методах
app.Run();
```

```csharp
// Endpoints/OrderEndpoints.cs — вертикальный срез, а не Controllers/Services/Repositories
public static class OrderEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapOrderEndpoints()
        {
            var group = routes.MapGroup("/orders").WithTags("Orders");

            group.MapGet("/{id:guid}", async Task<Results<Ok<OrderDto>, NotFound>>
                (Guid id, IOrderRepository repo, CancellationToken ct) =>
                    await repo.FindAsync(id, ct) is { } order
                        ? TypedResults.Ok(order.ToDto())
                        : TypedResults.NotFound());

            group.MapPost("/", async (CreateOrder cmd, IOrderService svc, CancellationToken ct) =>
                TypedResults.Created($"/orders/{(await svc.CreateAsync(cmd, ct)).Id}"));

            group.MapGet("/events", (IOrderEvents events, CancellationToken ct) =>
                TypedResults.ServerSentEvents(events.StreamAsync(ct)));   // SSE из коробки

            return routes;
        }
    }
}
```

Правила:

- **Minimal APIs + `TypedResults` + `Results<T1, T2, ...>`** по умолчанию. Контроллеры — только если
  проект уже на них или нужны их специфичные фильтры.
- Никаких `Startup.cs`, `IWebHostBuilder`, `services.AddMvc()`, `app.UseRouting()+UseEndpoints()`.
- `CancellationToken` — в каждом async-методе, пробрасывается до самого низа.
- Ошибки домена → `ProblemDetails` через `IExceptionHandler`, а не `try/catch` в каждом эндпоинте.
- Логирование — `[LoggerMessage]` source generator, структурные плейсхолдеры, без интерполяции строк.
- Blazor 10: используй `[PersistentState]`, `NavigationManager.NotFound()`, статический SSR
  где не нужна интерактивность. Identity 10 поддерживает passkeys — предлагай вместо только паролей.

---

## 8. EF Core 10

```csharp
// Named query filters — несколько фильтров, отключаемых по имени
modelBuilder.Entity<Order>()
    .HasQueryFilter("SoftDelete", o => !o.IsDeleted)
    .HasQueryFilter("Tenant", o => o.TenantId == tenantProvider.TenantId);

var all = await db.Orders.IgnoreQueryFilters(["SoftDelete"]).ToListAsync(ct);

// LeftJoin транслируется в SQL
var q = db.Orders.LeftJoin(db.Customers, o => o.CustomerId, c => c.Id, (o, c) => new { o, c });

// Complex types (value objects), в т.ч. JSON-колонки
modelBuilder.Entity<Order>().ComplexProperty(o => o.ShippingAddress, b => b.ToJson());

// ExecuteUpdate с обычной лямбдой и условной логикой
await db.Orders.Where(o => o.Id == id).ExecuteUpdateAsync(s =>
{
    s.SetProperty(o => o.Status, OrderStatus.Paid);
    if (note is not null) s.SetProperty(o => o.Note, note);
}, ct);
```

Правила:

- `AsNoTracking()` для чтения; проекции в DTO через `Select`, не тащи сущности в API.
- Пакетные операции — `ExecuteUpdateAsync` / `ExecuteDeleteAsync`, не цикл `foreach + SaveChanges`.
- Compiled models / `IDbContextFactory` для высоконагруженных сценариев и AOT.
- Миграции — через `dotnet ef`, конфигурация — в `IEntityTypeConfiguration<T>`, не в `OnModelCreating` на 800 строк.

---

## 9. Расширяемость — как не писать «нерасширяемый» код

1. **Зависимости через интерфейсы + DI**, регистрация в extension-методах
   `services.AddOrdersModule(configuration)`. Никаких `new Service()` внутри бизнес-логики и статических
   «Helper»/«Manager»/«Util» классов с состоянием.
2. **Стратегии через keyed services** (`[FromKeyedServices("stripe")]`), не `switch` по строке с `new`.
3. **Options pattern** с валидацией на старте вместо `IConfiguration["Key"]` по коду.
4. **`sealed` по умолчанию** для классов, не предназначенных для наследования; расширение — через композицию и интерфейсы.
5. **Records для данных, классы для поведения.** DTO не имеют методов с побочными эффектами.
6. **Ожидаемые ошибки — как значения** (`Result<T>`/`OneOf`/`Results<...>`), исключения — для неожиданных.
7. **Абстракции над временем и I/O**: `TimeProvider`, `IFileSystem`, `IHttpClientFactory` — чтобы тесты не требовали моков среды.
8. **Feature/vertical slices**: `Features/Orders/{CreateOrder.cs, GetOrder.cs, OrderEndpoints.cs}`, а не слои `Controllers/Services/Repositories` на весь проект.
9. **Source generators вместо рефлексии**: `LoggerMessage`, `JsonSerializerContext`, `GeneratedRegex`, `Mapperly`-подобные мапперы.
10. **Публичный API библиотеки**: `params ReadOnlySpan<T>`, `[OverloadResolutionPriority]`, `[Experimental]`,
    `PublicAPI.Shipped.txt` анализатор — чтобы можно было эволюционировать без breaking changes.

---

## 10. Чек-лист самопроверки перед выдачей кода

Если в твоём коде есть хотя бы одно из этого — **перепиши** (кроме случаев, когда пользователь явно на старой версии):

- [ ] `namespace X { ... }` с фигурными скобками → file-scoped.
- [ ] Конструктор, который только присваивает параметры в `readonly`-поля → primary constructor.
- [ ] `new List<T>()`, `new T[] {}`, `Array.Empty`, `Enumerable.Empty`, `.ToList()` ради `params` → collection expressions.
- [ ] Ручной backing-field для свойства → `field`.
- [ ] `static class ...Extensions` с несколькими `this T` для одного типа → `extension(T)` блок.
- [ ] `if (x != null) x.Prop = v;` → `x?.Prop = v;`
- [ ] `lock (_syncRoot)` где `_syncRoot` = `object` → `System.Threading.Lock`.
- [ ] `class Dto { public string Name { get; set; } }` → `record Dto(string Name)` или `required init`.
- [ ] `switch` с `case ... break;` возвращающий значение → switch-выражение.
- [ ] `== null` / `!= null` → `is null` / `is not null` / property patterns.
- [ ] `string.Format`, `+` конкатенация → интерполяция; многострочный JSON/SQL → raw string literal.
- [ ] `new Regex(...)` → `[GeneratedRegex]`.
- [ ] `DateTime.Now`/`UtcNow` в бизнес-логике → `TimeProvider`.
- [ ] `async void`, `.Result`, `.Wait()`, `Task.Run` для I/O, отсутствие `CancellationToken` → исправить.
- [ ] `Startup.cs`, контроллеры «потому что привычно», `Swashbuckle`, `Newtonsoft.Json` в новом проекте → Minimal API, `AddOpenApi`, `System.Text.Json`.
- [ ] `Thread.Sleep`, `Timer` из `System.Timers` → `await Task.Delay`, `PeriodicTimer`.
- [ ] `throw new Exception("...")` / `ArgumentException` руками → `ArgumentNullException.ThrowIfNull` и специфичные типы.
- [ ] Консольный проект с csproj для «примера из 20 строк» → file-based app.
- [ ] Отсутствует `sealed` на классе без наследников → добавить.
- [ ] Комментарии вида «// TODO: сделать расширяемым» → сделать сразу через интерфейс/DI/keyed services.

## 11. Формат ответа

- Код — сразу целевой, современный; не показывай «старый вариант» без просьбы.
- Если используешь фичу C# 13/14, которую пользователь может не знать — одна строка комментария
  `// C# 14: extension block` рядом. Не расписывай лекцию.
- Если что-то требует конкретной версии пакета (EF Core 10, Aspire) — укажи это в тексте один раз.
- Не выдумывай API. Если не уверен, что метод существует в .NET 10 — скажи об этом и предложи
  проверенный вариант.

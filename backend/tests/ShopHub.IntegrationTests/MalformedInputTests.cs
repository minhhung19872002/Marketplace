using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ShopHub.Application.Security;
using ShopHub.IntegrationTests.Infrastructure;

namespace ShopHub.IntegrationTests;

/// <summary>
/// Bodies with missing or null fields must come back as a Vietnamese 4xx, never a 500 (a validator rule running on a
/// null list used to throw).
/// </summary>
[Collection(ApiCollection.Name)]
public class MalformedInputTests(ApiFactory factory)
{
    [Fact]
    public async Task Empty_and_null_bodies_never_cause_a_server_error()
    {
        var store = await factory.CreateStoreAsync(products: [new("Bút Chì", "Đèn Bàn", 10_000, 5, "Việt Nam")]);
        var user = await factory.CreateUserAsync(Permissions.All);
        var shop = store.ShopId;
        var product = store.Products["Bút Chì"];
        await factory.WithDbAsync(async db =>
        {
            db.ShopStaff.Add(new Domain.Shops.ShopStaff(shop, user.Id, Domain.Shops.ShopStaffRole.Manager, ShopPermissions.All));
            await db.SaveChangesAsync();
        });

        (HttpMethod Method, string Path)[] endpoints =
        [
            (HttpMethod.Post, $"/api/seller/shops/{shop}/products"),
            (HttpMethod.Put, $"/api/seller/shops/{shop}/products/{product}"),
            (HttpMethod.Post, "/api/seller/shops"),
            (HttpMethod.Post, $"/api/seller/shops/{shop}/vouchers"),
            (HttpMethod.Post, $"/api/seller/shops/{shop}/orders/prepare"),
            (HttpMethod.Post, "/api/cart/items"),
            (HttpMethod.Post, "/api/cart/items/remove"),
            (HttpMethod.Post, "/api/checkout/quote"),
            (HttpMethod.Post, "/api/checkout"),
            (HttpMethod.Post, "/api/account/addresses"),
            (HttpMethod.Post, "/api/admin/vouchers"),
            (HttpMethod.Post, "/api/admin/categories"),
            (HttpMethod.Post, $"/api/admin/users/{user.Id}/coins"),
            (HttpMethod.Post, "/api/auth/register"),
        ];
        string[] bodies = ["{}", """{"input":{},"version":0}""", """{"checkout":{},"expectedGrandTotal":0}""", "null"];

        var failures = new List<string>();
        foreach (var (method, path) in endpoints)
            foreach (var body in bodies)
            {
                var msg = new HttpRequestMessage(method, path) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
                msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                var res = await user.Client.SendAsync(msg);
                if ((int)res.StatusCode >= 500) failures.Add($"{method} {path} {body} → {(int)res.StatusCode}");
            }

        failures.Should().BeEmpty(string.Join(" | ", failures));
    }

    [Fact]
    public async Task A_null_code_with_an_otherwise_valid_body_is_a_400_not_a_500()
    {
        var user = await factory.CreateUserAsync(Permissions.All);
        // A code exists for each target, so the handler would reach the comparison with the (missing) code
        var newcomer = ApiFactory.NewPhone();
        var newContact = ApiFactory.NewPhone();
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/otp/send", new { target = newcomer, purpose = "Register" })).EnsureSuccessStatusCode();
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/otp/send", new { target = user.Phone, purpose = "Login" })).EnsureSuccessStatusCode();
        (await user.Client.PostAsJsonAsync("/api/account/contact/otp", new { newValue = newContact })).EnsureSuccessStatusCode();
        (HttpMethod Method, string Path, string Body, bool Signed)[] cases =
        [
            (HttpMethod.Post, "/api/auth/otp/verify", $$"""{"target":"{{newcomer}}","purpose":"Register","code":null}""", false),
            (HttpMethod.Post, "/api/auth/login-otp", $$"""{"phone":"{{user.Phone}}","code":null}""", false),
            (HttpMethod.Put, "/api/account/contact", $$"""{"newValue":"{{newContact}}","code":null}""", true),
            (HttpMethod.Post, "/api/admin/roles", """{"code":null,"name":"Vai trò thử","description":"x","permissions":[]}""", true),
        ];
        var failures = new List<string>();
        foreach (var (method, path, body, signed) in cases)
        {
            var msg = new HttpRequestMessage(method, path) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            var res = await (signed ? user.Client : factory.CreateClient()).SendAsync(msg);
            if (res.StatusCode != System.Net.HttpStatusCode.BadRequest) failures.Add($"{method} {path} → {(int)res.StatusCode}");
        }
        failures.Should().BeEmpty(string.Join(" | ", failures));
    }
}

using FluentAssertions;
using ShopHub.Infrastructure.Seed;
using Shot = ShopHub.Infrastructure.Seed.ProductGenerator.Shot;

namespace ShopHub.UnitTests.Application;

// G-VIS round 3 (L198): a close-up cut from a photo led 325 of ~1,000 sample listings — the card showed half a Rubik's cube
public class GalleryOrderTests
{
    private static readonly Shot Cover = new(Guid.NewGuid(), "p001-ad", true);
    private static readonly Shot A = new(Guid.NewGuid(), "p001-0", false);
    private static readonly Shot B = new(Guid.NewGuid(), "p001-1", false);
    private static readonly Shot Zoom1 = new(Guid.NewGuid(), "m001-zoom1", false, true);
    private static readonly Shot Zoom2 = new(Guid.NewGuid(), "m001-zoom2", false, true);

    [Fact]
    public void A_close_up_never_leads_and_always_comes_last()
    {
        for (var copy = 0; copy < 8; copy++)
        {
            var order = ProductGenerator.GalleryOrder([Cover, A, B, Zoom1, Zoom2], copy);
            order[0].CloseUp.Should().BeFalse($"bản {copy}");
            order.TakeLast(2).Should().Equal(Zoom1, Zoom2);
            order.Should().HaveCount(5);
        }
    }

    [Fact]
    public void Listings_differ_by_cover_and_turned_originals()
    {
        ProductGenerator.GalleryOrder([Cover, A, B, Zoom1], 0).Take(3).Should().Equal(Cover, A, B);
        ProductGenerator.GalleryOrder([Cover, A, B, Zoom1], 1).Take(3).Should().Equal(B, A, Cover);
        ProductGenerator.GalleryOrder([Cover, A, B, Zoom1], 2).Take(3).Should().Equal(Cover, A, B);
    }

    [Fact]
    public void A_model_with_one_photo_and_close_ups_shows_the_photo_first()
    {
        ProductGenerator.GalleryOrder([A, Zoom1, Zoom2], 1)[0].Should().Be(A);
    }
}

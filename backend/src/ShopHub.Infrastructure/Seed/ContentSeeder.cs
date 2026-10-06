using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Features.Admin;
using ShopHub.Domain.SystemConfig;
using ShopHub.Infrastructure.Persistence;

namespace ShopHub.Infrastructure.Seed;

/// <summary>
/// Reference content (always, like the carriers): legal pages, help articles and the editable message templates.
/// Each part checks its own rows; admins edit them afterwards (VI.8) and re-runs never overwrite an edit.
/// </summary>
public sealed class ContentSeeder(ShopHubDbContext db, IClock clock, ILogger<ContentSeeder> logger)
{
    private sealed record Page(CmsKind Kind, string Slug, string Title, string? Topic, int Order, string Html);

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var existing = await db.CmsPages.Select(p => p.Slug).ToListAsync(ct);
        var pages = Pages.Where(p => !existing.Contains(p.Slug)).ToList();
        foreach (var p in pages) db.CmsPages.Add(new CmsPage(p.Kind, p.Slug, p.Title, p.Html, p.Topic, p.Order, true, now));

        var templates = await db.MessageTemplates.Select(t => new { t.Key, t.Channel }).ToListAsync(ct);
        var missing = TemplateCatalog.All.Where(d => !templates.Any(t => t.Key == d.Key && t.Channel == d.Channel)).ToList();
        foreach (var d in missing) db.MessageTemplates.Add(new MessageTemplate(d.Key, d.Channel, d.Name, d.Subject, d.Body, d.Placeholders, now));

        if (pages.Count + missing.Count == 0) return;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Pages} content page(s) and {Templates} message template(s)", pages.Count, missing.Count);
    }

    private static readonly Page[] Pages =
    [
        new(CmsKind.Page, "dieu-khoan-su-dung", "Điều khoản sử dụng", null, 1, """
            <h2>1. Phạm vi áp dụng</h2>
            <p>Điều khoản này áp dụng cho mọi người dùng truy cập và sử dụng sàn thương mại điện tử ShopHub, bao gồm người mua, người bán và khách vãng lai. Khi đăng ký tài khoản hoặc đặt hàng, bạn đồng ý tuân thủ Điều khoản sử dụng, Quy chế hoạt động và Chính sách bảo mật của sàn.</p>
            <h2>2. Tài khoản</h2>
            <p>Bạn chịu trách nhiệm về tính chính xác của thông tin đăng ký và bảo mật mật khẩu, mã OTP. Không chia sẻ mã OTP cho bất kỳ ai, kể cả người tự xưng là nhân viên ShopHub. Sàn có quyền tạm khoá tài khoản có dấu hiệu gian lận, lạm dụng khuyến mãi hoặc vi phạm pháp luật.</p>
            <h2>3. Giao dịch</h2>
            <p>Hợp đồng mua bán được lập giữa người mua và người bán; ShopHub là đơn vị cung cấp nền tảng, giữ tiền thanh toán của người mua cho tới khi đơn hoàn thành rồi mới thanh toán cho người bán. Giá, phí vận chuyển và giảm giá hiển thị tại bước thanh toán là giá cuối cùng người mua phải trả.</p>
            <h2>4. Nội dung do người dùng đăng</h2>
            <p>Người dùng không được đăng nội dung vi phạm pháp luật, hàng giả, hàng cấm, nội dung xúc phạm hoặc thông tin liên hệ nhằm giao dịch ngoài sàn. Sàn có quyền ẩn, gỡ nội dung và ghi điểm phạt đối với người bán vi phạm.</p>
            <h2>5. Giới hạn trách nhiệm</h2>
            <p>ShopHub không chịu trách nhiệm đối với giao dịch thực hiện ngoài nền tảng. Mọi khiếu nại liên quan tới đơn hàng trên sàn được giải quyết theo Chính sách trả hàng & hoàn tiền.</p>
            <h2>6. Sửa đổi</h2>
            <p>Điều khoản có thể được cập nhật; phiên bản mới có hiệu lực kể từ khi đăng trên trang này.</p>
            """),
        new(CmsKind.Page, "quy-che-hoat-dong", "Quy chế hoạt động sàn", null, 2, """
            <h2>1. Nguyên tắc chung</h2>
            <p>ShopHub hoạt động theo mô hình sàn giao dịch thương mại điện tử theo Nghị định 52/2013/NĐ-CP và Nghị định 85/2021/NĐ-CP. Người bán được xác minh danh tính (CCCD hoặc giấy phép kinh doanh) trước khi bán hàng.</p>
            <h2>2. Quy trình giao dịch</h2>
            <ol><li>Người mua chọn sản phẩm, đặt hàng và thanh toán (COD, ví điện tử, thẻ, Ví ShopHub).</li><li>Người bán xác nhận, đóng gói và giao cho đơn vị vận chuyển.</li><li>Người mua nhận hàng, bấm "Đã nhận được hàng" hoặc đơn tự hoàn thành sau thời hạn quy định.</li><li>Sàn giải ngân cho người bán sau khi trừ phí theo biểu phí công bố.</li></ol>
            <h2>3. Điểm phạt người bán</h2>
            <p>Người bán giao hàng trễ, huỷ đơn không lý do chính đáng, đăng hàng vi phạm sẽ bị ghi điểm phạt. Điểm phạt có thời hạn; vượt ngưỡng sẽ bị hạn chế hiển thị, cấm tham gia chiến dịch hoặc khoá shop.</p>
            <h2>4. Giải quyết tranh chấp</h2>
            <p>Tranh chấp giữa người mua và người bán được giải quyết trước hết qua thương lượng; nếu không đạt thoả thuận, người mua gửi khiếu nại lên sàn, sàn xem xét bằng chứng của hai bên và ra quyết định cuối cùng.</p>
            <h2>5. Bảo vệ người tiêu dùng</h2>
            <p>Tiền của người mua được sàn giữ hộ cho tới khi đơn hoàn thành; mọi yêu cầu hoàn tiền hợp lệ được hoàn về đúng nguồn thanh toán.</p>
            """),
        new(CmsKind.Page, "chinh-sach-bao-mat", "Chính sách bảo mật & xử lý dữ liệu cá nhân", null, 3, """
            <h2>1. Dữ liệu thu thập</h2>
            <p>ShopHub thu thập họ tên, số điện thoại, email, địa chỉ nhận hàng, lịch sử mua hàng và thông tin thiết bị đăng nhập để cung cấp dịch vụ. Số tài khoản ngân hàng và số giấy tờ tuỳ thân được mã hoá khi lưu trữ.</p>
            <h2>2. Mục đích xử lý</h2>
            <p>Dữ liệu được dùng để xử lý đơn hàng, giao hàng, thanh toán, hỗ trợ khách hàng, phòng chống gian lận và gửi thông báo theo lựa chọn của bạn (có thể tắt từng loại trong Cài đặt thông báo).</p>
            <h2>3. Chia sẻ dữ liệu</h2>
            <p>Thông tin nhận hàng được chia sẻ cho người bán và đơn vị vận chuyển của đơn đó. ShopHub không bán dữ liệu cá nhân cho bên thứ ba.</p>
            <h2>4. Quyền của chủ thể dữ liệu</h2>
            <p>Theo Nghị định 13/2023/NĐ-CP, bạn có quyền được biết, đồng ý, truy cập, chỉnh sửa, yêu cầu xoá dữ liệu và rút lại sự đồng ý. Bạn có thể tải dữ liệu của mình hoặc yêu cầu xoá tài khoản trong mục Tài khoản.</p>
            <h2>5. Lưu trữ</h2>
            <p>Dữ liệu đơn hàng được lưu theo thời hạn kế toán bắt buộc; khi xoá tài khoản, thông tin cá nhân được ẩn danh hoá.</p>
            """),
        new(CmsKind.Page, "chinh-sach-tra-hang", "Chính sách trả hàng & hoàn tiền", null, 4, """
            <h2>1. Thời hạn</h2>
            <p>Người mua có thể yêu cầu trả hàng / hoàn tiền trong vòng 15 ngày kể từ khi đơn được giao thành công.</p>
            <h2>2. Trường hợp áp dụng</h2>
            <ul><li>Thiếu hàng, sai hàng, hàng hư hỏng.</li><li>Hàng không giống mô tả.</li><li>Hàng giả, hàng nhái.</li></ul>
            <h2>3. Quy trình</h2>
            <p>Chọn sản phẩm và số lượng, lý do, ảnh / video bằng chứng và hình thức Chỉ hoàn tiền hoặc Trả hàng & hoàn tiền. Người bán phản hồi trong thời hạn quy định; nếu người bán từ chối, người mua có thể khiếu nại để sàn phân xử.</p>
            <h2>4. Số tiền hoàn</h2>
            <p>Số tiền hoàn là phần người mua thực trả cho các sản phẩm trả lại sau khi đã trừ giảm giá; hoàn về đúng nguồn: thanh toán online hoàn qua cổng, COD và Ví ShopHub hoàn vào Ví ShopHub, xu đã dùng được trả lại.</p>
            """),
        new(CmsKind.Page, "huong-dan-mua-hang", "Hướng dẫn mua hàng", null, 5, """
            <ol><li>Tìm sản phẩm bằng ô tìm kiếm (gõ không dấu vẫn ra) hoặc theo danh mục.</li><li>Chọn phân loại, số lượng rồi bấm "Thêm vào giỏ hàng" hoặc "Mua ngay".</li><li>Trong giỏ, tick các sản phẩm muốn mua; mỗi shop là một đơn riêng.</li><li>Ở trang thanh toán, chọn địa chỉ, đơn vị vận chuyển, voucher, ShopHub Xu và phương thức thanh toán.</li><li>Theo dõi đơn tại Tài khoản → Đơn mua; bấm "Đã nhận được hàng" khi nhận đủ hàng.</li></ol>
            """),
        new(CmsKind.Help, "tro-giup-quen-mat-khau", "Tôi quên mật khẩu, phải làm sao?", "Tài khoản", 1,
            "<p>Tại trang đăng nhập bấm <strong>Quên mật khẩu</strong>, nhập số điện thoại hoặc email đã đăng ký, nhập mã OTP rồi đặt mật khẩu mới. Đổi mật khẩu sẽ đăng xuất mọi thiết bị khác.</p>"),
        new(CmsKind.Help, "tro-giup-xoa-tai-khoan", "Làm sao để xoá tài khoản?", "Tài khoản", 2,
            "<p>Vào Tài khoản → Hồ sơ → Yêu cầu xoá tài khoản. Tài khoản còn đơn chưa hoàn tất hoặc còn số dư Ví ShopHub thì chưa xoá được.</p>"),
        new(CmsKind.Help, "tro-giup-huy-don", "Tôi muốn huỷ đơn hàng", "Mua hàng", 1,
            "<p>Đơn đang <strong>Chờ xác nhận</strong> bạn tự huỷ được ngay. Khi shop đã xác nhận, bạn gửi yêu cầu huỷ; shop có 24 giờ để phản hồi, quá hạn hệ thống tự chấp thuận.</p>"),
        new(CmsKind.Help, "tro-giup-voucher", "Vì sao tôi không dùng được voucher?", "Mua hàng", 2,
            "<p>Voucher có điều kiện đơn tối thiểu, thời gian, số lượt và phạm vi áp dụng. Ở màn chọn voucher, mã không dùng được hiển thị kèm lý do, ví dụ \"Mua thêm ₫35.000 để dùng mã này\".</p>"),
        new(CmsKind.Help, "tro-giup-thanh-toan-that-bai", "Thanh toán online thất bại nhưng đã bị trừ tiền", "Thanh toán", 1,
            "<p>Hệ thống tự đối chiếu với cổng thanh toán. Nếu cổng xác nhận đã trừ tiền, đơn sẽ được ghi nhận đã thanh toán; nếu đơn đã huỷ, tiền được hoàn về nguồn thanh toán.</p>"),
        new(CmsKind.Help, "tro-giup-vi-shophub", "Ví ShopHub dùng để làm gì?", "Thanh toán", 2,
            "<p>Ví ShopHub nhận tiền hoàn của đơn COD, dùng để thanh toán đơn hàng (cần mật khẩu ví 6 số) và rút về tài khoản ngân hàng đã xác minh.</p>"),
        new(CmsKind.Help, "tro-giup-theo-doi-van-don", "Theo dõi đơn hàng đang giao ở đâu?", "Vận chuyển", 1,
            "<p>Vào Đơn mua → chi tiết đơn để xem hành trình, hoặc nhập mã vận đơn tại trang Tra cứu vận đơn.</p>"),
        new(CmsKind.Help, "tro-giup-tra-hang", "Hàng nhận được bị lỗi, tôi phải làm gì?", "Trả hàng & hoàn tiền", 1,
            "<p>Trong 15 ngày kể từ khi nhận hàng, vào chi tiết đơn → Trả hàng/Hoàn tiền, chọn sản phẩm, lý do và đính kèm ảnh / video. Xem thêm Chính sách trả hàng & hoàn tiền.</p>"),
        new(CmsKind.Help, "tro-giup-dang-ky-ban-hang", "Đăng ký bán hàng trên ShopHub", "Người bán", 1,
            "<p>Vào Kênh Người Bán → Đăng ký shop, khai thông tin shop, kho lấy hàng, giấy tờ (CCCD hoặc giấy phép kinh doanh) và tài khoản ngân hàng. Sàn duyệt trong 1–2 ngày làm việc.</p>"),
        new(CmsKind.Help, "tro-giup-diem-phat", "Điểm phạt người bán là gì?", "Người bán", 2,
            "<p>Điểm phạt ghi cho người bán giao trễ, huỷ đơn, đăng hàng vi phạm. Điểm có thời hạn; vượt ngưỡng sẽ bị hạn chế hiển thị, cấm tham gia chiến dịch hoặc khoá shop. Xem tại Kênh Người Bán → Dữ liệu & phân tích → Hiệu quả hoạt động.</p>"),
    ];
}

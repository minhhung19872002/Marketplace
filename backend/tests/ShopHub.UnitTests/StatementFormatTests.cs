using FluentAssertions;
using ShopHub.Application.Features.Finance;
using ShopHub.Domain.Common;
using ShopHub.Domain.Sales;
using ShopHub.UnitTests.SourceScan;
using Xunit;

namespace ShopHub.UnitTests;

/// <summary>Phase 14 A4: each gateway's statement file (fixtures in backend/tests/fixtures/statements) is read by its own columns.</summary>
public class StatementFormatTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoFiles.BackendRoot, "tests", "fixtures", "statements", name));

    [Fact]
    public void Vnpay_export_reads_dotted_amounts_fees_refund_rows_and_reports_a_repeated_transaction()
    {
        var s = GatewayStatementFormats.Parse(PaymentMethod.VnPay, Fixture("vnpay.csv"));
        s.Lines.Should().Be(4);
        s.Payments.Select(p => (p.TxnId, p.Amount, p.Refunded, p.Fee)).Should().Equal(("14000003", 1_250_000L, 0L, 13_750L), ("14000004", 300_000L, 100_000L, 3_300L));
        s.Duplicates.Should().ContainSingle().Which.Should().Be((5, "14000003"));
        s.BadLines.Should().BeEmpty();
    }

    [Fact]
    public void Momo_refund_row_points_at_the_original_transaction_and_counts_as_a_positive_refund()
    {
        var s = GatewayStatementFormats.Parse(PaymentMethod.MoMo, Fixture("momo.csv"));
        s.Payments.Select(p => (p.TxnId, p.Amount, p.Refunded)).Should().Equal(("3100000001", 200_000L, 0L), ("3100000002", 150_000L, 150_000L));
    }

    [Fact]
    public void Zalopay_refunded_column_is_read_and_an_unreadable_line_is_reported()
    {
        var s = GatewayStatementFormats.Parse(PaymentMethod.ZaloPay, Fixture("zalopay.csv"));
        s.Payments.Select(p => (p.TxnId, p.Refunded)).Should().Equal(("261007000001", 0L), ("261007000002", 90_000L));
        s.BadLines.Should().ContainSingle().Which.Line.Should().Be(4);
    }

    [Fact]
    public void A_file_of_another_gateway_is_refused_with_a_clear_message()
    {
        // The simulated gateway's own file has no "Mã giao dịch MoMo" column
        var act = () => GatewayStatementFormats.Parse(PaymentMethod.MoMo, "txn_id,payment_id,amount,refunded_amount,paid_at\nSIM1,x,1000,0,2026-10-07");
        act.Should().Throw<BusinessRuleException>().WithMessage("*MoMo*thiếu cột*");
        GatewayStatementFormats.Parse(PaymentMethod.Simulated, "txn_id,payment_id,amount,refunded_amount,paid_at\nSIM1,x,1000,0,2026-10-07")
            .Payments.Should().ContainSingle(p => p.TxnId == "SIM1" && p.Amount == 1000);
    }
}

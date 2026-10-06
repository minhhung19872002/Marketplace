using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;

namespace ShopHub.Application.Features.Payments;

public record WebhookReply(bool Accepted, string Body, string Result, int StatusCode);

/// <summary>Entry for every gateway notification: verify the signature with the gateway's own rules, then process once.</summary>
public sealed class PaymentWebhookIntake(IPaymentGatewayRegistry gateways, PaymentProcessor processor)
{
    public async Task<WebhookReply> HandleAsync(string provider, InboundWebhook webhook, CancellationToken ct)
    {
        var gateway = gateways.ForProvider(provider) ?? throw new NotFoundException("Không tìm thấy cổng thanh toán.");
        var callback = gateway.VerifyCallback(webhook);
        if (callback is null) return new WebhookReply(false, gateway.Acknowledge(false), "INVALID_SIGNATURE", gateway.AcknowledgeStatus(false));
        var result = await processor.ProcessAsync(gateway.Provider, callback, ct);
        return new WebhookReply(true, gateway.Acknowledge(true, result), result, gateway.AcknowledgeStatus(true));
    }
}

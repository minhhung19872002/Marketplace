using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

// Messages "sent" by the simulated SMS provider — lets OTP flows be demoed and tested without a contract
public class SimulatedSms : Entity
{
    private SimulatedSms() { }

    public SimulatedSms(string to, string content, DateTimeOffset createdAt)
    {
        To = to;
        Content = content;
        CreatedAt = createdAt;
    }

    public string To { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}

namespace ShopHub.Domain.Common;

/// <summary>Business rule violated — mapped to HTTP 409 with the Vietnamese message.</summary>
public class BusinessRuleException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

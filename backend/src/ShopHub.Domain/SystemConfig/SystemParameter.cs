using System.Globalization;
using System.Text.Json;
using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

public enum ParameterDataType
{
    String,
    Int,
    Bool,
    Cron,
    Json,
}

// Platform-wide setting (name, hotline, fees, job schedules…) editable from the admin screen
public class SystemParameter : AuditableEntity
{
    private SystemParameter() { }

    public SystemParameter(string key, string value, ParameterDataType dataType, string group, string name, string description)
    {
        Key = key;
        DataType = dataType;
        Group = group;
        Name = name;
        Description = description;
        SetValue(value);
    }

    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public ParameterDataType DataType { get; private set; }
    public string Group { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    // Optimistic concurrency token (PostgreSQL xmin)
    public uint Version { get; private set; }

    public void SetValue(string value)
    {
        var error = Validate(DataType, value);
        if (error is not null) throw new BusinessRuleException(error, "PARAMETER_INVALID_VALUE");
        Value = value.Trim();
    }

    /// <summary>Returns a Vietnamese error message, or null when the value fits the data type.</summary>
    public static string? Validate(ParameterDataType type, string? value)
    {
        if (value is null) return "Giá trị không được để trống.";
        var v = value.Trim();
        return type switch
        {
            ParameterDataType.Int when !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                => "Giá trị phải là số nguyên.",
            ParameterDataType.Bool when v is not ("true" or "false")
                => "Giá trị phải là true hoặc false.",
            ParameterDataType.Cron when v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is not (5 or 6)
                => "Biểu thức lịch chạy (cron) phải có 5 hoặc 6 trường.",
            ParameterDataType.Json when !IsJson(v)
                => "Giá trị phải là JSON hợp lệ.",
            _ => null,
        };
    }

    private static bool IsJson(string v)
    {
        try
        {
            using var _ = JsonDocument.Parse(v);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

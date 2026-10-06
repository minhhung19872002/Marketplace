using ShopHub.Domain.Common;

namespace ShopHub.Domain.SystemConfig;

public enum CmsKind
{
    Page,  // trang tĩnh / pháp lý: Điều khoản, Quy chế hoạt động, Chính sách bảo mật…
    Help,  // câu hỏi thường gặp của Trung tâm trợ giúp, nhóm theo chủ đề
}

/// <summary>Static page or help article (spec II.12, VI.8). Content is HTML sanitised before it is stored.</summary>
public class CmsPage : Entity
{
    private CmsPage() { }

    public CmsPage(CmsKind kind, string slug, string title, string content, string? topic, int sortOrder, bool isPublished, DateTimeOffset now)
    {
        Kind = kind;
        Slug = slug;
        Update(title, content, topic, sortOrder, isPublished, now);
    }

    public CmsKind Kind { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    // Help center topic ("Mua hàng", "Thanh toán"…); null for static pages
    public string? Topic { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsPublished { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string title, string content, string? topic, int sortOrder, bool isPublished, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new BusinessRuleException("Vui lòng nhập tiêu đề.");
        if (string.IsNullOrWhiteSpace(content)) throw new BusinessRuleException("Vui lòng nhập nội dung.");
        Title = title.Trim();
        Content = content;
        Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
        SortOrder = sortOrder;
        IsPublished = isPublished;
        UpdatedAt = now;
    }
}

public enum TemplateChannel
{
    Sms,
    Email,
}

/// <summary>
/// Editable text of a message ShopHub sends (spec VI.8): placeholders {{name}} are filled at send time; a missing
/// template falls back to the built-in text so sending never stops.
/// </summary>
public class MessageTemplate : Entity
{
    private MessageTemplate() { }

    public MessageTemplate(string key, TemplateChannel channel, string name, string? subject, string body, string placeholders, DateTimeOffset now)
    {
        Key = key;
        Channel = channel;
        Name = name;
        Placeholders = placeholders;
        Update(subject, body, now);
    }

    public string Key { get; private set; } = string.Empty;
    public TemplateChannel Channel { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Subject { get; private set; }
    public string Body { get; private set; } = string.Empty;
    // Comma-separated names the body may use, e.g. "code,minutes"
    public string Placeholders { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string? subject, string body, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new BusinessRuleException("Nội dung mẫu không được để trống.");
        if (Channel == TemplateChannel.Email && string.IsNullOrWhiteSpace(subject)) throw new BusinessRuleException("Mẫu email cần tiêu đề.");
        var allowed = Placeholders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body + subject, @"\{\{(\w+)\}\}"))
            if (!allowed.Contains(m.Groups[1].Value)) throw new BusinessRuleException($"Biến {{{{{m.Groups[1].Value}}}}} không có trong mẫu này.");
        Subject = subject?.Trim();
        Body = body;
        UpdatedAt = now;
    }

    public (string? Subject, string Body) Render(IReadOnlyDictionary<string, string> values)
    {
        string Fill(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text, @"\{\{(\w+)\}\}", m => values.GetValueOrDefault(m.Groups[1].Value) ?? "");
        return (Subject is null ? null : Fill(Subject), Fill(Body));
    }
}

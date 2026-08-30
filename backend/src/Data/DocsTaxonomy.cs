namespace LiaraDocsAssistant.Data;

public static class DocsTaxonomy
{
    public sealed record Category(string Id, string PathSegment, string LabelFa, string LabelEn);

    public static readonly IReadOnlyList<Category> Categories =
    [
        new("paas", "paas", "پلتفرم به‌عنوان سرویس", "PaaS"),
        new("ai", "ai", "هوش مصنوعی", "AI"),
        new("iaas", "iaas", "زیرساخت به‌عنوان سرویس", "IaaS"),
        new("dbaas", "dbaas", "پایگاه‌داده به‌عنوان سرویس", "DBaaS"),
        new("mail", "email-server", "سرور ایمیل", "Email Server"),
        new("dns", "dns-management-system", "مدیریت DNS", "DNS Management"),
        new("object_storage", "object-storage", "فضای ذخیره‌سازی ابری", "Object Storage"),
        new("one_click_app", "one-click-apps", "اپلیکیشن‌های یک‌کلیکی", "One-Click Apps"),
        new("references", "references", "مرجع API", "References"),
        new("overview", "overview", "معرفی کلی", "Overview"),
    ];

    public static string? MatchPathSegment(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return null;
        }

        var trimmed = absolutePath.Split('?', '#')[0].Trim('/');
        var firstSegment = trimmed.Split('/')[0];

        return Categories.FirstOrDefault(c =>
            firstSegment.Equals(c.PathSegment, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}

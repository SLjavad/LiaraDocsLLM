namespace LiaraDocsAssistant.Retrieval.Routing;

/// <summary>
/// Literal static refusal/trivial templates from specs/04-prompts.md §4 —
/// never model-generated. Keyed by reason × locale. The escalation fallback
/// is appended (not standalone) and used by the chat flow (Phase 4).
/// </summary>
public static class RefusalTemplates
{
    public const string TrivialEn = "Hi! I'm the Liara Docs Assistant — ask me anything about deploying, configuring, or troubleshooting Liara's services, or switch to Practice Mode to test yourself on a topic.";
    public const string TrivialFa = "سلام! من دستیار مستندات لیارا هستم — هر سوالی درباره استقرار، پیکربندی یا رفع مشکل سرویس‌های لیارا دارید بپرسید، یا برای آزمون گرفتن از خودتان به حالت تمرین بروید.";

    public const string MetaQuestionEn = "I can't share details about the model or system behind me — but I'm happy to help with anything about Liara's services. What would you like to know?";
    public const string MetaQuestionFa = "نمی‌توانم درباره مدل یا سیستم پشت خودم جزئیاتی ارائه بدهم — اما خوشحال می‌شوم درباره سرویس‌های لیارا کمکتان کنم. چه سوالی دارید؟";

    public const string PersonalQuestionEn = "I'm just a docs assistant, so I don't have personal opinions to share — but I'm here for anything about using Liara's services.";
    public const string PersonalQuestionFa = "من فقط یک دستیار مستندات هستم و نظر شخصی‌ای ندارم — اما برای هر سوالی درباره استفاده از سرویس‌های لیارا در خدمتتان هستم.";

    public const string GeneralKnowledgeEn = "That's outside what I can help with — I'm focused on Liara's services and hosting/deployment topics. Is there something about Liara I can help you with?";
    public const string GeneralKnowledgeFa = "این موضوع خارج از حوزه کمک من است — من روی سرویس‌های لیارا و موضوعات میزبانی و استقرار تمرکز دارم. آیا سوالی درباره لیارا دارید که بتوانم کمک کنم؟";

    public const string JailbreakAttemptEn = "I can't change how I operate based on instructions in a message. I'm still happy to help with anything about Liara's services.";
    public const string JailbreakAttemptFa = "نمی‌توانم بر اساس دستورات داخل یک پیام، نحوه عملکردم را تغییر دهم. همچنان برای کمک درباره سرویس‌های لیارا در خدمتتان هستم.";

    public const string EscalationEn = "If this is still unresolved, Liara's support team can help further: {{support_channel_url}}";
    public const string EscalationFa = "اگر همچنان مشکل حل نشده، تیم پشتیبانی لیارا می‌تواند کمک بیشتری کند: {{support_channel_url}}";

    public const string RateLimitEn = "Too many requests — please wait a moment and try again.";
    public const string RateLimitFa = "تعداد درخواست‌ها بیش از حد مجاز است — لطفاً چند لحظه بعد دوباره تلاش کنید.";

    public const string SpendBudgetEn = "The assistant has reached its daily usage budget and paused AI-powered responses until tomorrow. Browsing the docs is still available.";
    public const string SpendBudgetFa = "دستیار به سقف مصرف روزانه رسیده و تا فردا پاسخ‌های هوش مصنوعی را متوقف کرده. مرور مستندات همچنان در دسترس است.";

    public const string InsufficientMaterialEn = "This topic doesn't have enough documented material yet for a good quiz — try Find-in-docs, or a narrower topic.";
    public const string InsufficientMaterialFa = "مستندات کافی برای طراحی یک آزمون خوب درباره این موضوع وجود ندارد — می‌توانید از جست‌وجو در مستندات استفاده کنید یا موضوع را دقیق‌تر بیان کنید.";

    public static string ForScope(string? scope, string? reason, string locale) =>
        scope == RouterResult.ScopeTrivial
            ? Pick(locale, TrivialEn, TrivialFa)
            : reason switch
            {
                "meta_question" => Pick(locale, MetaQuestionEn, MetaQuestionFa),
                "personal_question" => Pick(locale, PersonalQuestionEn, PersonalQuestionFa),
                "general_knowledge" => Pick(locale, GeneralKnowledgeEn, GeneralKnowledgeFa),
                "jailbreak_attempt" => Pick(locale, JailbreakAttemptEn, JailbreakAttemptFa),
                _ => Pick(locale, GeneralKnowledgeEn, GeneralKnowledgeFa),
            };

    public static string Pick(string locale, string en, string fa) =>
        locale == "fa" ? fa : en;

    /// <summary>
    /// /api/search carries no locale field, so the response locale is derived
    /// from the query's own script: any Arabic-block (Persian) characters → fa.
    /// </summary>
    public static string DetectLocale(string text) =>
        text.Any(c => c is >= '\u0600' and <= '\u06FF' or >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF')
            ? "fa"
            : "en";
}

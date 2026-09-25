import { Button } from "@/components/ui/button";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";

export function ErrorBanner({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const { locale } = useSession();
  return (
    <div className="border-destructive/50 bg-destructive/10 text-destructive flex items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm">
      <span dir="auto">{message}</span>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          {t(locale).common.retry}
        </Button>
      )}
    </div>
  );
}

import { Progress } from "@/components/ui/progress";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";

export function PracticeProgressBar({ index, total }: { index: number; total: number }) {
  const { locale } = useSession();
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-muted-foreground text-xs">{t(locale).practice.stepOf(index + 1, total)}</span>
      <Progress value={((index + 1) / total) * 100} />
    </div>
  );
}

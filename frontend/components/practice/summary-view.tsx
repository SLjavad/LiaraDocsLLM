import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { CitationList } from "@/components/shared/citation-list";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { PracticeSummaryResponse } from "@/lib/types";

export function SummaryView({
  summary,
  onRestart,
}: {
  summary: PracticeSummaryResponse;
  onRestart: () => void;
}) {
  const { locale } = useSession();

  return (
    <div className="mx-auto flex w-full max-w-2xl flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle dir="auto">{t(locale).practice.summaryTitle}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-1">
          <p dir="auto" className="text-lg font-semibold">
            {t(locale).practice.scoreLabel(summary.score.correct, summary.score.total)}
          </p>
          <p dir="auto" className="text-muted-foreground text-sm">
            {summary.topic}
          </p>
        </CardContent>
      </Card>

      <div className="flex flex-col gap-4">
        {summary.steps.map((step) => (
          <Card key={step.index}>
            <CardHeader className="flex-row items-center justify-between gap-2 pb-2">
              <CardTitle dir="auto" className="text-sm font-medium">
                {step.question}
              </CardTitle>
              <Badge variant={step.isCorrect ? "default" : "destructive"} className={cn(step.isCorrect && "bg-green-600")}>
                {step.isCorrect ? t(locale).practice.correct : t(locale).practice.incorrect}
              </Badge>
            </CardHeader>
            <CardContent className="flex flex-col gap-2">
              <Separator />
              <p dir="auto" className="text-muted-foreground text-sm">
                {step.explanation}
              </p>
              <CitationList citations={[step.source]} />
            </CardContent>
          </Card>
        ))}
      </div>

      <Button onClick={onRestart} className="w-fit">
        {t(locale).practice.restart}
      </Button>
    </div>
  );
}

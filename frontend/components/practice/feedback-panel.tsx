import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { CitationList } from "@/components/shared/citation-list";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { PracticeAnswerResponse, PracticeStep } from "@/lib/types";

export function FeedbackPanel({
  step,
  result,
  onAdvance,
}: {
  step: PracticeStep;
  result: PracticeAnswerResponse;
  onAdvance: () => void;
}) {
  const { locale } = useSession();

  return (
    <Card className={cn(result.isCorrect ? "border-green-500/50" : "border-destructive/50")}>
      <CardHeader>
        <CardTitle
          dir="auto"
          className={cn("text-sm font-semibold", result.isCorrect ? "text-green-600" : "text-destructive")}
        >
          {result.isCorrect ? t(locale).practice.correct : t(locale).practice.incorrect}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {!result.isCorrect && (
          <p dir="auto" className="text-sm">
            <span className="font-medium">{t(locale).practice.correctAnswerWas}</span>{" "}
            {step.options[result.correctIndex]}
          </p>
        )}
        <p dir="auto" className="text-muted-foreground text-sm">
          {result.explanation}
        </p>
        <CitationList citations={[result.source]} />
        <Button onClick={onAdvance} className="mt-2 w-fit">
          {result.next ? t(locale).practice.next : t(locale).practice.seeSummary}
        </Button>
      </CardContent>
    </Card>
  );
}

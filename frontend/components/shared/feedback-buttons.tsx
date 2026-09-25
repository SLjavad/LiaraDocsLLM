"use client";

import { useState } from "react";
import { ThumbsUp, ThumbsDown } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useSession } from "@/contexts/session-provider";
import { postFeedback } from "@/lib/api";
import { t } from "@/lib/i18n";
import type { FeedbackVote } from "@/lib/types";

export function FeedbackButtons({ messageId }: { messageId: string }) {
  const { sessionId, locale } = useSession();
  const [vote, setVote] = useState<FeedbackVote | null>(null);
  const [pending, setPending] = useState(false);

  const submit = async (next: FeedbackVote) => {
    if (!sessionId || pending || vote) return;
    setPending(true);
    try {
      await postFeedback(sessionId, messageId, next);
      setVote(next);
    } catch {
      toast.error(t(locale).common.error);
    } finally {
      setPending(false);
    }
  };

  return (
    <div className="mt-2 flex items-center gap-1">
      <span className="text-muted-foreground text-xs">{t(locale).common.feedbackPrompt}</span>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6"
        disabled={pending || vote !== null}
        aria-pressed={vote === "up"}
        onClick={() => submit("up")}
      >
        <ThumbsUp className={vote === "up" ? "fill-current" : ""} size={14} />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6"
        disabled={pending || vote !== null}
        aria-pressed={vote === "down"}
        onClick={() => submit("down")}
      >
        <ThumbsDown className={vote === "down" ? "fill-current" : ""} size={14} />
      </Button>
    </div>
  );
}

import { Badge } from "@/components/ui/badge";
import { MarkdownRenderer } from "@/components/shared/markdown-renderer";
import { CitationList } from "@/components/shared/citation-list";
import { FeedbackButtons } from "@/components/shared/feedback-buttons";
import { LoadingDots } from "@/components/shared/loading-dots";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { ChatUiMessage } from "./types";

export function MessageBubble({ message }: { message: ChatUiMessage }) {
  const { locale } = useSession();

  if (message.role === "user") {
    return (
      <div className="flex justify-end">
        <div dir="auto" className="bg-primary text-primary-foreground max-w-[85%] rounded-2xl px-4 py-2 text-sm">
          {message.content}
        </div>
      </div>
    );
  }

  const isRefusal = message.kind === "scope_refusal";
  const isTriage = message.kind === "triage";
  const isEscalation = message.kind === "escalation";

  return (
    <div className="flex justify-start">
      <div
        className={cn(
          "max-w-[90%] rounded-2xl border px-4 py-3 text-sm",
          isRefusal && "bg-muted text-muted-foreground border-transparent",
          isTriage && "border-primary/40 bg-primary/5 border-l-4",
          isEscalation && "border-amber-400/60 bg-amber-50 dark:bg-amber-950/20",
          !isRefusal && !isTriage && !isEscalation && "bg-card",
        )}
      >
        {isTriage && message.triageRound && (
          <Badge variant="outline" className="mb-2">
            {t(locale).chat.triageBadge(message.triageRound)}
          </Badge>
        )}
        {isEscalation && (
          <Badge variant="outline" className="mb-2 border-amber-500 text-amber-700 dark:text-amber-400">
            {t(locale).chat.escalationBadge}
          </Badge>
        )}

        {message.content ? (
          <MarkdownRenderer content={message.content} />
        ) : message.status === "streaming" ? (
          <LoadingDots />
        ) : null}

        {message.kind === "answer" && message.sources && message.sources.length > 0 && (
          <CitationList citations={message.sources} />
        )}

        {!isRefusal && !isTriage && message.status === "complete" && (
          <FeedbackButtons messageId={message.id} />
        )}
      </div>
    </div>
  );
}

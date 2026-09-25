"use client";

import { useEffect, useRef } from "react";
import { ScrollArea } from "@/components/ui/scroll-area";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import { MessageBubble } from "./message-bubble";
import type { ChatUiMessage } from "./types";

export function MessageList({ messages }: { messages: ChatUiMessage[] }) {
  const { locale } = useSession();
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  return (
    <ScrollArea className="flex-1">
      <div className="mx-auto flex max-w-3xl flex-col gap-4 px-4 py-6">
        {messages.length === 0 && (
          <p dir="auto" className="text-muted-foreground text-center text-sm">
            {t(locale).chat.emptyState}
          </p>
        )}
        {messages.map((m) => (
          <MessageBubble key={m.id} message={m} />
        ))}
        <div ref={bottomRef} />
      </div>
    </ScrollArea>
  );
}

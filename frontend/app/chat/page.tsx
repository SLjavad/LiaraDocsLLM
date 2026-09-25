"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { MessageList } from "@/components/chat/message-list";
import { ChatInput } from "@/components/chat/chat-input";
import type { ChatUiMessage } from "@/components/chat/types";
import { useSession } from "@/contexts/session-provider";
import { useChatStream } from "@/hooks/use-chat-stream";
import { getSessionMessages } from "@/lib/api";
import { t } from "@/lib/i18n";

export default function ChatPage() {
  const { sessionId, locale } = useSession();
  const { send } = useChatStream();
  const [messages, setMessages] = useState<ChatUiMessage[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!sessionId) return;
    getSessionMessages(sessionId)
      .then(({ messages: history }) => {
        setMessages(
          history.map((m) => ({
            id: m.id,
            role: m.role,
            content: m.content,
            kind: m.sources && m.sources.length > 0 ? "answer" : undefined,
            sources: m.sources ?? undefined,
            status: "complete" as const,
          })),
        );
      })
      .catch(() => {
        // First-ever visit for a brand-new sessionId 404s/empty-lists silently — nothing to resume.
      });
  }, [sessionId]);

  const handleSend = () => {
    const text = input.trim();
    if (!text || !sessionId || busy) return;

    setInput("");
    setBusy(true);

    const userMessage: ChatUiMessage = {
      id: crypto.randomUUID(),
      role: "user",
      content: text,
      status: "complete",
    };
    const assistantId = crypto.randomUUID();
    const assistantMessage: ChatUiMessage = {
      id: assistantId,
      role: "assistant",
      content: "",
      status: "streaming",
    };
    setMessages((prev) => [...prev, userMessage, assistantMessage]);

    const patchAssistant = (patch: Partial<ChatUiMessage>) => {
      setMessages((prev) => prev.map((m) => (m.id === assistantId ? { ...m, ...patch } : m)));
    };

    send(sessionId, text, {
      onMeta: (meta) => {
        patchAssistant({
          kind: meta.kind,
          reason: "reason" in meta ? meta.reason : undefined,
          triageRound: "triageRound" in meta ? meta.triageRound : undefined,
        });
      },
      onToken: (delta) => {
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, content: m.content + delta } : m)),
        );
      },
      onSources: (sources) => patchAssistant({ sources }),
      onDone: (messageId) => {
        patchAssistant({ id: messageId, status: "complete" });
        setBusy(false);
      },
      onError: (error) => {
        toast.error(error.message || t(locale).common.error);
        patchAssistant({ status: "complete" });
        setBusy(false);
      },
    });
  };

  return (
    <div className="flex h-full flex-1 flex-col overflow-hidden">
      <MessageList messages={messages} />
      <ChatInput value={input} onChange={setInput} onSend={handleSend} disabled={busy} />
    </div>
  );
}

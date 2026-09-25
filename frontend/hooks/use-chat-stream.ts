"use client";

import { useCallback, useRef } from "react";
import { API_BASE_URL } from "@/lib/api";
import type { ChatErrorEvent, ChatMetaEvent, ChatSource } from "@/lib/types";

type ChatStreamCallbacks = {
  onMeta: (meta: ChatMetaEvent) => void;
  onToken: (delta: string) => void;
  onSources: (sources: ChatSource[]) => void;
  onDone: (messageId: string) => void;
  onError: (error: ChatErrorEvent) => void;
};

// Custom SSE client (per specs/05-frontend-plan.md §4) — the Vercel AI SDK's
// useChat expects a different data-stream protocol than our meta/token/
// sources/done/error events, so we parse the stream by hand instead.
export function useChatStream() {
  const abortRef = useRef<AbortController | null>(null);

  const send = useCallback(
    async (sessionId: string, message: string, callbacks: ChatStreamCallbacks) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      try {
        const res = await fetch(`${API_BASE_URL}/api/chat`, {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-Session-Id": sessionId },
          body: JSON.stringify({ sessionId, message }),
          signal: controller.signal,
        });

        if (!res.ok || !res.body) {
          const body = await res.json().catch(() => null);
          callbacks.onError({
            message: body?.error ?? "Failed to reach the assistant.",
            retryable: true,
          });
          return;
        }

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });

          const frames = buffer.split("\n\n");
          buffer = frames.pop() ?? "";

          for (const frame of frames) {
            const lines = frame.split("\n");
            let eventName = "message";
            let data = "";
            for (const line of lines) {
              if (line.startsWith("event:")) eventName = line.slice(6).trim();
              else if (line.startsWith("data:")) data += line.slice(5).trim();
            }
            if (!data) continue;

            const payload = JSON.parse(data);
            switch (eventName) {
              case "meta":
                callbacks.onMeta(payload as ChatMetaEvent);
                break;
              case "token":
                callbacks.onToken(payload.delta as string);
                break;
              case "sources":
                callbacks.onSources(payload.sources as ChatSource[]);
                break;
              case "done":
                callbacks.onDone(payload.messageId as string);
                break;
              case "error":
                callbacks.onError(payload as ChatErrorEvent);
                break;
            }
          }
        }
      } catch (err) {
        if ((err as Error).name === "AbortError") return;
        callbacks.onError({ message: "Connection to the assistant was lost.", retryable: true });
      }
    },
    [],
  );

  const cancel = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  return { send, cancel };
}

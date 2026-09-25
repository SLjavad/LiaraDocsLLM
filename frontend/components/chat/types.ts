import type { ChatMetaEvent, ChatSource } from "@/lib/types";

export type ChatUiMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  kind?: ChatMetaEvent["kind"];
  reason?: string;
  triageRound?: number;
  sources?: ChatSource[];
  status: "streaming" | "complete";
};

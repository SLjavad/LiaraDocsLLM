"use client";

import { Streamdown } from "streamdown";

export function MarkdownRenderer({ content }: { content: string }) {
  return (
    <div dir="auto" className="prose prose-sm dark:prose-invert max-w-none break-words">
      <Streamdown>{content}</Streamdown>
    </div>
  );
}

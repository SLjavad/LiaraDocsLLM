"use client";

import { useRef, type KeyboardEvent } from "react";
import { Send, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";

export function ChatInput({
  value,
  onChange,
  onSend,
  disabled,
}: {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  disabled: boolean;
}) {
  const { locale } = useSession();
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      if (value.trim() && !disabled) onSend();
    }
  };

  const explainError = () => {
    onChange(t(locale).chat.explainErrorPlaceholder);
    textareaRef.current?.focus();
  };

  return (
    <div className="bg-background border-t px-4 py-3">
      <div className="mx-auto flex max-w-3xl items-end gap-2">
        <Button variant="ghost" size="icon" title={t(locale).chat.explainError} onClick={explainError}>
          <AlertTriangle size={18} />
        </Button>
        <Textarea
          ref={textareaRef}
          dir="auto"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={t(locale).chat.placeholder}
          rows={1}
          className="max-h-40 min-h-10 flex-1 resize-none"
        />
        <Button size="icon" disabled={disabled || !value.trim()} onClick={onSend}>
          <Send size={16} />
        </Button>
      </div>
    </div>
  );
}

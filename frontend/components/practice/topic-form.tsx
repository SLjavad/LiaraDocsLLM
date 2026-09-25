"use client";

import { useState, type KeyboardEvent } from "react";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";

export function TopicForm({
  onSubmit,
  disabled,
}: {
  onSubmit: (description: string) => void;
  disabled: boolean;
}) {
  const { locale } = useSession();
  const [value, setValue] = useState("");

  const submit = () => {
    const text = value.trim();
    if (!text || disabled) return;
    onSubmit(text);
    setValue("");
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
      <Textarea
        dir="auto"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder={t(locale).practice.topicPlaceholder}
        rows={3}
      />
      <Button onClick={submit} disabled={disabled || !value.trim()}>
        {t(locale).practice.start}
      </Button>
    </div>
  );
}

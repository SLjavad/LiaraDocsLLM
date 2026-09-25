"use client";

import { useState, type KeyboardEvent } from "react";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";

export function ClarificationPrompt({
  question,
  triageRound,
  onAnswer,
  disabled,
}: {
  question: string;
  triageRound: number;
  onAnswer: (answer: string) => void;
  disabled: boolean;
}) {
  const { locale } = useSession();
  const [value, setValue] = useState("");

  const submit = () => {
    const text = value.trim();
    if (!text || disabled) return;
    onAnswer(text);
    setValue("");
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") submit();
  };

  return (
    <div className="border-primary/40 bg-primary/5 mx-auto flex w-full max-w-xl flex-col gap-3 rounded-2xl border-l-4 px-4 py-3">
      <Badge variant="outline" className="w-fit">
        {t(locale).chat.triageBadge(triageRound)}
      </Badge>
      <p dir="auto" className="text-sm">
        {question}
      </p>
      <div className="flex gap-2">
        <Input
          dir="auto"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={t(locale).practice.answerPlaceholder}
          className="flex-1"
        />
        <Button onClick={submit} disabled={disabled || !value.trim()}>
          {t(locale).chat.send}
        </Button>
      </div>
    </div>
  );
}

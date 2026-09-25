"use client";

import { useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Button } from "@/components/ui/button";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import type { PracticeStep } from "@/lib/types";

export function QuestionCard({
  step,
  onSubmit,
  disabled,
}: {
  step: PracticeStep;
  onSubmit: (selectedIndex: number) => void;
  disabled: boolean;
}) {
  const { locale } = useSession();
  // "" (never undefined) is the "nothing selected" sentinel — RadioGroup must
  // stay controlled from the very first render, or Base UI logs an
  // uncontrolled->controlled warning the moment a real value is picked.
  const [selected, setSelected] = useState("");

  return (
    <Card>
      <CardHeader>
        <CardTitle dir="auto" className="text-base font-medium">
          {step.question}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <RadioGroup value={selected} onValueChange={setSelected} className="gap-3">
          {step.options.map((option, i) => (
            <div key={i} className="flex items-center gap-2">
              <RadioGroupItem value={String(i)} id={`option-${step.index}-${i}`} />
              <label
                dir="auto"
                htmlFor={`option-${step.index}-${i}`}
                className="cursor-pointer text-sm leading-none"
              >
                {option}
              </label>
            </div>
          ))}
        </RadioGroup>
        <Button
          disabled={disabled || selected === ""}
          onClick={() => selected !== "" && onSubmit(Number(selected))}
        >
          {t(locale).practice.submit}
        </Button>
      </CardContent>
    </Card>
  );
}

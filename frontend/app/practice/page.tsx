"use client";

import { useState } from "react";
import { toast } from "sonner";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { TopicForm } from "@/components/practice/topic-form";
import { ClarificationPrompt } from "@/components/practice/clarification-prompt";
import { PracticeProgressBar } from "@/components/practice/progress-bar";
import { QuestionCard } from "@/components/practice/question-card";
import { FeedbackPanel } from "@/components/practice/feedback-panel";
import { SummaryView } from "@/components/practice/summary-view";
import { useSession } from "@/contexts/session-provider";
import { postPracticeStart, postPracticeAnswer, getPracticeSummary, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import type { PracticeAnswerResponse, PracticeStep, PracticeSummaryResponse } from "@/lib/types";

type PracticeState =
  | { phase: "topic" }
  | { phase: "clarifying"; question: string; triageRound: number }
  | { phase: "out_of_scope"; message: string }
  | { phase: "insufficient_material"; message: string }
  | { phase: "question"; examId: string; topic: string; stepCount: number; step: PracticeStep }
  | {
      phase: "feedback";
      examId: string;
      topic: string;
      stepCount: number;
      step: PracticeStep;
      result: PracticeAnswerResponse;
    }
  | { phase: "summary"; summary: PracticeSummaryResponse };

export default function PracticePage() {
  const { sessionId, locale } = useSession();
  const [state, setState] = useState<PracticeState>({ phase: "topic" });
  const [busy, setBusy] = useState(false);

  const restart = () => setState({ phase: "topic" });

  const handleStart = async (description: string) => {
    if (!sessionId || busy) return;
    setBusy(true);
    try {
      const res = await postPracticeStart(sessionId, description);
      switch (res.status) {
        case "out_of_scope":
          setState({ phase: "out_of_scope", message: res.message });
          break;
        case "needs_clarification":
          setState({ phase: "clarifying", question: res.question, triageRound: res.triageRound });
          break;
        case "insufficient_material":
          setState({ phase: "insufficient_material", message: res.message });
          break;
        case "ready":
          setState({ phase: "question", examId: res.examId, topic: res.topic, stepCount: res.stepCount, step: res.step });
          break;
      }
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : t(locale).common.error);
    } finally {
      setBusy(false);
    }
  };

  const handleAnswer = async (selectedIndex: number) => {
    if (state.phase !== "question" || !sessionId || busy) return;
    setBusy(true);
    try {
      const result = await postPracticeAnswer(sessionId, state.examId, state.step.index, selectedIndex);
      setState({
        phase: "feedback",
        examId: state.examId,
        topic: state.topic,
        stepCount: state.stepCount,
        step: state.step,
        result,
      });
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : t(locale).common.error);
    } finally {
      setBusy(false);
    }
  };

  const handleAdvance = async () => {
    if (state.phase !== "feedback" || !sessionId || busy) return;
    if (state.result.next) {
      setState({
        phase: "question",
        examId: state.examId,
        topic: state.topic,
        stepCount: state.stepCount,
        step: state.result.next,
      });
      return;
    }
    setBusy(true);
    try {
      const summary = await getPracticeSummary(sessionId, state.examId);
      setState({ phase: "summary", summary });
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : t(locale).common.error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 overflow-y-auto px-4 py-6">
      {state.phase === "topic" && <TopicForm onSubmit={handleStart} disabled={busy} />}

      {state.phase === "clarifying" && (
        <ClarificationPrompt
          question={state.question}
          triageRound={state.triageRound}
          onAnswer={handleStart}
          disabled={busy}
        />
      )}

      {state.phase === "out_of_scope" && (
        <>
          <div dir="auto" className="bg-muted text-muted-foreground rounded-md px-4 py-3 text-sm">
            {state.message}
          </div>
          <Button variant="outline" onClick={restart} className="w-fit">
            {t(locale).practice.restart}
          </Button>
        </>
      )}

      {state.phase === "insufficient_material" && (
        <>
          <div dir="auto" className="bg-muted text-muted-foreground rounded-md px-4 py-3 text-sm">
            {state.message}
          </div>
          <Button variant="outline" onClick={restart} className="w-fit">
            {t(locale).practice.restart}
          </Button>
        </>
      )}

      {state.phase === "question" && (
        <>
          <PracticeProgressBar index={state.step.index} total={state.stepCount} />
          {busy ? <Skeleton className="h-40 w-full" /> : (
            <QuestionCard step={state.step} onSubmit={handleAnswer} disabled={busy} />
          )}
        </>
      )}

      {state.phase === "feedback" && (
        <>
          <PracticeProgressBar index={state.step.index} total={state.stepCount} />
          <FeedbackPanel step={state.step} result={state.result} onAdvance={handleAdvance} />
        </>
      )}

      {state.phase === "summary" && <SummaryView summary={state.summary} onRestart={restart} />}
    </div>
  );
}

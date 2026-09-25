"use client";

import { useEffect } from "react";
import { useSession } from "@/contexts/session-provider";

// Static UI chrome (nav, buttons) follows the locale, not per-element auto
// detection (that's only for user/model content — see MarkdownRenderer and
// message bubbles). shadcn's logical-CSS-property setup mirrors the layout
// automatically once `dir` changes here.
export function DirectionSync() {
  const { locale } = useSession();

  useEffect(() => {
    document.documentElement.dir = locale === "fa" ? "rtl" : "ltr";
    document.documentElement.lang = locale;
  }, [locale]);

  return null;
}

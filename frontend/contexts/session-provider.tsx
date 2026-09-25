"use client";

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { Locale } from "@/lib/types";

const SESSION_ID_KEY = "liara-docs-assistant:session-id";
const LOCALE_KEY = "liara-docs-assistant:locale";

type SessionContextValue = {
  sessionId: string | null;
  locale: Locale;
  setLocale: (locale: Locale) => void;
};

const SessionContext = createContext<SessionContextValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [locale, setLocaleState] = useState<Locale>("fa");

  useEffect(() => {
    // One-time sync from localStorage, an external system only readable on
    // the client — can't be done in useState's lazy initializer without
    // risking an SSR/client hydration mismatch.
    let id = localStorage.getItem(SESSION_ID_KEY);
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(SESSION_ID_KEY, id);
    }
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setSessionId(id);

    const storedLocale = localStorage.getItem(LOCALE_KEY);
    if (storedLocale === "fa" || storedLocale === "en") {
      setLocaleState(storedLocale);
    }
  }, []);

  const setLocale = (next: Locale) => {
    setLocaleState(next);
    localStorage.setItem(LOCALE_KEY, next);
  };

  const value = useMemo(() => ({ sessionId, locale, setLocale }), [sessionId, locale]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const ctx = useContext(SessionContext);
  if (!ctx) {
    throw new Error("useSession must be used within a SessionProvider");
  }
  return ctx;
}

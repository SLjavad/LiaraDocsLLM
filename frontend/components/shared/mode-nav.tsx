"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { MessageCircle, Search, GraduationCap } from "lucide-react";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

const items = [
  { href: "/chat", key: "chat" as const, icon: MessageCircle },
  { href: "/search", key: "search" as const, icon: Search },
  { href: "/practice", key: "practice" as const, icon: GraduationCap },
];

export function ModeNav() {
  const pathname = usePathname();
  const { locale, setLocale } = useSession();
  const nav = t(locale).nav;

  return (
    <>
      <header className="bg-background sticky top-0 z-10 flex items-center justify-between border-b px-4 py-2 md:px-6">
        <span className="text-sm font-semibold">{t(locale).appName}</span>
        <nav className="hidden gap-1 md:flex">
          {items.map(({ href, key, icon: Icon }) => {
            const active = pathname?.startsWith(href);
            return (
              <Link
                key={href}
                href={href}
                className={cn(
                  "flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm transition-colors",
                  active ? "bg-secondary font-medium" : "text-muted-foreground hover:bg-secondary/50",
                )}
              >
                <Icon size={16} />
                {nav[key]}
              </Link>
            );
          })}
        </nav>
        <button
          onClick={() => setLocale(locale === "fa" ? "en" : "fa")}
          className="text-muted-foreground hover:text-foreground text-xs underline underline-offset-2"
        >
          {locale === "fa" ? "EN" : "فا"}
        </button>
      </header>

      <nav className="bg-background fixed bottom-0 left-0 right-0 z-10 flex border-t md:hidden">
        {items.map(({ href, key, icon: Icon }) => {
          const active = pathname?.startsWith(href);
          return (
            <Link
              key={href}
              href={href}
              className={cn(
                "flex flex-1 flex-col items-center gap-0.5 py-2 text-xs",
                active ? "text-primary font-medium" : "text-muted-foreground",
              )}
            >
              <Icon size={18} />
              {nav[key]}
            </Link>
          );
        })}
      </nav>
    </>
  );
}

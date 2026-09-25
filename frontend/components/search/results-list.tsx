import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";
import type { SearchResult } from "@/lib/types";
import { ResultCard } from "./result-card";

export function ResultsList({ results }: { results: SearchResult[] }) {
  const { locale } = useSession();

  if (results.length === 0) {
    return <p className="text-muted-foreground text-center text-sm">{t(locale).search.noResults}</p>;
  }

  const grouped = new Map<string | null, SearchResult[]>();
  for (const r of results) {
    const key = r.matchedSubQuery ?? null;
    grouped.set(key, [...(grouped.get(key) ?? []), r]);
  }

  const hasSubQueries = [...grouped.keys()].some((k) => k !== null);

  return (
    <div className="flex flex-col gap-6">
      {[...grouped.entries()].map(([subQuery, items]) => (
        <div key={subQuery ?? "_all"} className="flex flex-col gap-3">
          {hasSubQueries && subQuery && (
            <h3 dir="auto" className="text-muted-foreground text-xs font-medium uppercase tracking-wide">
              {subQuery}
            </h3>
          )}
          {items.map((r, i) => (
            <ResultCard key={`${r.url}-${r.anchor ?? ""}-${i}`} result={r} />
          ))}
        </div>
      ))}
    </div>
  );
}

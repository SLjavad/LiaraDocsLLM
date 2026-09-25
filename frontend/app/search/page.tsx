"use client";

import { useState, type KeyboardEvent } from "react";
import { Search as SearchIcon } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { CategoryFilter } from "@/components/search/category-filter";
import { ResultsList } from "@/components/search/results-list";
import { useSession } from "@/contexts/session-provider";
import { postSearch, ApiError } from "@/lib/api";
import { t } from "@/lib/i18n";
import type { SearchResponse } from "@/lib/types";

export default function SearchPage() {
  const { sessionId, locale } = useSession();
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const [response, setResponse] = useState<SearchResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const runSearch = async () => {
    const q = query.trim();
    if (!q || loading) return;
    setLoading(true);
    setError(null);
    try {
      const res = await postSearch(sessionId, q, category, locale);
      setResponse(res);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t(locale).common.error);
    } finally {
      setLoading(false);
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") runSearch();
  };

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-4 overflow-y-auto px-4 py-6">
      <div className="flex flex-col gap-2 sm:flex-row">
        <Input
          dir="auto"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={t(locale).search.placeholder}
          className="w-full sm:flex-1"
        />
        <div className="flex gap-2">
          <CategoryFilter value={category} onChange={setCategory} />
          <Button onClick={runSearch} disabled={loading || !query.trim()} className="flex-1 sm:flex-none">
            <SearchIcon size={16} />
            {t(locale).search.button}
          </Button>
        </div>
      </div>

      {loading && (
        <div className="flex flex-col gap-3">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-20 w-full" />
        </div>
      )}

      {error && <p className="text-destructive text-sm">{error}</p>}

      {!loading && response && response.scope !== "in_scope" && (
        <div className="bg-muted text-muted-foreground rounded-md px-4 py-3 text-sm" dir="auto">
          {response.message}
        </div>
      )}

      {!loading && response && response.scope === "in_scope" && (
        <>
          <p className="text-muted-foreground text-xs">{t(locale).search.matchedFor(query)}</p>
          <ResultsList results={response.results} />
        </>
      )}
    </div>
  );
}
